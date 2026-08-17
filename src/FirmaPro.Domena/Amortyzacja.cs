namespace FirmaPro.Domena;

/// <summary>
/// Metoda amortyzacji środka trwałego.
/// </summary>
/// <remarks>
/// Wybór metody jest decyzją podatnika, ale raz podjętą stosuje się do pełnego
/// zamortyzowania (art. 22h ust. 2 ustawy o PIT) - dlatego siedzi przy środku
/// trwałym, a nie w ustawieniach firmy.
/// </remarks>
public enum MetodaAmortyzacji
{
    /// <summary>
    /// Liniowa - równe odpisy przez cały okres.
    /// </summary>
    /// <remarks>Metoda podstawowa, art. 22h ust. 1 pkt 1 i art. 22i.</remarks>
    Liniowa = 0,

    /// <summary>
    /// Degresywna - większe odpisy na początku.
    /// </summary>
    /// <remarks>
    /// Art. 22k ust. 1. Odpis liczy się od wartości pomniejszonej o dotychczasowe
    /// odpisy, ze współczynnikiem podwyższającym stawkę. Od roku, w którym odpis
    /// liniowy byłby wyższy, przechodzi się na liniową - i to przejście jest
    /// obowiązkowe, nie wyborem.
    /// </remarks>
    Degresywna = 1,

    /// <summary>
    /// Jednorazowa - cała wartość w miesiącu przyjęcia.
    /// </summary>
    /// <remarks>
    /// Art. 22k ust. 7 (mały podatnik i rozpoczynający działalność) albo
    /// art. 22d ust. 1 przy wartości do 10 000 zł. Limit kwotowy pilnuje
    /// podatnik - program go nie zna, bo zależy od statusu firmy i od tego,
    /// ile z niego już wykorzystano w roku.
    /// </remarks>
    Jednorazowa = 2
}

/// <summary>
/// Środek trwały podlegający amortyzacji.
/// </summary>
/// <remarks>
/// Zakup środka trwałego nie jest kosztem miesiąca zakupu - kosztem są odpisy
/// amortyzacyjne (art. 22 ust. 8 ustawy o PIT). Dlatego faktura za samochód
/// nie wchodzi do księgi, a wchodzą comiesięczne odpisy liczone tutaj.
/// </remarks>
public sealed record SrodekTrwaly
{
    /// <summary>Nazwa środka - trafia do opisu zdarzenia w księdze.</summary>
    public string Nazwa { get; init; } = string.Empty;

    /// <summary>Numer inwentarzowy - trafia do kolumny „nr dowodu”.</summary>
    public string NumerInwentarzowy { get; init; } = string.Empty;

    /// <summary>
    /// Dzień przyjęcia do używania.
    /// </summary>
    /// <remarks>
    /// Nie dzień zakupu. Amortyzacja zaczyna się od miesiąca <b>następującego</b>
    /// po miesiącu przyjęcia (art. 22h ust. 1 pkt 1), więc sprzęt kupiony
    /// w marcu, a uruchomiony w maju, daje pierwszy odpis w czerwcu.
    /// </remarks>
    public DateOnly DataPrzyjecia { get; init; }

    /// <summary>Wartość początkowa - podstawa odpisów.</summary>
    public decimal WartoscPoczatkowa { get; init; }

    public MetodaAmortyzacji Metoda { get; init; } = MetodaAmortyzacji.Liniowa;

    /// <summary>Roczna stawka amortyzacji w procentach, z Wykazu stawek.</summary>
    public decimal StawkaRoczna { get; init; } = 20m;

    /// <summary>
    /// Współczynnik podwyższający przy metodzie degresywnej.
    /// </summary>
    /// <remarks>
    /// Nie więcej niż 2,0 (art. 22k ust. 1), a w gminach zagrożonych bezrobociem
    /// do 3,0. Przy metodzie liniowej nie ma znaczenia.
    /// </remarks>
    public decimal Wspolczynnik { get; init; } = 2.0m;

    /// <summary>
    /// Górna granica wartości, od której odpis jest kosztem.
    /// </summary>
    /// <remarks>
    /// Dla samochodu osobowego 150 000 zł, dla elektrycznego 225 000 zł
    /// (art. 23 ust. 1 pkt 4 ustawy o PIT). Odpis liczy się od pełnej wartości
    /// początkowej, ale kosztem jest tylko część przypadająca na kwotę
    /// mieszczącą się w limicie - reszta nie jest kosztem nigdy.
    /// </remarks>
    public decimal? LimitKosztu { get; init; }

    /// <summary>Dzień likwidacji albo sprzedaży - po nim odpisów już nie ma.</summary>
    public DateOnly? DataLikwidacji { get; init; }

    /// <summary>
    /// Pierwszy miesiąc, za który należy się odpis.
    /// </summary>
    /// <remarks>
    /// Miesiąc następujący po miesiącu przyjęcia do używania. Przy metodzie
    /// jednorazowej odpis i tak przypada w całości na ten jeden miesiąc.
    /// </remarks>
    public OkresRozliczeniowy PierwszyOkres =>
        OkresRozliczeniowy.Dla(DataPrzyjecia, TypOkresu.Miesieczny).Nastepny;

    /// <summary>
    /// Jaka część odpisu jest kosztem podatkowym.
    /// </summary>
    /// <remarks>
    /// Jeden przy braku limitu. Przy samochodzie droższym od limitu - proporcja
    /// limitu do wartości początkowej; tak liczy się tę część w praktyce.
    /// </remarks>
    public decimal UdzialKosztu =>
        LimitKosztu is { } limit && WartoscPoczatkowa > limit && WartoscPoczatkowa > 0
            ? limit / WartoscPoczatkowa
            : 1m;

    /// <summary>Czy część odpisów przepada przez limit wartości.</summary>
    public bool PrzekraczaLimit => UdzialKosztu < 1m;
}

/// <summary>Jeden miesięczny odpis amortyzacyjny.</summary>
/// <param name="Okres">Miesiąc, którego dotyczy odpis.</param>
/// <param name="Kwota">Pełna kwota odpisu.</param>
/// <param name="Koszt">Część odpisu stanowiąca koszt podatkowy.</param>
/// <param name="Umorzenie">Suma odpisów po tym miesiącu.</param>
public sealed record Odpis(OkresRozliczeniowy Okres, decimal Kwota, decimal Koszt,
                           decimal Umorzenie)
{
    /// <summary>Wartość, jaka pozostaje do zamortyzowania po tym odpisie.</summary>
    public decimal DoUmorzenia(decimal wartoscPoczatkowa) =>
        Kwoty.Zaokraglij(wartoscPoczatkowa - Umorzenie);
}

/// <summary>
/// Plan odpisów amortyzacyjnych.
/// </summary>
/// <remarks>
/// <para>
/// Plan liczony jest w całości od pierwszego do ostatniego odpisu, a nie
/// miesiąc po miesiącu. Metoda degresywna wymaga znajomości sumy odpisów
/// z lat poprzednich, a ostatni odpis musi wyrównać się do grosza z wartością
/// początkową - jednego i drugiego nie da się zrobić, patrząc na jeden miesiąc.
/// </para>
/// <para>
/// Zaokrąglenia zbierają się przez kilkadziesiąt miesięcy, więc ostatni odpis
/// jest różnicą, a nie kolejną ratą. Bez tego środek trwały zostawałby
/// z groszem niezamortyzowanym albo umorzenie przekraczałoby wartość.
/// </para>
/// </remarks>
public static class PlanAmortyzacji
{
    /// <summary>Ile najwyżej miesięcy liczymy - zabezpieczenie przed stawką zerową.</summary>
    private const int MaksymalnaLiczbaMiesiecy = 100 * 12;

    /// <summary>Buduje pełny plan odpisów dla środka trwałego.</summary>
    public static IReadOnlyList<Odpis> Zbuduj(SrodekTrwaly srodek)
    {
        ArgumentNullException.ThrowIfNull(srodek);

        if (srodek.WartoscPoczatkowa <= 0)
        {
            return [];
        }

        return srodek.Metoda switch
        {
            MetodaAmortyzacji.Jednorazowa => Jednorazowo(srodek),
            MetodaAmortyzacji.Degresywna => Degresywnie(srodek),
            _ => Liniowo(srodek)
        };
    }

    /// <summary>Odpisy przypadające na wskazany okres.</summary>
    public static Odpis? ZaOkres(SrodekTrwaly srodek, OkresRozliczeniowy okres)
    {
        ArgumentNullException.ThrowIfNull(okres);

        return Zbuduj(srodek).FirstOrDefault(o => o.Okres == okres);
    }

    private static List<Odpis> Jednorazowo(SrodekTrwaly srodek)
    {
        OkresRozliczeniowy okres = srodek.PierwszyOkres;

        if (Zlikwidowany(srodek, okres))
        {
            return [];
        }

        decimal kwota = Kwoty.Zaokraglij(srodek.WartoscPoczatkowa);

        return [new Odpis(okres, kwota, Koszt(srodek, kwota), kwota)];
    }

    private static List<Odpis> Liniowo(SrodekTrwaly srodek)
    {
        decimal miesieczny = Miesieczny(srodek.WartoscPoczatkowa, srodek.StawkaRoczna);

        return Rozpisz(srodek, _ => miesieczny);
    }

    /// <summary>
    /// Odpisy metodą degresywną.
    /// </summary>
    /// <remarks>
    /// W pierwszym roku podstawą jest wartość początkowa, w kolejnych - wartość
    /// pomniejszona o dotychczasowe umorzenie, ustalona na początek roku.
    /// Od roku, w którym odpis liniowy byłby wyższy, przechodzimy na liniowy
    /// liczony od wartości początkowej (art. 22k ust. 1 zdanie drugie).
    /// </remarks>
    private static List<Odpis> Degresywnie(SrodekTrwaly srodek)
    {
        decimal liniowyMiesieczny = Miesieczny(srodek.WartoscPoczatkowa, srodek.StawkaRoczna);
        decimal stawkaPodwyzszona = srodek.StawkaRoczna * srodek.Wspolczynnik;

        int rokPlanu = -1;
        decimal miesiecznyWRoku = 0m;
        bool juzLiniowo = false;

        return Rozpisz(srodek, stan =>
        {
            if (stan.Rok != rokPlanu)
            {
                rokPlanu = stan.Rok;

                decimal podstawa = stan.Rok == 0
                    ? srodek.WartoscPoczatkowa
                    : srodek.WartoscPoczatkowa - stan.Umorzenie;

                decimal degresywny = Miesieczny(podstawa, stawkaPodwyzszona);

                // Przejście na liniową jest jednokierunkowe - raz wykonane,
                // obowiązuje do końca amortyzacji.
                juzLiniowo = juzLiniowo || degresywny <= liniowyMiesieczny;

                miesiecznyWRoku = juzLiniowo ? liniowyMiesieczny : degresywny;
            }

            return miesiecznyWRoku;
        });
    }

    /// <summary>Miesięczna rata przy danej podstawie i stawce rocznej.</summary>
    private static decimal Miesieczny(decimal podstawa, decimal stawkaRoczna) =>
        Kwoty.Zaokraglij(podstawa * stawkaRoczna / 100m / 12m);

    /// <summary>Stan planu widziany przez regułę wyliczającą ratę.</summary>
    private readonly record struct StanPlanu(int Rok, decimal Umorzenie);

    /// <summary>
    /// Rozpisuje odpisy miesiąc po miesiącu aż do pełnego umorzenia.
    /// </summary>
    /// <remarks>
    /// Ostatni odpis jest różnicą do wartości początkowej, a nie kolejną ratą -
    /// zaokrąglenia zebrane przez kilkadziesiąt miesięcy zostawiłyby inaczej
    /// grosz nieumorzony albo przekroczyłyby wartość środka.
    /// </remarks>
    private static List<Odpis> Rozpisz(SrodekTrwaly srodek, Func<StanPlanu, decimal> rata)
    {
        var odpisy = new List<Odpis>();

        OkresRozliczeniowy okres = srodek.PierwszyOkres;
        decimal umorzenie = 0m;
        int rok = 0;
        int miesiacWRoku = 0;

        for (int i = 0; i < MaksymalnaLiczbaMiesiecy; i++)
        {
            if (Zlikwidowany(srodek, okres))
            {
                break;
            }

            decimal kwota = rata(new StanPlanu(rok, umorzenie));

            if (kwota <= 0)
            {
                break;
            }

            decimal pozostalo = Kwoty.Zaokraglij(srodek.WartoscPoczatkowa - umorzenie);

            if (kwota >= pozostalo)
            {
                kwota = pozostalo;
            }

            umorzenie = Kwoty.Zaokraglij(umorzenie + kwota);

            odpisy.Add(new Odpis(okres, kwota, Koszt(srodek, kwota), umorzenie));

            if (umorzenie >= srodek.WartoscPoczatkowa)
            {
                break;
            }

            okres = okres.Nastepny;

            // Rok amortyzacji liczymy w latach kalendarzowych, bo tak liczy je
            // ustawa - w kolejnych latach podatkowych, nie po dwunastu ratach.
            if (++miesiacWRoku >= 12 || okres.PierwszyDzien.Month == 1)
            {
                miesiacWRoku = 0;
                rok++;
            }
        }

        return odpisy;
    }

    /// <summary>Część odpisu stanowiąca koszt - pełna albo obcięta limitem.</summary>
    private static decimal Koszt(SrodekTrwaly srodek, decimal odpis) =>
        srodek.PrzekraczaLimit
            ? Kwoty.Zaokraglij(odpis * srodek.UdzialKosztu)
            : odpis;

    private static bool Zlikwidowany(SrodekTrwaly srodek, OkresRozliczeniowy okres) =>
        srodek.DataLikwidacji is { } koniec && okres.PierwszyDzien > koniec;
}
