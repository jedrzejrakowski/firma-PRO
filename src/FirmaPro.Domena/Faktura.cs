namespace FirmaPro.Domena;

/// <summary>Rodzaj faktury (typ TRodzajFaktury schematu FA(3)).</summary>
public enum RodzajFaktury
{
    /// <summary>Faktura podstawowa.</summary>
    Vat,
    /// <summary>Faktura korygująca.</summary>
    Korygujaca,
    /// <summary>Faktura zaliczkowa.</summary>
    Zaliczkowa,
    /// <summary>Faktura rozliczeniowa (art. 106f ust. 3 ustawy).</summary>
    Rozliczeniowa,
    /// <summary>Faktura uproszczona.</summary>
    Uproszczona,
    /// <summary>Korekta faktury zaliczkowej.</summary>
    KorektaZaliczkowej,
    /// <summary>Korekta faktury rozliczeniowej.</summary>
    KorektaRozliczeniowej
}

/// <summary>Forma płatności (typ TFormaPlatnosci schematu FA(3)).</summary>
public enum FormaPlatnosci
{
    Gotowka = 1,
    Karta = 2,
    Bon = 3,
    Czek = 4,
    Kredyt = 5,
    Przelew = 6,
    Mobilna = 7
}

/// <summary>Pojedynczy wiersz faktury (element FaWiersz).</summary>
public sealed class PozycjaFaktury
{
    /// <summary>Nazwa towaru lub usługi (pole P_7).</summary>
    public string Nazwa { get; set; } = string.Empty;

    /// <summary>Jednostka miary, np. "szt.", "godz." (pole P_8A).</summary>
    public string Jednostka { get; set; } = "szt.";

    /// <summary>Ilość (pole P_8B).</summary>
    public decimal Ilosc { get; set; } = 1m;

    /// <summary>Cena jednostkowa netto (pole P_9A).</summary>
    public decimal CenaNetto { get; set; }

    /// <summary>Stawka podatku (pole P_12).</summary>
    public StawkaVat Stawka { get; set; } = StawkaVat.Vat23;

    /// <summary>Kod GTU_01..GTU_13, jeśli wymagany.</summary>
    public string? Gtu { get; set; }

    public string? Pkwiu { get; set; }
    public string? Cn { get; set; }

    /// <summary>Indeks własny towaru w magazynie.</summary>
    public string? Indeks { get; set; }

    /// <summary>Wartość netto pozycji (pole P_11) = ilość × cena.</summary>
    public decimal WartoscNetto => Kwoty.Zaokraglij(Ilosc * CenaNetto);

    /// <summary>Kwota podatku dla pozycji - zero przy stawkach bez podatku.</summary>
    public decimal KwotaVat => Stawka.PodatekOd(WartoscNetto);

    /// <summary>Wartość z podatkiem - używana tylko na wizualizacji.</summary>
    public decimal WartoscBrutto => WartoscNetto + KwotaVat;
}

/// <summary>Warunki płatności (sekcja Platnosc).</summary>
public sealed class WarunkiPlatnosci
{
    public FormaPlatnosci? Forma { get; set; } = FormaPlatnosci.Przelew;
    public DateOnly? Termin { get; set; }

    /// <summary>Numer rachunku (26 cyfr NRB, bez spacji).</summary>
    public string? Rachunek { get; set; }
    public string? NazwaBanku { get; set; }

    public bool Zaplacono { get; set; }
    public DateOnly? DataZaplaty { get; set; }

    /// <summary>Czy w sekcji jest cokolwiek wartego zapisania w pliku XML.</summary>
    public bool CzyPusta =>
        Forma is null && Termin is null && Rachunek is null
        && !Zaplacono && DataZaplaty is null;
}

/// <summary>
/// Sumy faktury w rozbiciu na pola P_13_* i P_14_* schematu FA(3).
/// </summary>
public sealed class PodsumowanieFaktury
{
    private readonly Dictionary<string, decimal> _pola = new(StringComparer.Ordinal);

    /// <summary>Kwota w danym polu podsumowania (zero, gdy pole nie wystąpiło).</summary>
    public decimal Pole(string nazwa) =>
        _pola.TryGetValue(nazwa, out decimal wartosc) ? wartosc : Kwoty.Zero;

    /// <summary>Czy pole w ogóle wystąpiło na tej fakturze.</summary>
    public bool MaPole(string nazwa) => _pola.ContainsKey(nazwa);

    public decimal RazemNetto { get; private set; }
    public decimal RazemVat { get; private set; }
    public decimal RazemBrutto => RazemNetto + RazemVat;

    /// <summary>Rozbicie na stawki - do tabeli podsumowania na wizualizacji.</summary>
    public IReadOnlyList<PozycjaPodsumowania> WedlugStawek { get; private set; } = [];

    internal void Dodaj(string pole, decimal kwota) =>
        _pola[pole] = Pole(pole) + kwota;

    internal void UstawSumy(decimal netto, decimal vat,
                            IReadOnlyList<PozycjaPodsumowania> wedlugStawek)
    {
        RazemNetto = netto;
        RazemVat = vat;
        WedlugStawek = wedlugStawek;
    }
}

/// <summary>Jeden wiersz zestawienia "netto, VAT, brutto w danej stawce".</summary>
public sealed record PozycjaPodsumowania(StawkaVat Stawka, decimal Netto, decimal Vat)
{
    public decimal Brutto => Netto + Vat;
}

/// <summary>
/// Typ skutku korekty w ewidencji VAT (pole TypKorekty).
/// </summary>
/// <remarks>
/// Decyduje o okresie, w którym korekta wchodzi do rejestru - a więc o tym,
/// czy trzeba poprawiać deklarację wstecz, czy wystarczy bieżąca.
/// </remarks>
public enum TypKorektyVat
{
    /// <summary>
    /// Skutek w dacie ujęcia faktury pierwotnej - typowo przy błędzie
    /// na fakturze, który istniał od początku.
    /// </summary>
    WDaciePierwotnej = 1,

    /// <summary>
    /// Skutek w dacie wystawienia korekty - typowo przy rabacie albo zwrocie
    /// towaru, czyli zdarzeniu, które nastąpiło później.
    /// </summary>
    WDacieKorekty = 2,

    /// <summary>Skutek w innej dacie, także gdy pozycje mają różne daty.</summary>
    WInnejDacie = 3
}

/// <summary>Dane faktury, której dotyczy korekta.</summary>
/// <param name="Numer">Numer faktury korygowanej.</param>
/// <param name="DataWystawienia">Data wystawienia faktury korygowanej.</param>
/// <param name="NumerKsef">
/// Numer KSeF faktury korygowanej albo <c>null</c>, gdy została wystawiona
/// poza systemem. Schemat wymaga wskazania jednego albo drugiego - nie da się
/// pominąć obu.
/// </param>
public sealed record DaneFakturyKorygowanej(
    string Numer,
    DateOnly DataWystawienia,
    string? NumerKsef);

/// <summary>
/// Faktura zaliczkowa rozliczana fakturą końcową.
/// </summary>
/// <remarks>
/// Poza numerem niesie kwotę: na wydruku trzeba pokazać, ile z wartości
/// dostawy nabywca już zapłacił, żeby nie zapłacił drugi raz.
/// </remarks>
public sealed record DaneZaliczki(
    string Numer,
    DateOnly DataWystawienia,
    string? NumerKsef,
    decimal Brutto);

/// <summary>Kompletna faktura.</summary>
public sealed class Faktura
{
    /// <summary>Numer nadany przez wystawcę (pole P_2).</summary>
    public string Numer { get; set; } = string.Empty;

    /// <summary>Data wystawienia (pole P_1).</summary>
    public DateOnly DataWystawienia { get; set; }

    /// <summary>
    /// Data dokonania dostawy lub wykonania usługi (pole P_6). Gdy jest równa
    /// dacie wystawienia, przepisy pozwalają ją pominąć.
    /// </summary>
    public DateOnly? DataSprzedazy { get; set; }

    /// <summary>Miejsce wystawienia (pole P_1M, nieobowiązkowe).</summary>
    public string? MiejsceWystawienia { get; set; }

    /// <summary>Trzyliterowy kod waluty.</summary>
    public string Waluta { get; set; } = "PLN";

    /// <summary>
    /// Kurs przeliczenia na złote; puste przy fakturze w złotówkach.
    /// </summary>
    /// <remarks>
    /// Faktura w walucie obcej musi wykazywać kwotę podatku w złotych
    /// (art. 106e ust. 11 ustawy), a schemat FA(3) chce dodatkowo samego
    /// kursu przy każdym wierszu.
    /// </remarks>
    public KursWaluty? Kurs { get; set; }

    /// <summary>Kurs do przeliczeń - jeden do jednego dla faktur złotowych.</summary>
    public decimal KursDoPrzeliczen => Kurs?.Wartosc ?? 1m;

    /// <summary>Czy faktura jest wystawiona w walucie innej niż złoty.</summary>
    public bool Walutowa =>
        !string.Equals(Waluta, "PLN", StringComparison.OrdinalIgnoreCase);

    public RodzajFaktury Rodzaj { get; set; } = RodzajFaktury.Vat;

    public Podmiot Sprzedawca { get; set; } = new();
    public Podmiot Nabywca { get; set; } = new();

    /// <summary>
    /// Podmioty trzecie związane z fakturą.
    /// </summary>
    /// <remarks>
    /// Odbiorca będący oddziałem nabywcy, faktor, dodatkowy nabywca, jednostka
    /// podrzędna samorządu. Struktura FA(3) dopuszcza do stu takich podmiotów,
    /// każdy z własną rolą - i to po nich KSeF udostępnia fakturę komuś innemu
    /// niż nabywca z sekcji Podmiot2.
    /// </remarks>
    public List<PodmiotInny> PodmiotyInne { get; set; } = [];

    /// <summary>
    /// Podmiot upoważniony do wystawienia faktury w imieniu podatnika.
    /// </summary>
    /// <remarks>
    /// Komornik, organ egzekucyjny albo przedstawiciel podatkowy. Sprzedawcą
    /// pozostaje podatnik - ta sekcja mówi tylko, kto dokument wystawił.
    /// </remarks>
    public PodmiotUpowazniony? Upowazniony { get; set; }

    /// <summary>
    /// Dane sprzedawcy sprzed korekty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wypełniane wyłącznie wtedy, gdy fakturą korygującą poprawia się dane
    /// samego sprzedawcy - nazwę albo adres (art. 106j ust. 2 pkt 3 ustawy).
    /// Wtedy trzeba podać pełne dane w brzmieniu z faktury korygowanej, bo
    /// inaczej nie widać, co właściwie zostało poprawione.
    /// </para>
    /// <para>
    /// Nie dotyczy błędnego numeru NIP: tego nie koryguje się w ten sposób,
    /// tylko fakturą do wartości zerowych i wystawieniem nowej.
    /// </para>
    /// </remarks>
    public Podmiot? SprzedawcaPrzedKorekta { get; set; }

    public List<PozycjaFaktury> Pozycje { get; set; } = [];
    public WarunkiPlatnosci Platnosc { get; set; } = new();

    /// <summary>Stopka faktury - dowolny tekst.</summary>
    public string? Stopka { get; set; }

    /// <summary>
    /// Podstawa prawna zwolnienia. Wymagana, gdy na fakturze jest choć jedna
    /// pozycja ze stawką "zw" - trafia do pola P_19A.
    /// </summary>
    public string? PodstawaZwolnienia { get; set; }

    /// <summary>Numer nadany przez KSeF po przyjęciu dokumentu.</summary>
    public string? NumerKsef { get; set; }

    // --- korekta ---------------------------------------------------------

    /// <summary>Przyczyna korekty - wypełniana tylko na fakturze korygującej.</summary>
    public string? PrzyczynaKorekty { get; set; }

    /// <summary>Typ skutku korekty w ewidencji VAT.</summary>
    public TypKorektyVat? TypKorekty { get; set; }

    /// <summary>Faktury, których dotyczy korekta.</summary>
    public List<DaneFakturyKorygowanej> Korygowane { get; set; } = [];

    /// <summary>
    /// Pozycje w stanie sprzed korekty.
    /// </summary>
    /// <remarks>
    /// Schemat dopuszcza wykazanie danych przed korektą i po korekcie jako
    /// osobnych wierszy (znacznik StanPrzed). Ta forma jest czytelna także
    /// na wydruku - odbiorca widzi, co się zmieniło, zamiast samej różnicy.
    /// W <see cref="Pozycje"/> siedzi stan po korekcie.
    /// </remarks>
    public List<PozycjaFaktury> PozycjePrzedKorekta { get; set; } = [];

    /// <summary>
    /// Pozycje zamówienia, na poczet którego wpłacono zaliczkę.
    /// </summary>
    /// <remarks>
    /// Wymagane na fakturze zaliczkowej (art. 106f ust. 1 pkt 4 ustawy):
    /// wiersze faktury pokazują samą wpłatę, więc bez zamówienia nie byłoby
    /// wiadomo, czego ta wpłata dotyczy.
    /// </remarks>
    public List<PozycjaZamowienia> Zamowienie { get; set; } = [];

    /// <summary>
    /// Faktury zaliczkowe rozliczane tą fakturą.
    /// </summary>
    /// <remarks>
    /// Wskazywane na fakturze końcowej (art. 106f ust. 3 ustawy) - to one
    /// mówią, ile z należności zostało już zafakturowane wcześniej.
    /// </remarks>
    public List<DaneZaliczki> Zaliczkowe { get; set; } = [];

    /// <summary>Ile z tej faktury zafakturowano już zaliczkami.</summary>
    public decimal ZafakturowaneZaliczkami =>
        Kwoty.Zaokraglij(Zaliczkowe.Sum(z => z.Brutto));

    /// <summary>
    /// Kwota, której wystawca żąda tym dokumentem.
    /// </summary>
    /// <remarks>
    /// Dla zwykłej faktury to jej wartość brutto. Faktura końcowa obejmuje
    /// całą dostawę, ale za zaliczki nabywca już zapłacił - do zapłaty
    /// zostaje różnica (art. 106f ust. 3 ustawy).
    /// </remarks>
    public decimal DoZaplaty =>
        Kwoty.Zaokraglij(Podsumowanie().RazemBrutto - ZafakturowaneZaliczkami);

    /// <summary>Czy dokument jest fakturą korygującą.</summary>
    public bool CzyKorekta =>
        Rodzaj is RodzajFaktury.Korygujaca
               or RodzajFaktury.KorektaZaliczkowej
               or RodzajFaktury.KorektaRozliczeniowej;

    /// <summary>
    /// Wylicza sumy faktury w rozbiciu na pola wymagane przez FA(3).
    /// </summary>
    /// <remarks>
    /// Podatek liczony jest od sumy wartości netto w danej stawce, a nie jako
    /// suma podatków z poszczególnych pozycji. Ta kolejność działań jest
    /// zgodna z art. 106e ustawy o VAT i z walidacją po stronie KSeF -
    /// przy trzech pozycjach po 0,10 zł daje 0,07 zł, a nie 0,06 zł.
    /// </remarks>
    public PodsumowanieFaktury Podsumowanie()
    {
        var nettoWgStawki = new Dictionary<StawkaVat, decimal>();
        foreach (PozycjaFaktury pozycja in Pozycje)
        {
            nettoWgStawki.TryGetValue(pozycja.Stawka, out decimal dotychczas);
            nettoWgStawki[pozycja.Stawka] = dotychczas + pozycja.WartoscNetto;
        }

        // Faktura korygująca wykazuje różnicę, a nie nowy stan. Odejmujemy
        // więc stan sprzed korekty - dzięki temu do rejestru VAT i deklaracji
        // trafia dokładnie to, o ile zmienia się podatek, a nie cała wartość
        // transakcji policzona po raz drugi.
        foreach (PozycjaFaktury pozycja in PozycjePrzedKorekta)
        {
            nettoWgStawki.TryGetValue(pozycja.Stawka, out decimal dotychczas);
            nettoWgStawki[pozycja.Stawka] = dotychczas - pozycja.WartoscNetto;
        }

        var podsumowanie = new PodsumowanieFaktury();
        var wiersze = new List<PozycjaPodsumowania>();
        decimal razemNetto = Kwoty.Zero;
        decimal razemVat = Kwoty.Zero;

        // Kolejność stawek jak w StawkaVat.Wszystkie - żeby zestawienie
        // wyglądało tak samo niezależnie od kolejności pozycji na fakturze.
        foreach (StawkaVat stawka in StawkaVat.Wszystkie)
        {
            if (!nettoWgStawki.TryGetValue(stawka, out decimal netto))
            {
                continue;
            }

            netto = Kwoty.Zaokraglij(netto);
            decimal vat = stawka.PodatekOd(netto);

            podsumowanie.Dodaj(stawka.PoleNetto, netto);
            if (stawka.PoleVat is not null && vat != Kwoty.Zero)
            {
                podsumowanie.Dodaj(stawka.PoleVat, vat);
            }

            razemNetto += netto;
            razemVat += vat;
            wiersze.Add(new PozycjaPodsumowania(stawka, netto, vat));
        }

        podsumowanie.UstawSumy(razemNetto, razemVat, wiersze);
        return podsumowanie;
    }

    /// <summary>Kwota należności ogółem (pole P_15).</summary>
    public decimal KwotaDoZaplaty => Podsumowanie().RazemBrutto;

    /// <summary>Czy na fakturze jest sprzedaż zwolniona z podatku.</summary>
    public bool MaSprzedazZwolniona => Pozycje.Any(p => p.Stawka == StawkaVat.Zwolniona);

    /// <summary>Czy na fakturze jest sprzedaż w odwrotnym obciążeniu.</summary>
    public bool MaOdwrotneObciazenie =>
        Pozycje.Any(p => p.Stawka == StawkaVat.OdwrotneObciazenie);
}
