namespace FirmaPro.Domena;

/// <summary>
/// Tytuł, z jakiego przedsiębiorca opłaca składki.
/// </summary>
/// <remarks>
/// Kolejność jest zarazem kolejnością w czasie: ulga na start przez pierwsze
/// sześć miesięcy, potem preferencyjne składki przez dwadzieścia cztery,
/// potem Mały ZUS Plus albo od razu pełny. Wybór nie jest kosmetyczny -
/// między ulgą na start a pełnym ZUS-em różnica sięga tysiąca złotych
/// miesięcznie.
/// </remarks>
public enum TytulUbezpieczenia
{
    /// <summary>
    /// Ulga na start - przez sześć miesięcy tylko składka zdrowotna.
    /// </summary>
    /// <remarks>
    /// Art. 18 ust. 1 Prawa przedsiębiorców. Brak składek społecznych oznacza
    /// też brak ubezpieczenia chorobowego i brak tych miesięcy w stażu
    /// emerytalnym - to nie jest sama korzyść.
    /// </remarks>
    UlgaNaStart = 0,

    /// <summary>Preferencyjny - 30% minimalnego wynagrodzenia przez 24 miesiące.</summary>
    /// <remarks>Art. 18a ustawy o systemie ubezpieczeń społecznych.</remarks>
    Preferencyjny = 1,

    /// <summary>Mały ZUS Plus - podstawa liczona od dochodu poprzedniego roku.</summary>
    /// <remarks>Art. 18c ustawy o systemie ubezpieczeń społecznych.</remarks>
    MalyZusPlus = 2,

    /// <summary>Pełny ZUS - 60% prognozowanego przeciętnego wynagrodzenia.</summary>
    Pelny = 3
}

/// <summary>Jak firma opłaca składki - to, czego program nie policzy sam.</summary>
/// <param name="Tytul">Tytuł ubezpieczenia.</param>
/// <param name="Chorobowe">
/// Czy przedsiębiorca przystąpił do dobrowolnego ubezpieczenia chorobowego.
/// </param>
/// <param name="StopaWypadkowa">
/// Stopa składki wypadkowej - różna dla różnych płatników.
/// </param>
/// <param name="BezFunduszuPracy">
/// Czy płatnik jest zwolniony z Funduszu Pracy niezależnie od podstawy -
/// przysługuje kobietom po 55. i mężczyznom po 60. roku życia
/// (art. 104b ustawy o promocji zatrudnienia).
/// </param>
/// <param name="PodstawaMalegoZusPlus">
/// Podstawa ustalona na cały rok przy Małym ZUS Plus; ustala się ją raz,
/// w styczniu, i nie zmienia w trakcie roku.
/// </param>
public sealed record UstawieniaZus(
    TytulUbezpieczenia Tytul,
    bool Chorobowe = true,
    decimal StopaWypadkowa = StopyZus.WypadkowaMalyPlatnik,
    bool BezFunduszuPracy = false,
    decimal? PodstawaMalegoZusPlus = null);

/// <summary>Składki na ubezpieczenia społeczne za jeden miesiąc.</summary>
public sealed record SkladkiSpoleczne(
    decimal Podstawa,
    decimal Emerytalna,
    decimal Rentowa,
    decimal Chorobowe,
    decimal Wypadkowa,
    decimal FunduszPracy)
{
    /// <summary>Same ubezpieczenia społeczne, bez Funduszu Pracy.</summary>
    /// <remarks>
    /// To ta kwota - a nie suma z Funduszem Pracy - podlega odliczeniu
    /// od dochodu albo zaliczeniu do kosztów (art. 26 ust. 1 pkt 2 ustawy
    /// o PIT). Fundusz Pracy jest kosztem, ale odliczeniem od dochodu nie.
    /// </remarks>
    public decimal Ubezpieczenia =>
        Kwoty.Zaokraglij(Emerytalna + Rentowa + Chorobowe + Wypadkowa);

    /// <summary>Wszystko, co idzie jednym przelewem do ZUS z tytułu społecznych.</summary>
    public decimal Razem => Kwoty.Zaokraglij(Ubezpieczenia + FunduszPracy);

    /// <summary>Miesiąc bez składek społecznych - ulga na start.</summary>
    public static SkladkiSpoleczne Brak { get; } = new(0m, 0m, 0m, 0m, 0m, 0m);
}

/// <summary>Składka zdrowotna za jeden miesiąc.</summary>
/// <param name="Podstawa">Podstawa wymiaru.</param>
/// <param name="Kwota">Składka do zapłaty.</param>
/// <param name="ZNajnizszejPodstawy">
/// Czy składkę policzono od najniższej podstawy, bo dochód był niższy albo ujemny.
/// </param>
public sealed record SkladkaZdrowotna(decimal Podstawa, decimal Kwota, bool ZNajnizszejPodstawy);

/// <summary>Wszystkie składki za jeden miesiąc razem.</summary>
public sealed record SkladkiMiesiaca(
    OkresRozliczeniowy Okres,
    SkladkiSpoleczne Spoleczne,
    SkladkaZdrowotna Zdrowotna,
    DateOnly Termin)
{
    /// <summary>Cała kwota do zapłaty do ZUS za ten miesiąc.</summary>
    public decimal Razem => Kwoty.Zaokraglij(Spoleczne.Razem + Zdrowotna.Kwota);

    /// <summary>Czy termin zapłaty już minął.</summary>
    public bool PoTerminie(DateOnly dzis) => Termin < dzis;
}

/// <summary>
/// Rachunek składek przedsiębiorcy.
/// </summary>
/// <remarks>
/// <para>
/// Składki społeczne liczy się od podstawy, która nie ma nic wspólnego
/// z dochodem - jest ryczałtowa i zależy tylko od tytułu ubezpieczenia.
/// Składka zdrowotna od 2022 roku jest odwrotnie: <b>zależy od formy
/// opodatkowania i od dochodu</b>, i liczy się trzema różnymi wzorami.
/// To najczęstsze źródło zdziwienia, bo przez lata była stała.
/// </para>
/// <para>
/// Program liczy składki właściciela. Składki za pracowników to osobna
/// rzecz i osobny moduł - tu ich nie ma.
/// </para>
/// </remarks>
public static class Zus
{
    /// <summary>Dzień miesiąca, w którym mija termin zapłaty składek.</summary>
    /// <remarks>
    /// Art. 47 ust. 1 ustawy o systemie ubezpieczeń społecznych. Od 2022 roku
    /// jeden termin dla wszystkich płatników - wcześniej były trzy różne.
    /// </remarks>
    public const int DzienTerminu = 20;

    /// <summary>Termin zapłaty składek za wskazany miesiąc.</summary>
    public static DateOnly Termin(OkresRozliczeniowy okres)
    {
        ArgumentNullException.ThrowIfNull(okres);

        DateOnly nominalny = okres.OstatniDzien.AddDays(1)
            .AddDays(DzienTerminu - 1);

        return DniRobocze.NajblizszyRoboczy(nominalny);
    }

    // ---------------------------------------------------- składki społeczne

    /// <summary>Podstawa wymiaru składek społecznych.</summary>
    public static decimal PodstawaSpolecznych(UstawieniaZus ustawienia,
                                              StawkiZus stawki,
                                              int miesiac)
    {
        ArgumentNullException.ThrowIfNull(ustawienia);
        ArgumentNullException.ThrowIfNull(stawki);

        return ustawienia.Tytul switch
        {
            TytulUbezpieczenia.UlgaNaStart => 0m,
            TytulUbezpieczenia.Preferencyjny => stawki.PodstawaPreferencyjna(miesiac),

            // Podstawy Małego ZUS Plus program nie zgaduje - ustala się ją raz
            // w roku z dochodu roku poprzedniego. Bez niej rachunek byłby
            // wzięty z powietrza, więc spada do podstawy pełnej.
            TytulUbezpieczenia.MalyZusPlus =>
                ustawienia.PodstawaMalegoZusPlus ?? stawki.PodstawaPelna,

            _ => stawki.PodstawaPelna
        };
    }

    /// <summary>Składki społeczne za miesiąc.</summary>
    public static SkladkiSpoleczne Spoleczne(UstawieniaZus ustawienia,
                                             StawkiZus stawki,
                                             int miesiac)
    {
        ArgumentNullException.ThrowIfNull(ustawienia);
        ArgumentNullException.ThrowIfNull(stawki);

        if (ustawienia.Tytul == TytulUbezpieczenia.UlgaNaStart)
        {
            return SkladkiSpoleczne.Brak;
        }

        decimal podstawa = PodstawaSpolecznych(ustawienia, stawki, miesiac);

        return new SkladkiSpoleczne(
            podstawa,
            Kwoty.Zaokraglij(podstawa * StopyZus.Emerytalna),
            Kwoty.Zaokraglij(podstawa * StopyZus.Rentowa),
            ustawienia.Chorobowe ? Kwoty.Zaokraglij(podstawa * StopyZus.Chorobowa) : 0m,
            Kwoty.Zaokraglij(podstawa * ustawienia.StopaWypadkowa),
            NalezyFunduszPracy(ustawienia, stawki, miesiac, podstawa)
                ? Kwoty.Zaokraglij(podstawa * StopyZus.FunduszPracy)
                : 0m);
    }

    /// <summary>
    /// Czy za dany miesiąc należy się Fundusz Pracy.
    /// </summary>
    /// <remarks>
    /// Składkę opłaca się dopiero wtedy, gdy podstawa wymiaru w przeliczeniu
    /// na miesiąc sięga minimalnego wynagrodzenia (art. 104 ust. 1 ustawy
    /// o promocji zatrudnienia). Przy składkach preferencyjnych podstawa to
    /// 30% minimalnego, więc Funduszu Pracy nie ma - i to nie przeoczenie,
    /// tylko reguła.
    /// </remarks>
    public static bool NalezyFunduszPracy(UstawieniaZus ustawienia,
                                          StawkiZus stawki,
                                          int miesiac,
                                          decimal podstawa)
    {
        ArgumentNullException.ThrowIfNull(ustawienia);
        ArgumentNullException.ThrowIfNull(stawki);

        return !ustawienia.BezFunduszuPracy
            && podstawa >= stawki.MinimalneWMiesiacu(miesiac);
    }

    /// <summary>
    /// Podstawa Małego ZUS Plus z dochodu roku poprzedniego.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Art. 18c ust. 3-4 ustawy o systemie ubezpieczeń społecznych: dochód
    /// roczny dzieli się przez liczbę dni prowadzenia działalności, mnoży
    /// przez 30 i bierze połowę. Wynik nie może być niższy niż 30%
    /// minimalnego wynagrodzenia ani wyższy niż 60% prognozowanego
    /// przeciętnego - czyli niż podstawa pełnego ZUS.
    /// </para>
    /// <para>
    /// Liczba dni bierze się z tego, że ulga przysługuje także komuś, kto
    /// działalność zaczął albo zawiesił w trakcie roku - wtedy roczny dochód
    /// trzeba przeliczyć na pełny miesiąc, inaczej podstawa wyszłaby zaniżona.
    /// </para>
    /// </remarks>
    /// <param name="dochodPoprzedniegoRoku">Dochód z roku poprzedniego.</param>
    /// <param name="dniProwadzenia">Dni prowadzenia działalności w tamtym roku.</param>
    /// <param name="stawki">Kwoty roku, za który liczy się składki.</param>
    /// <param name="miesiac">Miesiąc, dla którego bierze się minimalne wynagrodzenie.</param>
    public static decimal PodstawaMalegoZusPlus(decimal dochodPoprzedniegoRoku,
                                                int dniProwadzenia,
                                                StawkiZus stawki,
                                                int miesiac = 1)
    {
        ArgumentNullException.ThrowIfNull(stawki);
        ArgumentOutOfRangeException.ThrowIfLessThan(dniProwadzenia, 1);

        decimal dolna = stawki.PodstawaPreferencyjna(miesiac);
        decimal gorna = stawki.PodstawaPelna;

        // Strata w poprzednim roku nie odbiera prawa do ulgi - podstawa
        // spada wtedy po prostu do dolnej granicy.
        if (dochodPoprzedniegoRoku <= 0m)
        {
            return dolna;
        }

        decimal przecietnyMiesieczny =
            Kwoty.Zaokraglij(dochodPoprzedniegoRoku / dniProwadzenia * 30m);

        decimal podstawa = Kwoty.Zaokraglij(przecietnyMiesieczny * 0.5m);

        return Math.Clamp(podstawa, dolna, gorna);
    }

    /// <summary>Czy przychód uprawnia do Małego ZUS Plus.</summary>
    public static bool MalyZusPlusPrzysluguje(decimal przychodPoprzedniegoRoku,
                                              StawkiZus stawki)
    {
        ArgumentNullException.ThrowIfNull(stawki);

        return przychodPoprzedniegoRoku <= stawki.LimitPrzychoduMalyZusPlus;
    }

    // ---------------------------------------------------- składka zdrowotna

    /// <summary>
    /// Składka zdrowotna za miesiąc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trzy formy opodatkowania, trzy różne wzory:
    /// </para>
    /// <list type="bullet">
    /// <item>skala - 9% dochodu,</item>
    /// <item>podatek liniowy - 4,9% dochodu,</item>
    /// <item>ryczałt - 9% podstawy zależnej od progu przychodu.</item>
    /// </list>
    /// <para>
    /// Przy skali i liniowym podstawą jest dochód <b>miesiąca poprzedniego</b>,
    /// a rok składkowy biegnie od lutego do stycznia. Wywołujący musi podać
    /// właściwy dochód - tego program nie zgadnie z samej daty.
    /// </para>
    /// </remarks>
    /// <param name="forma">Forma opodatkowania.</param>
    /// <param name="podstawaZDochodu">
    /// Dochód miesiąca poprzedniego przy skali i liniowym; przychód narastająco
    /// od początku roku przy ryczałcie.
    /// </param>
    /// <param name="stawki">Kwoty roku składkowego.</param>
    public static SkladkaZdrowotna Zdrowotna(FormaOpodatkowania forma,
                                             decimal podstawaZDochodu,
                                             StawkiZus stawki)
    {
        ArgumentNullException.ThrowIfNull(stawki);

        if (forma == FormaOpodatkowania.Ryczalt)
        {
            decimal podstawaRyczaltu = PodstawaZdrowotnejRyczalt(podstawaZDochodu, stawki);

            return new SkladkaZdrowotna(
                podstawaRyczaltu,
                Kwoty.Zaokraglij(podstawaRyczaltu * StopyZus.Zdrowotna),
                ZNajnizszejPodstawy: false);
        }

        decimal stopa = forma == FormaOpodatkowania.Liniowy
            ? StopyZus.ZdrowotnaLiniowy
            : StopyZus.Zdrowotna;

        decimal najnizsza = stawki.NajnizszaPodstawaZdrowotnej;

        // Składka od dochodu nie może być niższa niż 9% najniższej podstawy -
        // i to 9% także przy liniowym, mimo że dochód mnoży się przez 4,9%.
        decimal zDochodu = Kwoty.Zaokraglij(Math.Max(podstawaZDochodu, 0m) * stopa);
        decimal minimalna = Kwoty.Zaokraglij(najnizsza * StopyZus.Zdrowotna);

        return zDochodu <= minimalna
            ? new SkladkaZdrowotna(najnizsza, minimalna, ZNajnizszejPodstawy: true)
            : new SkladkaZdrowotna(Math.Max(podstawaZDochodu, 0m), zDochodu,
                                   ZNajnizszejPodstawy: false);
    }

    /// <summary>
    /// Podstawa zdrowotnej ryczałtowca według progu przychodu.
    /// </summary>
    /// <remarks>
    /// Trzy progi liczone od przychodu narastająco od początku roku: do 60 000 zł
    /// podstawą jest 60% przeciętnego wynagrodzenia, do 300 000 zł - 100%,
    /// powyżej - 180% (art. 81 ust. 2e ustawy o świadczeniach opieki zdrowotnej).
    /// Przekroczenie progu w listopadzie zmienia składkę za wszystkie miesiące
    /// roku, więc listopad potrafi zaskoczyć dopłatą.
    /// </remarks>
    public static decimal PodstawaZdrowotnejRyczalt(decimal przychodNarastajaco,
                                                    StawkiZus stawki)
    {
        ArgumentNullException.ThrowIfNull(stawki);

        decimal udzial = przychodNarastajaco switch
        {
            <= 60_000m => 0.60m,
            <= 300_000m => 1.00m,
            _ => 1.80m
        };

        return Kwoty.Zaokraglij(stawki.PrzecietneDoRyczaltu * udzial);
    }

    // ------------------------------------------------------------ odliczenia

    /// <summary>
    /// Ile zapłaconej składki zdrowotnej wolno odliczyć.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Od 2022 roku zdrowotnej nie odlicza się od podatku. Zostało z niej
    /// tyle:
    /// </para>
    /// <list type="bullet">
    /// <item><b>skala</b> - nic, ani złotówki,</item>
    /// <item><b>liniowy</b> - odliczenie od dochodu do rocznego limitu,</item>
    /// <item><b>ryczałt</b> - połowa zapłaconych składek, od przychodu.</item>
    /// </list>
    /// <para>
    /// Podatnik na skali, który pyta, gdzie w programie odlicza zdrowotną,
    /// nie przeoczył pola - takiego pola nie ma.
    /// </para>
    /// </remarks>
    public static decimal ZdrowotnaDoOdliczenia(FormaOpodatkowania forma,
                                                decimal zaplacona,
                                                StawkiZus stawki)
    {
        ArgumentNullException.ThrowIfNull(stawki);

        return forma switch
        {
            FormaOpodatkowania.Liniowy =>
                Kwoty.Zaokraglij(Math.Min(zaplacona, stawki.LimitOdliczeniaZdrowotnej)),

            FormaOpodatkowania.Ryczalt =>
                Kwoty.Zaokraglij(zaplacona * StopyZus.OdliczenieZdrowotnejRyczalt),

            _ => 0m
        };
    }

    /// <summary>
    /// Roczne rozliczenie składki zdrowotnej.
    /// </summary>
    /// <remarks>
    /// Składki płaci się co miesiąc od dochodu miesięcznego, a należne są
    /// od dochodu rocznego - te dwie kwoty nie muszą się zgadzać. Różnicę
    /// dopłaca się albo odbiera we wniosku składanym do 20 maja
    /// (art. 81 ust. 2j ustawy o świadczeniach opieki zdrowotnej).
    /// </remarks>
    /// <param name="forma">Forma opodatkowania.</param>
    /// <param name="podstawaRoczna">
    /// Dochód roczny przy skali i liniowym; roczna podstawa ryczałtowca
    /// (dwunastokrotność miesięcznej) przy ryczałcie.
    /// </param>
    /// <param name="zaplaconoWRoku">Suma składek zapłaconych w roku.</param>
    /// <param name="stawki">Kwoty roku składkowego.</param>
    public static RozliczenieZdrowotnej RozliczRoczna(FormaOpodatkowania forma,
                                                      decimal podstawaRoczna,
                                                      decimal zaplaconoWRoku,
                                                      StawkiZus stawki)
    {
        ArgumentNullException.ThrowIfNull(stawki);

        decimal stopa = forma == FormaOpodatkowania.Liniowy
            ? StopyZus.ZdrowotnaLiniowy
            : StopyZus.Zdrowotna;

        decimal nalezna = forma == FormaOpodatkowania.Ryczalt
            ? Kwoty.Zaokraglij(podstawaRoczna * StopyZus.Zdrowotna)
            : Kwoty.Zaokraglij(Math.Max(podstawaRoczna, 0m) * stopa);

        // Roczna też ma dolną granicę - dwunastokrotność najniższej miesięcznej.
        decimal minimalna = forma == FormaOpodatkowania.Ryczalt
            ? 0m
            : Kwoty.Zaokraglij(stawki.NajnizszaPodstawaZdrowotnej * StopyZus.Zdrowotna * 12m);

        nalezna = Math.Max(nalezna, minimalna);

        return new RozliczenieZdrowotnej(nalezna, Kwoty.Zaokraglij(zaplaconoWRoku));
    }
}

/// <summary>Wynik rocznego rozliczenia składki zdrowotnej.</summary>
public sealed record RozliczenieZdrowotnej(decimal Nalezna, decimal Zaplacona)
{
    /// <summary>Różnica: dodatnia to dopłata, ujemna to zwrot.</summary>
    public decimal Roznica => Kwoty.Zaokraglij(Nalezna - Zaplacona);

    /// <summary>Kwota do dopłaty; zero, gdy nadpłacono.</summary>
    public decimal DoDoplaty => Roznica > 0m ? Roznica : 0m;

    /// <summary>Kwota do zwrotu; zero, gdy niedopłacono.</summary>
    public decimal DoZwrotu => Roznica < 0m ? -Roznica : 0m;

    /// <summary>Termin złożenia wniosku o zwrot i zapłaty dopłaty.</summary>
    public static DateOnly Termin(int rok) =>
        DniRobocze.NajblizszyRoboczy(new DateOnly(rok + 1, 5, 20));
}
