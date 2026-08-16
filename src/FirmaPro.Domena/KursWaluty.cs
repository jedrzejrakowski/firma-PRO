namespace FirmaPro.Domena;

/// <summary>
/// Kurs waluty użyty do przeliczenia faktury na złote.
/// </summary>
/// <param name="Waluta">Trzyliterowy kod waluty faktury, np. „EUR”.</param>
/// <param name="Wartosc">Ile złotych za jedną jednostkę waluty.</param>
/// <param name="ZDnia">Dzień tabeli, z której pochodzi kurs.</param>
/// <param name="Tabela">Numer tabeli NBP - trafia na wydruk jako dowód.</param>
public sealed record KursWaluty(string Waluta, decimal Wartosc, DateOnly ZDnia, string? Tabela)
{
    /// <summary>Kurs złotego do samego siebie - jeden do jednego.</summary>
    public static KursWaluty Zlotowy(DateOnly dzien) => new("PLN", 1m, dzien, null);

    public bool Zlotowka => string.Equals(Waluta, "PLN", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Przeliczanie faktur wystawionych w walucie obcej.
/// </summary>
/// <remarks>
/// <para>
/// Faktura może być wystawiona w euro, ale podatek państwo pobiera
/// w złotych - i to kwota w złotych trafia do rejestru VAT, do deklaracji
/// i na fakturę obok kwoty w walucie (art. 106e ust. 11 ustawy).
/// </para>
/// <para>
/// Kurs nie jest dowolny. Bierze się średni kurs NBP z <b>ostatniego dnia
/// roboczego poprzedzającego</b> dzień powstania obowiązku podatkowego
/// (art. 31a ust. 1), a gdy faktura powstała wcześniej niż obowiązek -
/// z dnia poprzedzającego jej wystawienie (art. 31a ust. 2). Kurs z dnia
/// wystawienia albo z dnia zapłaty byłby zwyczajnie zły.
/// </para>
/// </remarks>
public static class Przeliczenie
{
    /// <summary>
    /// Dzień, z którego bierze się kurs.
    /// </summary>
    /// <remarks>
    /// Zwraca dzień <em>poprzedzający</em> zdarzenie. Który dokładnie dzień
    /// roboczy to będzie, rozstrzyga dopiero tabela NBP - świąt i weekendów
    /// nie da się wyliczyć z kalendarza, bo Nowy Rok wypada w różne dni
    /// tygodnia, a Wielkanoc rusza się co roku.
    /// </remarks>
    /// <param name="dataWystawienia">Data wystawienia faktury.</param>
    /// <param name="dataObowiazku">Data powstania obowiązku podatkowego.</param>
    public static DateOnly DzienKursu(DateOnly dataWystawienia, DateOnly dataObowiazku)
    {
        // Faktura wystawiona przed powstaniem obowiązku - liczy się dzień
        // przed jej wystawieniem. W pozostałych przypadkach dzień przed
        // powstaniem obowiązku. Sprowadza się to do wcześniejszej z dat.
        DateOnly zdarzenie = dataWystawienia < dataObowiazku ? dataWystawienia : dataObowiazku;

        return zdarzenie.AddDays(-1);
    }

    /// <summary>
    /// Przelicza kwotę na złote i zaokrągla do groszy.
    /// </summary>
    /// <remarks>
    /// Zaokrąglenie następuje na końcu, po pomnożeniu - przeliczanie kwot już
    /// zaokrąglonych dawałoby sumę, która nie zgadza się z sumą przeliczoną.
    /// </remarks>
    public static decimal NaZlote(decimal kwota, KursWaluty kurs)
    {
        ArgumentNullException.ThrowIfNull(kurs);

        return kurs.Zlotowka ? Kwoty.Zaokraglij(kwota) : Kwoty.Zaokraglij(kwota * kurs.Wartosc);
    }

    /// <summary>Przelicza kwotę na złote kursem podanym wprost.</summary>
    public static decimal NaZlote(decimal kwota, decimal kurs) =>
        Kwoty.Zaokraglij(kwota * kurs);
}
