namespace FirmaPro.Domena;

/// <summary>Po co składany jest plik.</summary>
public enum CelZlozenia
{
    /// <summary>Złożenie deklaracji po raz pierwszy za dany okres.</summary>
    Pierwotny = 1,

    /// <summary>Korekta deklaracji już złożonej.</summary>
    Korekta = 2
}

/// <summary>
/// Wyliczona część deklaracyjna JPK_V7.
/// </summary>
/// <remarks>
/// <para>
/// Kwoty podawane są w <b>pełnych złotych</b> - tego wymaga część
/// deklaracyjna, w odróżnieniu od ewidencyjnej, która zostaje w groszach.
/// Dlatego pola są typu całkowitego: gdyby zostały dziesiętne, prędzej czy
/// później ktoś wpisałby do nich grosze.
/// </para>
/// <para>
/// Zaokrąglana jest suma w każdym polu, a nie każdy dokument z osobna -
/// przy większej liczbie faktur zaokrąglanie po drodze rozjechałoby
/// deklarację z ewidencją nawet o kilka złotych. Pola podsumowujące
/// (P_38, P_48) liczone są natomiast z <b>już zaokrąglonych</b> pól
/// składowych, żeby deklaracja zgadzała się sama ze sobą - inaczej suma
/// pokazana na dole nie odpowiadałaby pozycjom widocznym wyżej.
/// </para>
/// </remarks>
public sealed class DeklaracjaVat
{
    /// <summary>Pole „wysokość nadwyżki do przeniesienia na następny okres".</summary>
    private const int PoleDoPrzeniesienia = 62;

    private readonly Dictionary<int, long> _pola = [];

    private DeklaracjaVat(OkresRozliczeniowy okres)
    {
        Okres = okres;
    }

    public OkresRozliczeniowy Okres { get; }

    /// <summary>Zastrzeżenia do danych, z których powstała deklaracja.</summary>
    public WynikWalidacji Walidacja { get; } = new();

    /// <summary>Wartość pola deklaracji; zero, gdy pole nie wystąpiło.</summary>
    public long Pole(int numer) => _pola.GetValueOrDefault(numer);

    /// <summary>Czy pole zostało w ogóle wypełnione.</summary>
    public bool MaPole(int numer) => _pola.ContainsKey(numer);

    /// <summary>Wypełnione pola, w kolejności numerów.</summary>
    public IReadOnlyList<KeyValuePair<int, long>> WypelnionePola =>
        _pola.Where(p => p.Value != 0).OrderBy(p => p.Key).ToList();

    // --- pola podsumowujące, wyliczane z pozostałych ------------------------

    /// <summary>Łączna wysokość podatku należnego (P_38).</summary>
    public long PodatekNalezny => Pole(38);

    /// <summary>Łączna wysokość podatku naliczonego do odliczenia (P_48).</summary>
    public long PodatekNaliczony => Pole(48);

    /// <summary>Nadwyżka z poprzedniej deklaracji (P_39).</summary>
    public long NadwyzkaZPoprzedniegoOkresu => Pole(39);

    /// <summary>Wysokość podatku podlegająca wpłacie do urzędu (P_51).</summary>
    public long DoWplaty => Pole(51);

    /// <summary>Wysokość nadwyżki podatku naliczonego nad należnym (P_53).</summary>
    public long Nadwyzka => Pole(53);

    /// <summary>
    /// Kwota przechodząca na następny okres.
    /// </summary>
    /// <remarks>
    /// Cała nadwyżka, bo program nie obsługuje wniosku o zwrot na rachunek -
    /// to osobna decyzja podatnika, wraz z wyborem terminu zwrotu.
    /// Ta wartość jest źródłem pola „nadwyżka z poprzedniej deklaracji"
    /// w następnym okresie.
    /// </remarks>
    public long DoPrzeniesienia => Nadwyzka;

    /// <summary>
    /// Składa deklarację z rejestru VAT za ten sam okres.
    /// </summary>
    /// <param name="rejestr">Rejestr sprzedaży i zakupów za okres.</param>
    /// <param name="nadwyzkaZPoprzedniegoOkresu">
    /// Kwota z pola „nadwyżka do przeniesienia" poprzedniej deklaracji.
    /// </param>
    public static DeklaracjaVat Zbuduj(RejestrVat rejestr,
                                       long nadwyzkaZPoprzedniegoOkresu = 0)
    {
        ArgumentNullException.ThrowIfNull(rejestr);
        ArgumentOutOfRangeException.ThrowIfNegative(nadwyzkaZPoprzedniegoOkresu);

        var deklaracja = new DeklaracjaVat(rejestr.Okres);

        deklaracja.WypelnijSprzedaz(rejestr.Sprzedaz);
        deklaracja.WypelnijZakupy(rejestr.Zakupy, nadwyzkaZPoprzedniegoOkresu);
        deklaracja.WyliczRozliczenie();

        return deklaracja;
    }

    private void WypelnijSprzedaz(RejestrSprzedazy sprzedaz)
    {
        foreach (KwotyWStawce kwoty in sprzedaz.WedlugStawek)
        {
            PolaSprzedazy? pola = PolaJpk.DlaStawki(kwoty.Stawka);

            if (pola is null)
            {
                // Ryczałt dla taksówek rozlicza się deklaracją VAT-12,
                // a nie JPK_V7 - milczące pominięcie kwoty byłoby gorsze
                // niż powiedzenie o tym wprost.
                Walidacja.Ostrzez("Sprzedaz",
                    $"sprzedaż w stawce {kwoty.Stawka.Opis} " +
                    $"({Kwoty.NaXml(kwoty.Netto)} zł) nie ma odpowiednika " +
                    "w deklaracji JPK_V7 i nie została w niej ujęta");

                continue;
            }

            Dodaj(pola.PoleNetto, kwoty.Netto);

            if (pola.PoleVat is int poleVat)
            {
                Dodaj(poleVat, kwoty.Vat);
            }

            if (pola.PoleDodatkowe is int poleDodatkowe)
            {
                Dodaj(poleDodatkowe, kwoty.Netto);
            }
        }

        Ustaw(38, PolaJpk.PolaPodatkuNaleznego.Sum(Pole));
    }

    private void WypelnijZakupy(RejestrZakupow zakupy, long nadwyzka)
    {
        Dodaj(PolaJpk.NettoSrodkiTrwale, zakupy.NettoSrodkiTrwale);
        Dodaj(PolaJpk.VatSrodkiTrwale, zakupy.VatSrodkiTrwale);
        Dodaj(PolaJpk.NettoPozostale, zakupy.NettoPozostale);
        Dodaj(PolaJpk.VatPozostale, zakupy.VatPozostale);

        Ustaw(39, nadwyzka);

        // Podatek naliczony do odliczenia obejmuje nadwyżkę przeniesioną
        // z poprzedniego okresu - dlatego nie jest to zwykła suma zakupów.
        Ustaw(48, nadwyzka + Pole(PolaJpk.VatSrodkiTrwale) + Pole(PolaJpk.VatPozostale));
    }

    private void WyliczRozliczenie()
    {
        long roznica = PodatekNalezny - PodatekNaliczony;

        Ustaw(51, roznica > 0 ? roznica : 0);
        Ustaw(53, roznica < 0 ? -roznica : 0);

        // Cała nadwyżka przechodzi domyślnie na następny okres. Wniosek
        // o zwrot na rachunek to osobna decyzja podatnika, wraz z terminem
        // zwrotu - program jej za niego nie podejmuje.
        //
        // UWAGA: numeru tego pola nie udało się potwierdzić oficjalnym
        // schematem - jest wypisany w README wśród miejsc do sprawdzenia.
        // Gdyby był błędny, walidator Ministerstwa odrzuci plik przy
        // pierwszej próbie, więc pomyłka wyjdzie głośno, a nie po cichu.
        Ustaw(PoleDoPrzeniesienia, Pole(53));
    }

    private void Dodaj(int numer, decimal kwota)
    {
        long zaokraglona = Kwoty.ZaokraglijDoZlotych(kwota);
        if (zaokraglona == 0)
        {
            return;
        }

        _pola[numer] = Pole(numer) + zaokraglona;
    }

    private void Ustaw(int numer, long wartosc)
    {
        if (wartosc == 0)
        {
            _pola.Remove(numer);
            return;
        }

        _pola[numer] = wartosc;
    }
}
