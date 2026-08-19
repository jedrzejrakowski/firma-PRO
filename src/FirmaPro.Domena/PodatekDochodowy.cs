namespace FirmaPro.Domena;

/// <summary>
/// Skala podatkowa obowiązująca w danym roku.
/// </summary>
/// <remarks>
/// <para>
/// Kwota wolna nie jest odliczana od podstawy - działa przez <b>kwotę
/// zmniejszającą podatek</b>, czyli podatek od kwoty wolnej. Przy 30 000 zł
/// wolnych i stawce 12% wychodzi 3 600 zł, i to ta kwota schodzi z podatku.
/// Odejmowanie 30 000 od podstawy dałoby zupełnie inny wynik.
/// </para>
/// <para>
/// Kwota zmniejszająca liczy się <b>narastająco</b>, nie co miesiąc. Cały
/// rok dostaje jedno zmniejszenie o 3 600 zł, a nie dwanaście po 300.
/// </para>
/// </remarks>
/// <param name="Rok">Rok podatkowy.</param>
/// <param name="KwotaWolna">Dochód wolny od podatku.</param>
/// <param name="Prog">Granica pierwszego przedziału skali.</param>
/// <param name="StawkaPierwsza">Stawka do progu.</param>
/// <param name="StawkaDruga">Stawka ponad progiem.</param>
public sealed record SkalaPodatkowa(
    int Rok,
    decimal KwotaWolna,
    decimal Prog,
    decimal StawkaPierwsza,
    decimal StawkaDruga)
{
    /// <summary>Kwota zmniejszająca podatek - podatek od kwoty wolnej.</summary>
    public decimal KwotaZmniejszajaca => Kwoty.Zaokraglij(KwotaWolna * StawkaPierwsza);

    /// <summary>Podatek od podstawy według skali; nigdy ujemny.</summary>
    public decimal Podatek(decimal podstawa)
    {
        if (podstawa <= 0m)
        {
            return 0m;
        }

        decimal podatek = podstawa <= Prog
            ? (podstawa * StawkaPierwsza) - KwotaZmniejszajaca
            : (Prog * StawkaPierwsza) - KwotaZmniejszajaca
              + ((podstawa - Prog) * StawkaDruga);

        return Math.Max(0m, Kwoty.Zaokraglij(podatek));
    }

    /// <summary>Czy podstawa przekroczyła próg drugiej stawki.</summary>
    public bool PonadProgiem(decimal podstawa) => podstawa > Prog;

    /// <summary>
    /// Skale wpisane w programie.
    /// </summary>
    /// <remarks>
    /// Skala stoi niezmieniona od połowy 2022 roku, ale to nie znaczy, że tak
    /// zostanie - dlatego jest tablicą po latach, a nie stałą. Roku, którego
    /// program nie zna, nie liczy skalą z innego.
    /// </remarks>
    public static IReadOnlyList<SkalaPodatkowa> Wpisane { get; } =
    [
        new SkalaPodatkowa(2024, 30_000m, 120_000m, 0.12m, 0.32m),
        new SkalaPodatkowa(2025, 30_000m, 120_000m, 0.12m, 0.32m),
        new SkalaPodatkowa(2026, 30_000m, 120_000m, 0.12m, 0.32m)
    ];

    /// <summary>Skala dla roku; puste, gdy program go nie zna.</summary>
    public static SkalaPodatkowa? Dla(int rok) => Wpisane.FirstOrDefault(s => s.Rok == rok);
}

/// <summary>Zaliczka na podatek w jednej stawce ryczałtu.</summary>
/// <param name="Stawka">Stawka ryczałtu w procentach.</param>
/// <param name="Przychod">Przychód narastająco w tej stawce.</param>
/// <param name="Odliczenie">Część odliczeń przypadająca na tę stawkę.</param>
public sealed record RyczaltWStawce(decimal Stawka, decimal Przychod, decimal Odliczenie)
{
    /// <summary>Przychód po odliczeniu składek; nigdy ujemny.</summary>
    public decimal Podstawa => Math.Max(0m, Kwoty.Zaokraglij(Przychod - Odliczenie));

    /// <summary>Podatek narastająco w tej stawce.</summary>
    public long Podatek => StawkiRyczaltu.Podatek(Podstawa, Stawka);
}

/// <summary>
/// Zaliczka na podatek dochodowy za okres.
/// </summary>
/// <param name="Okres">Okres, za który liczona jest zaliczka.</param>
/// <param name="PodstawaPrzedOdliczeniem">Dochód albo przychód narastająco.</param>
/// <param name="Odliczenia">Składki i strata odliczone od podstawy.</param>
/// <param name="Podstawa">Podstawa opodatkowania w pełnych złotych.</param>
/// <param name="PodatekNarastajaco">Podatek od początku roku.</param>
/// <param name="ZaplaconeWczesniej">Suma zaliczek za okresy poprzednie.</param>
/// <param name="Termin">Termin zapłaty, przesunięty na dzień roboczy.</param>
/// <param name="WedlugStawek">Rozbicie na stawki - tylko przy ryczałcie.</param>
public sealed record ZaliczkaPit(
    OkresRozliczeniowy Okres,
    decimal PodstawaPrzedOdliczeniem,
    decimal Odliczenia,
    long Podstawa,
    long PodatekNarastajaco,
    long ZaplaconeWczesniej,
    DateOnly Termin,
    IReadOnlyList<RyczaltWStawce> WedlugStawek)
{
    /// <summary>
    /// Kwota do zapłaty za ten okres.
    /// </summary>
    /// <remarks>
    /// Nigdy ujemna. Gdy podatek narastająco spadł poniżej już wpłaconych
    /// zaliczek - bo w okresie była strata albo duża korekta - zaliczki się
    /// nie płaci, ale i nie odzyskuje w trakcie roku. Nadpłata wychodzi
    /// dopiero w zeznaniu rocznym.
    /// </remarks>
    public long DoZaplaty => Math.Max(0L, PodatekNarastajaco - ZaplaconeWczesniej);

    /// <summary>Czy w tym okresie jest cokolwiek do zapłaty.</summary>
    public bool CosDoZaplaty => DoZaplaty > 0L;

    /// <summary>Czy termin minął.</summary>
    public bool PoTerminie(DateOnly dzis) => Termin < dzis;
}

/// <summary>
/// Zaliczki na podatek dochodowy od osób fizycznych.
/// </summary>
/// <remarks>
/// <para>
/// Zaliczkę liczy się od dochodu (albo przychodu) <b>narastająco od
/// 1 stycznia</b>, a nie od wyniku samego okresu (art. 44 ust. 3 ustawy
/// o PIT). Od policzonego tak podatku odejmuje się zaliczki za okresy
/// wcześniejsze. Dzięki temu strata w jednym miesiącu sama koryguje
/// zawyżoną zaliczkę z miesiąca poprzedniego.
/// </para>
/// <para>
/// Podstawę i podatek zaokrągla się do <b>pełnych złotych</b> (art. 63 § 1
/// Ordynacji podatkowej) - grosze w zaliczce sugerowałyby dokładność, której
/// przepis nie przewiduje.
/// </para>
/// </remarks>
public static class PodatekDochodowy
{
    /// <summary>Stawka podatku liniowego.</summary>
    public const decimal StawkaLiniowa = 0.19m;

    /// <summary>Dzień miesiąca, w którym mija termin zapłaty zaliczki.</summary>
    /// <remarks>
    /// Art. 44 ust. 6 ustawy o PIT. Zaliczka za grudzień płatna jest do
    /// 20 stycznia na zwykłych zasadach - dawna reguła nakazująca zapłacić
    /// ją wcześniej już nie obowiązuje.
    /// </remarks>
    public const int DzienTerminu = 20;

    /// <summary>Termin zapłaty zaliczki za okres.</summary>
    public static DateOnly Termin(OkresRozliczeniowy okres)
    {
        ArgumentNullException.ThrowIfNull(okres);

        return DniRobocze.NajblizszyRoboczy(
            okres.OstatniDzien.AddDays(1).AddDays(DzienTerminu - 1));
    }

    /// <summary>
    /// Zaliczka przy skali podatkowej albo podatku liniowym.
    /// </summary>
    /// <param name="okres">Okres, za który liczona jest zaliczka.</param>
    /// <param name="dochodNarastajaco">
    /// Dochód narastająco - z uwzględnieniem zakupu towarów i remanentów,
    /// a nie sama różnica kolumn 9 i 14.
    /// </param>
    /// <param name="odliczenia">
    /// Składki społeczne, zdrowotna podlegająca odliczeniu i strata z lat
    /// ubiegłych.
    /// </param>
    /// <param name="skala">Skala podatkowa; puste przy podatku liniowym.</param>
    /// <param name="zaplaconeWczesniej">Suma zaliczek za okresy poprzednie.</param>
    public static ZaliczkaPit Zaliczka(OkresRozliczeniowy okres,
                                       decimal dochodNarastajaco,
                                       decimal odliczenia,
                                       SkalaPodatkowa? skala,
                                       long zaplaconeWczesniej)
    {
        ArgumentNullException.ThrowIfNull(okres);

        decimal poOdliczeniu = dochodNarastajaco - odliczenia;

        // Podstawę zaokrągla się do złotych przed policzeniem podatku,
        // nie po - inaczej wynik różniłby się od tego z deklaracji.
        long podstawa = poOdliczeniu <= 0m
            ? 0L
            : Kwoty.ZaokraglijDoZlotych(poOdliczeniu);

        decimal podatek = skala is null
            ? podstawa * StawkaLiniowa
            : skala.Podatek(podstawa);

        return new ZaliczkaPit(
            okres,
            Kwoty.Zaokraglij(dochodNarastajaco),
            Kwoty.Zaokraglij(odliczenia),
            podstawa,
            Kwoty.ZaokraglijDoZlotych(podatek),
            zaplaconeWczesniej,
            Termin(okres),
            []);
    }

    /// <summary>
    /// Zaliczka przy ryczałcie od przychodów ewidencjonowanych.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Podatek liczy się osobno w każdej stawce i dopiero sumuje. Odliczenia
    /// dzieli się między stawki <b>proporcjonalnie do udziału przychodu</b>
    /// każdej z nich - odliczenie w całości od najwyższej stawki byłoby
    /// korzystniejsze, ale przepis go nie przewiduje.
    /// </para>
    /// <para>
    /// Przy działalności w jednej stawce podział nie zmienia niczego; przy
    /// mieszanej rozstrzyga o kwocie podatku.
    /// </para>
    /// </remarks>
    /// <param name="okres">Okres, za który liczona jest zaliczka.</param>
    /// <param name="przychody">Przychód narastająco w rozbiciu na stawki.</param>
    /// <param name="odliczenia">
    /// Składki społeczne i połowa zapłaconej zdrowotnej.
    /// </param>
    /// <param name="zaplaconeWczesniej">Suma zaliczek za okresy poprzednie.</param>
    public static ZaliczkaPit ZaliczkaRyczalt(OkresRozliczeniowy okres,
                                              IEnumerable<PrzychodWStawce> przychody,
                                              decimal odliczenia,
                                              long zaplaconeWczesniej)
    {
        ArgumentNullException.ThrowIfNull(okres);
        ArgumentNullException.ThrowIfNull(przychody);

        List<PrzychodWStawce> lista = [.. przychody.Where(p => p.Przychod > 0m)];

        decimal razem = lista.Sum(p => p.Przychod);

        List<RyczaltWStawce> stawki = [];

        // Reszta z podziału trafia do ostatniej stawki, żeby suma odliczeń
        // zgadzała się co do grosza z kwotą faktycznie zapłaconych składek.
        decimal rozdzielone = 0m;

        for (int i = 0; i < lista.Count; i++)
        {
            PrzychodWStawce p = lista[i];

            decimal udzial = i == lista.Count - 1
                ? Kwoty.Zaokraglij(odliczenia - rozdzielone)
                : Kwoty.Zaokraglij(odliczenia * p.Przychod / razem);

            rozdzielone += udzial;

            stawki.Add(new RyczaltWStawce(p.Stawka, p.Przychod, udzial));
        }

        long podatek = stawki.Sum(s => s.Podatek);

        return new ZaliczkaPit(
            okres,
            Kwoty.Zaokraglij(razem),
            Kwoty.Zaokraglij(Math.Min(odliczenia, razem)),
            stawki.Sum(s => Kwoty.ZaokraglijDoZlotych(s.Podstawa)),
            podatek,
            zaplaconeWczesniej,
            Termin(okres),
            stawki);
    }
}
