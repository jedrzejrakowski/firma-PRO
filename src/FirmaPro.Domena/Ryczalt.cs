using System.Globalization;

namespace FirmaPro.Domena;

/// <summary>
/// Stawka ryczałtu od przychodów ewidencjonowanych.
/// </summary>
/// <remarks>
/// <para>
/// Stawki z art. 12 ustawy o zryczałtowanym podatku dochodowym. Zapisujemy je
/// jako liczbę procent, a nie jako pozycję listy - stawki bywają zmieniane
/// ustawą i przypisanie ich do numerów kończyłoby się przeliczaniem starych
/// ewidencji nową stawką.
/// </para>
/// <para>
/// Jedna działalność może mieć kilka stawek naraz - programista na 12% bywa
/// jednocześnie wykładowcą na 17%. Dlatego stawka siedzi przy fakturze,
/// a nie tylko w ustawieniach firmy.
/// </para>
/// </remarks>
public static class StawkiRyczaltu
{
    /// <summary>Stawki przewidziane ustawą, w kolejności rosnącej.</summary>
    public static IReadOnlyList<decimal> Wszystkie { get; } =
    [
        2m, 3m, 5.5m, 8.5m, 10m, 12m, 12.5m, 14m, 15m, 17m
    ];

    /// <summary>Czy taka stawka istnieje w ustawie.</summary>
    public static bool Znana(decimal stawka) => Wszystkie.Contains(stawka);

    /// <summary>Stawka zapisana po polsku, na przykład „8,5%”.</summary>
    public static string NaTekst(decimal stawka) =>
        stawka.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + "%";

    /// <summary>Podatek od przychodu przy danej stawce, zaokrąglony do złotych.</summary>
    /// <remarks>
    /// Podatek zaokrągla się do pełnych złotych (art. 63 § 1 Ordynacji
    /// podatkowej), a nie do groszy - tak samo jak przy zaliczkach.
    /// </remarks>
    public static long Podatek(decimal przychod, decimal stawka) =>
        Kwoty.ZaokraglijDoZlotych(przychod * stawka / 100m);
}

/// <summary>Jeden zapis w ewidencji przychodów.</summary>
/// <param name="Data">Data uzyskania przychodu.</param>
/// <param name="NumerDowodu">Numer faktury albo innego dowodu.</param>
/// <param name="Opis">Czego dotyczy przychód.</param>
/// <param name="Stawka">Stawka ryczałtu w procentach.</param>
/// <param name="Kwota">Przychód; ujemny przy korekcie zmniejszającej.</param>
public sealed record WpisRyczaltu(
    DateOnly Data,
    string NumerDowodu,
    string Opis,
    decimal Stawka,
    decimal Kwota);

/// <summary>Przychód i podatek w jednej stawce.</summary>
public sealed record PrzychodWStawce(decimal Stawka, decimal Przychod)
{
    /// <summary>Podatek przed odliczeniem składek.</summary>
    public long Podatek => StawkiRyczaltu.Podatek(Przychod, Stawka);
}

/// <summary>
/// Ewidencja przychodów za okres.
/// </summary>
/// <remarks>
/// <para>
/// Prostsza od księgi przychodów i rozchodów, bo nie ma w niej kosztów -
/// ryczałt liczy się od samego przychodu. Ale ma coś, czego księga nie ma:
/// podział na stawki. Jedna działalność bywa opodatkowana kilkoma stawkami
/// naraz i każda z nich daje osobny podatek.
/// </para>
/// <para>
/// Suma podatku nie jest podatkiem od sumy przychodów. Liczy się go osobno
/// w każdej stawce i dopiero sumuje - policzenie inaczej dałoby kwotę
/// oderwaną od rzeczywistości przy działalności mieszanej.
/// </para>
/// </remarks>
public sealed class EwidencjaRyczaltu
{
    private EwidencjaRyczaltu(OkresRozliczeniowy okres,
                              IReadOnlyList<WpisRyczaltu> wpisy,
                              IReadOnlyList<WpisRyczaltu> odPoczatkuRoku)
    {
        Okres = okres;
        Wpisy = wpisy;
        _odPoczatkuRoku = odPoczatkuRoku;
    }

    private readonly IReadOnlyList<WpisRyczaltu> _odPoczatkuRoku;

    public OkresRozliczeniowy Okres { get; }

    public IReadOnlyList<WpisRyczaltu> Wpisy { get; }

    /// <summary>Przychód okresu w podziale na stawki.</summary>
    public IReadOnlyList<PrzychodWStawce> WedlugStawek => Grupuj(Wpisy);

    /// <summary>Przychód okresu razem.</summary>
    public decimal RazemPrzychod => Kwoty.Zaokraglij(Wpisy.Sum(w => w.Kwota));

    /// <summary>
    /// Podatek okresu - suma podatków z poszczególnych stawek.
    /// </summary>
    /// <remarks>
    /// Zaliczkę liczy się narastająco (<see cref="PodatekNarastajaco"/>);
    /// ta kwota mówi, ile przypada na sam okres.
    /// </remarks>
    public long RazemPodatek => WedlugStawek.Sum(s => s.Podatek);

    /// <summary>Przychód od początku roku w podziale na stawki.</summary>
    public IReadOnlyList<PrzychodWStawce> NarastajacoWedlugStawek => Grupuj(_odPoczatkuRoku);

    public decimal PrzychodNarastajaco =>
        Kwoty.Zaokraglij(_odPoczatkuRoku.Sum(w => w.Kwota));

    /// <summary>
    /// Podatek narastająco - podstawa zaliczki za okres.
    /// </summary>
    /// <remarks>
    /// Zaliczkę za miesiąc ustala się jako podatek od przychodu narastająco
    /// pomniejszony o zaliczki już zapłacone. Bez składek na ubezpieczenie
    /// społeczne i zdrowotne, których program jeszcze nie prowadzi - to kwota
    /// do sprawdzenia, nie gotowa zaliczka.
    /// </remarks>
    public long PodatekNarastajaco => NarastajacoWedlugStawek.Sum(s => s.Podatek);

    private static List<PrzychodWStawce> Grupuj(IEnumerable<WpisRyczaltu> wpisy) =>
        wpisy
            .GroupBy(w => w.Stawka)
            .Select(g => new PrzychodWStawce(g.Key, Kwoty.Zaokraglij(g.Sum(w => w.Kwota))))
            .Where(s => s.Przychod != 0)
            .OrderBy(s => s.Stawka)
            .ToList();

    /// <summary>Buduje ewidencję za okres z zapisów całego roku.</summary>
    public static EwidencjaRyczaltu Zbuduj(OkresRozliczeniowy okres,
                                           IEnumerable<WpisRyczaltu> wszystkie)
    {
        ArgumentNullException.ThrowIfNull(okres);
        ArgumentNullException.ThrowIfNull(wszystkie);

        List<WpisRyczaltu> lista = [.. wszystkie];

        List<WpisRyczaltu> wOkresie = lista
            .Where(w => okres.Zawiera(w.Data))
            .OrderBy(w => w.Data)
            .ThenBy(w => w.NumerDowodu, StringComparer.Ordinal)
            .ToList();

        var poczatekRoku = new DateOnly(okres.PierwszyDzien.Year, 1, 1);

        List<WpisRyczaltu> narastajaco = lista
            .Where(w => w.Data >= poczatekRoku && w.Data <= okres.OstatniDzien)
            .ToList();

        return new EwidencjaRyczaltu(okres, wOkresie, narastajaco);
    }
}
