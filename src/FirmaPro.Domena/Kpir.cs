namespace FirmaPro.Domena;

/// <summary>
/// Forma opodatkowania działalności.
/// </summary>
/// <remarks>
/// Rozstrzyga, jaką księgę firma prowadzi - a to zupełnie różne dokumenty.
/// Przy skali i podatku liniowym jest to podatkowa księga przychodów
/// i rozchodów, przy ryczałcie ewidencja przychodów (bez kosztów, bo ryczałt
/// liczy się od samego przychodu), a spółka będąca podatnikiem CIT prowadzi
/// księgi rachunkowe, których ten program nie obsługuje.
/// </remarks>
public enum FormaOpodatkowania
{
    /// <summary>Skala podatkowa - 12% i 32% ponad progiem.</summary>
    Skala = 0,

    /// <summary>Podatek liniowy 19%.</summary>
    Liniowy = 1,

    /// <summary>Ryczałt od przychodów ewidencjonowanych.</summary>
    Ryczalt = 2,

    /// <summary>Księgi rachunkowe - poza zakresem programu.</summary>
    KsiegiRachunkowe = 3
}

/// <summary>
/// Kolumna podatkowej księgi przychodów i rozchodów.
/// </summary>
/// <remarks>
/// <para>
/// Numery odpowiadają kolumnom wzoru z rozporządzenia Ministra Finansów
/// w sprawie prowadzenia podatkowej księgi przychodów i rozchodów. Nie wolno
/// ich zmieniać - trafiają na wydruk księgi i po nich księgowa sprawdza,
/// czy zapis siedzi tam, gdzie powinien.
/// </para>
/// <para>
/// Kolumny 9 i 14 są sumami i nie da się do nich zapisać wprost, dlatego nie
/// ma ich na tej liście.
/// </para>
/// </remarks>
public enum KolumnaKpir
{
    /// <summary>Kolumna 7 - wartość sprzedanych towarów i usług.</summary>
    SprzedazTowarowIUslug = 7,

    /// <summary>
    /// Kolumna 8 - pozostałe przychody.
    /// </summary>
    /// <remarks>
    /// Odsetki od środków na rachunku, sprzedaż wyposażenia, odszkodowania -
    /// przychody, które nie są sprzedażą towarów ani usług.
    /// </remarks>
    PozostalePrzychody = 8,

    /// <summary>Kolumna 10 - zakup towarów handlowych i materiałów w cenach zakupu.</summary>
    ZakupTowarow = 10,

    /// <summary>
    /// Kolumna 11 - koszty uboczne zakupu.
    /// </summary>
    /// <remarks>
    /// Transport, załadunek, ubezpieczenie towaru w drodze. Osobna kolumna,
    /// bo te koszty wchodzą do wyceny towaru przy spisie z natury.
    /// </remarks>
    KosztyUboczneZakupu = 11,

    /// <summary>Kolumna 12 - wynagrodzenia w gotówce i w naturze.</summary>
    Wynagrodzenia = 12,

    /// <summary>Kolumna 13 - pozostałe wydatki.</summary>
    PozostaleWydatki = 13,

    /// <summary>Kolumna 15 - koszty działalności badawczo-rozwojowej.</summary>
    BadaniaIRozwoj = 15
}

/// <summary>Podpowiedzi o kolumnach księgi.</summary>
public static class Kolumny
{
    /// <summary>Czy kolumna jest przychodem.</summary>
    public static bool Przychod(KolumnaKpir kolumna) =>
        kolumna is KolumnaKpir.SprzedazTowarowIUslug or KolumnaKpir.PozostalePrzychody;

    /// <summary>Nazwa kolumny w brzmieniu z rozporządzenia.</summary>
    public static string Nazwa(KolumnaKpir kolumna) => kolumna switch
    {
        KolumnaKpir.SprzedazTowarowIUslug => "Wartość sprzedanych towarów i usług",
        KolumnaKpir.PozostalePrzychody => "Pozostałe przychody",
        KolumnaKpir.ZakupTowarow => "Zakup towarów handlowych i materiałów",
        KolumnaKpir.KosztyUboczneZakupu => "Koszty uboczne zakupu",
        KolumnaKpir.Wynagrodzenia => "Wynagrodzenia w gotówce i w naturze",
        KolumnaKpir.PozostaleWydatki => "Pozostałe wydatki",
        KolumnaKpir.BadaniaIRozwoj => "Koszty działalności badawczo-rozwojowej",
        _ => kolumna.ToString()
    };

    /// <summary>Numer kolumny na wydruku księgi.</summary>
    public static int Numer(KolumnaKpir kolumna) => (int)kolumna;

    /// <summary>Kolumny kosztowe - do wyboru przy fakturze zakupu.</summary>
    public static IReadOnlyList<KolumnaKpir> Kosztowe { get; } =
    [
        KolumnaKpir.ZakupTowarow,
        KolumnaKpir.KosztyUboczneZakupu,
        KolumnaKpir.Wynagrodzenia,
        KolumnaKpir.PozostaleWydatki,
        KolumnaKpir.BadaniaIRozwoj
    ];
}

/// <summary>
/// Jeden zapis w podatkowej księdze przychodów i rozchodów.
/// </summary>
/// <param name="Data">Data zdarzenia gospodarczego (kolumna 2).</param>
/// <param name="NumerDowodu">Numer dowodu księgowego (kolumna 3).</param>
/// <param name="Kontrahent">Nazwa kontrahenta (kolumna 4).</param>
/// <param name="Adres">Adres kontrahenta (kolumna 5).</param>
/// <param name="Opis">Opis zdarzenia gospodarczego (kolumna 6).</param>
/// <param name="Kolumna">Kolumna, do której trafia kwota.</param>
/// <param name="Kwota">
/// Kwota zapisu. Ujemna przy korekcie zmniejszającej - księga nie zna
/// czerwonych zapisów, minus jest jedynym sposobem pokazania zmniejszenia.
/// </param>
/// <param name="Uwagi">Uwagi (kolumna 16).</param>
public sealed record WpisKsiegi(
    DateOnly Data,
    string NumerDowodu,
    string Kontrahent,
    string? Adres,
    string Opis,
    KolumnaKpir Kolumna,
    decimal Kwota,
    string? Uwagi = null)
{
    /// <summary>Czy zapis jest przychodem.</summary>
    public bool Przychod => Kolumny.Przychod(Kolumna);
}

/// <summary>Kwota przypadająca na jedną kolumnę księgi.</summary>
public sealed record SumaKolumny(KolumnaKpir Kolumna, decimal Kwota);

/// <summary>
/// Podatkowa księga przychodów i rozchodów za okres.
/// </summary>
/// <remarks>
/// <para>
/// Księga nie jest widokiem rejestru VAT i nie da się jej z niego wyprowadzić.
/// Do rejestru wchodzi każda faktura, bo liczy się z niej podatek należny;
/// do księgi wchodzi to, co jest przychodem albo kosztem w rozumieniu ustawy
/// o podatku dochodowym - a to inny zbiór. Środek trwały jest wydatkiem, ale
/// nie kosztem miesiąca zakupu; niektóre wydatki nie są kosztem nigdy.
/// </para>
/// <para>
/// Sumy narastające liczy się od początku roku, bo zaliczka na podatek
/// dochodowy jest liczona od dochodu narastająco (art. 44 ust. 3 ustawy
/// o PIT), a nie od dochodu samego miesiąca.
/// </para>
/// </remarks>
public sealed class Kpir
{
    private Kpir(OkresRozliczeniowy okres,
                 IReadOnlyList<WpisKsiegi> wpisy,
                 IReadOnlyList<WpisKsiegi> odPoczatkuRoku)
    {
        Okres = okres;
        Wpisy = wpisy;
        _odPoczatkuRoku = odPoczatkuRoku;
    }

    private readonly IReadOnlyList<WpisKsiegi> _odPoczatkuRoku;

    public OkresRozliczeniowy Okres { get; }

    /// <summary>Zapisy okresu, w kolejności numeracji księgi.</summary>
    public IReadOnlyList<WpisKsiegi> Wpisy { get; }

    // --- sumy okresu --------------------------------------------------------

    public decimal Kolumna(KolumnaKpir kolumna) => Suma(Wpisy, kolumna);

    /// <summary>Kolumna 9 - razem przychód.</summary>
    public decimal RazemPrzychod =>
        Kolumna(KolumnaKpir.SprzedazTowarowIUslug) + Kolumna(KolumnaKpir.PozostalePrzychody);

    /// <summary>
    /// Kolumna 14 - razem wydatki.
    /// </summary>
    /// <remarks>
    /// Suma kolumn 12 i 13, a nie wszystkich kosztowych. Zakup towarów
    /// i koszty uboczne stoją poza tą sumą, bo rozlicza się je przez spis
    /// z natury - dodanie ich tutaj zawyżyłoby koszty o wartość towaru,
    /// który jeszcze leży w magazynie.
    /// </remarks>
    public decimal RazemWydatki =>
        Kolumna(KolumnaKpir.Wynagrodzenia) + Kolumna(KolumnaKpir.PozostaleWydatki);

    /// <summary>Wszystkie kolumny okresu z niezerowymi kwotami.</summary>
    public IReadOnlyList<SumaKolumny> Sumy => Enum.GetValues<KolumnaKpir>()
        .Select(k => new SumaKolumny(k, Kolumna(k)))
        .Where(s => s.Kwota != 0)
        .ToList();

    // --- sumy narastające ---------------------------------------------------

    /// <summary>Suma kolumny od początku roku do końca tego okresu.</summary>
    public decimal Narastajaco(KolumnaKpir kolumna) => Suma(_odPoczatkuRoku, kolumna);

    public decimal PrzychodNarastajaco =>
        Narastajaco(KolumnaKpir.SprzedazTowarowIUslug)
        + Narastajaco(KolumnaKpir.PozostalePrzychody);

    public decimal WydatkiNarastajaco =>
        Narastajaco(KolumnaKpir.Wynagrodzenia) + Narastajaco(KolumnaKpir.PozostaleWydatki);

    /// <summary>
    /// Dochód narastająco liczony kolumnami 9 i 14 - tak, jak pokazuje go księga.
    /// </summary>
    /// <remarks>
    /// <b>To nie jest podstawa zaliczki na podatek.</b> Kolumna 14 nie obejmuje
    /// zakupu towarów ani kosztów ubocznych, bo te rozlicza się przez spis
    /// z natury - firma handlowa miałaby tu dochód zawyżony o wartość całego
    /// zakupionego towaru. Do podatku służy
    /// <see cref="RozliczenieRoczne.Zbuduj"/>, które te kolumny uwzględnia.
    /// </remarks>
    public decimal DochodNarastajaco => PrzychodNarastajaco - WydatkiNarastajaco;

    private static decimal Suma(IEnumerable<WpisKsiegi> wpisy, KolumnaKpir kolumna) =>
        Kwoty.Zaokraglij(wpisy.Where(w => w.Kolumna == kolumna).Sum(w => w.Kwota));

    /// <summary>
    /// Buduje księgę za okres.
    /// </summary>
    /// <param name="okres">Okres, którego dotyczy księga.</param>
    /// <param name="wszystkie">
    /// Wszystkie zapisy roku - także z okresów poprzednich, bo z nich powstają
    /// sumy narastające.
    /// </param>
    public static Kpir Zbuduj(OkresRozliczeniowy okres, IEnumerable<WpisKsiegi> wszystkie)
    {
        ArgumentNullException.ThrowIfNull(okres);
        ArgumentNullException.ThrowIfNull(wszystkie);

        List<WpisKsiegi> lista = [.. wszystkie];

        List<WpisKsiegi> wOkresie = lista
            .Where(w => okres.Zawiera(w.Data))
            .OrderBy(w => w.Data)
            .ThenBy(w => w.NumerDowodu, StringComparer.Ordinal)
            .ToList();

        // Narastająco liczymy od pierwszego dnia roku okresu do jego końca.
        var poczatekRoku = new DateOnly(okres.PierwszyDzien.Year, 1, 1);

        List<WpisKsiegi> narastajaco = lista
            .Where(w => w.Data >= poczatekRoku && w.Data <= okres.OstatniDzien)
            .ToList();

        return new Kpir(okres, wOkresie, narastajaco);
    }
}
