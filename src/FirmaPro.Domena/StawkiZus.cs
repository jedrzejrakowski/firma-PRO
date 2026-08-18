namespace FirmaPro.Domena;

/// <summary>
/// Stopy procentowe składek - jedyne liczby w ZUS-ie, które nie zmieniają się co roku.
/// </summary>
/// <remarks>
/// Stopy są zapisane w ustawie o systemie ubezpieczeń społecznych (art. 22)
/// i stoją niezmienione od lat. Wszystko, co się zmienia - podstawy wymiaru,
/// minimalne wynagrodzenie, progi - siedzi w <see cref="StawkiZus"/>, bo to
/// tamte liczby trzeba co roku podmieniać.
/// </remarks>
public static class StopyZus
{
    /// <summary>Emerytalna - 19,52% podstawy.</summary>
    public const decimal Emerytalna = 0.1952m;

    /// <summary>Rentowa - 8% podstawy.</summary>
    public const decimal Rentowa = 0.0800m;

    /// <summary>Chorobowa - 2,45%, dla przedsiębiorcy dobrowolna.</summary>
    public const decimal Chorobowa = 0.0245m;

    /// <summary>
    /// Wypadkowa - 1,67% dla płatnika zgłaszającego do dziewięciu ubezpieczonych.
    /// </summary>
    /// <remarks>
    /// To jedyna stopa, która różni się między firmami: przy dziesięciu
    /// i więcej ubezpieczonych ustala ją ZUS według grupy działalności
    /// i wypadkowości. Dlatego jest ustawieniem firmy, a nie stałą.
    /// </remarks>
    public const decimal WypadkowaMalyPlatnik = 0.0167m;

    /// <summary>Fundusz Pracy i Fundusz Solidarnościowy razem - 2,45%.</summary>
    public const decimal FunduszPracy = 0.0245m;

    /// <summary>Zdrowotna - 9% podstawy.</summary>
    public const decimal Zdrowotna = 0.09m;

    /// <summary>Zdrowotna przy podatku liniowym - 4,9% dochodu.</summary>
    public const decimal ZdrowotnaLiniowy = 0.049m;

    /// <summary>Część zdrowotnej odliczana od przychodu przy ryczałcie - połowa.</summary>
    public const decimal OdliczenieZdrowotnejRyczalt = 0.50m;
}

/// <summary>
/// Kwoty i progi ZUS obowiązujące w danym roku.
/// </summary>
/// <remarks>
/// <para>
/// <b>Te liczby zmieniają się co roku</b> i ktoś musi je co roku sprawdzić.
/// Program trzyma je w jednym miejscu i pozwala poprawić bez zmiany kodu
/// (ekran „Stawki ZUS”), bo inaczej każdy styczeń wymagałby nowej wersji
/// programu.
/// </para>
/// <para>
/// Znacznik <see cref="Potwierdzone"/> mówi, czy kwoty pochodzą z ogłoszonego
/// aktu prawnego, czy są wpisane wstępnie. Rok niepotwierdzony liczy się
/// normalnie, ale program mówi o tym na ekranie - cicha pomyłka w podstawie
/// wymiaru wychodzi dopiero przy kontroli ZUS.
/// </para>
/// </remarks>
/// <param name="Rok">Rok kalendarzowy, którego dotyczą kwoty.</param>
/// <param name="MinimalneWynagrodzenie">Minimalne wynagrodzenie od 1 stycznia.</param>
/// <param name="MinimalneOdLipca">
/// Minimalne wynagrodzenie od 1 lipca, gdy w roku obowiązują dwie stawki;
/// puste, gdy przez cały rok obowiązuje jedna.
/// </param>
/// <param name="PrognozowanePrzecietne">
/// Prognozowane przeciętne wynagrodzenie miesięczne - podstawa pełnego ZUS
/// to 60% tej kwoty.
/// </param>
/// <param name="PrzecietneDoRyczaltu">
/// Przeciętne miesięczne wynagrodzenie w sektorze przedsiębiorstw w czwartym
/// kwartale roku poprzedniego, wraz z wypłatami z zysku - od niej liczy się
/// zdrowotną ryczałtowca.
/// </param>
/// <param name="UdzialMinimalnegoWZdrowotnej">
/// Jaka część minimalnego wynagrodzenia stanowi najniższą podstawę zdrowotnej.
/// Do 2024 roku było to całe minimalne wynagrodzenie, od 2025 - trzy czwarte.
/// </param>
/// <param name="LimitPrzychoduMalyZusPlus">
/// Górny limit przychodu poprzedniego roku, do którego przysługuje Mały ZUS Plus.
/// </param>
/// <param name="LimitOdliczeniaZdrowotnej">
/// Roczny limit odliczenia składki zdrowotnej przy podatku liniowym.
/// </param>
/// <param name="Potwierdzone">
/// Czy kwoty pochodzą z ogłoszonego aktu prawnego.
/// </param>
public sealed record StawkiZus(
    int Rok,
    decimal MinimalneWynagrodzenie,
    decimal? MinimalneOdLipca,
    decimal PrognozowanePrzecietne,
    decimal PrzecietneDoRyczaltu,
    decimal UdzialMinimalnegoWZdrowotnej,
    decimal LimitPrzychoduMalyZusPlus,
    decimal LimitOdliczeniaZdrowotnej,
    bool Potwierdzone = true)
{
    /// <summary>Minimalne wynagrodzenie obowiązujące w danym miesiącu.</summary>
    /// <remarks>
    /// W 2024 roku minimalne wynagrodzenie zmieniło się w połowie roku, więc
    /// podstawa preferencyjna była w tym roku dwukrotnie inna. Rachunek musi
    /// pytać o miesiąc, a nie brać kwoty ze stycznia.
    /// </remarks>
    public decimal MinimalneWMiesiacu(int miesiac) =>
        miesiac >= 7 && MinimalneOdLipca is decimal odLipca
            ? odLipca
            : MinimalneWynagrodzenie;

    /// <summary>Najniższa podstawa wymiaru składki zdrowotnej.</summary>
    public decimal NajnizszaPodstawaZdrowotnej =>
        Kwoty.Zaokraglij(MinimalneWynagrodzenie * UdzialMinimalnegoWZdrowotnej);

    /// <summary>Podstawa pełnego ZUS - 60% prognozowanego przeciętnego.</summary>
    public decimal PodstawaPelna => Kwoty.Zaokraglij(PrognozowanePrzecietne * 0.60m);

    /// <summary>Podstawa preferencyjna - 30% minimalnego wynagrodzenia.</summary>
    public decimal PodstawaPreferencyjna(int miesiac) =>
        Kwoty.Zaokraglij(MinimalneWMiesiacu(miesiac) * 0.30m);

    // ------------------------------------------------------- kwoty wpisane

    /// <summary>
    /// Kwoty wpisane w programie jako punkt wyjścia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Źródła: obwieszczenie Ministra Rodziny w sprawie kwoty ograniczenia
    /// podstawy wymiaru, rozporządzenie Rady Ministrów w sprawie minimalnego
    /// wynagrodzenia oraz komunikat Prezesa GUS o przeciętnym wynagrodzeniu
    /// w czwartym kwartale.
    /// </para>
    /// <para>
    /// <b>Rok bieżący i przyszły trzeba potwierdzić</b> przed pierwszym
    /// przelewem - kwoty ogłaszane są pod koniec roku poprzedniego i bywają
    /// inne niż zapowiadane w projekcie.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<StawkiZus> Wpisane { get; } =
    [
        new StawkiZus(2024,
            MinimalneWynagrodzenie: 4242m,
            MinimalneOdLipca: 4300m,
            PrognozowanePrzecietne: 7824m,
            PrzecietneDoRyczaltu: 7767.85m,
            UdzialMinimalnegoWZdrowotnej: 1.00m,
            LimitPrzychoduMalyZusPlus: 120_000m,
            LimitOdliczeniaZdrowotnej: 11_600m),

        new StawkiZus(2025,
            MinimalneWynagrodzenie: 4666m,
            MinimalneOdLipca: null,
            PrognozowanePrzecietne: 8673m,
            PrzecietneDoRyczaltu: 8549.18m,
            UdzialMinimalnegoWZdrowotnej: 0.75m,
            LimitPrzychoduMalyZusPlus: 120_000m,
            LimitOdliczeniaZdrowotnej: 12_900m),

        // Rok wpisany wstępnie - do potwierdzenia z ogłoszonymi aktami.
        new StawkiZus(2026,
            MinimalneWynagrodzenie: 4806m,
            MinimalneOdLipca: null,
            PrognozowanePrzecietne: 9420m,
            PrzecietneDoRyczaltu: 9000m,
            UdzialMinimalnegoWZdrowotnej: 0.75m,
            LimitPrzychoduMalyZusPlus: 120_000m,
            LimitOdliczeniaZdrowotnej: 12_900m,
            Potwierdzone: false)
    ];

    /// <summary>Kwoty wpisane w programie dla danego roku; puste, gdy roku nie zna.</summary>
    /// <remarks>
    /// Świadomie zwracamy puste zamiast sięgać po rok poprzedni. Policzenie
    /// składki zeszłorocznymi kwotami dałoby wynik wyglądający poprawnie
    /// i niedopłatę, o której nikt by się nie dowiedział.
    /// </remarks>
    public static StawkiZus? Dla(int rok) => Wpisane.FirstOrDefault(s => s.Rok == rok);
}
