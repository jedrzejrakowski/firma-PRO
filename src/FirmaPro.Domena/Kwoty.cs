using System.Globalization;

namespace FirmaPro.Domena;

/// <summary>
/// Zaokrąglanie kwot pieniężnych.
/// </summary>
/// <remarks>
/// Wszystkie kwoty w systemie są typu <see cref="decimal"/>. Typy
/// zmiennoprzecinkowe (<c>double</c>, <c>float</c>) nie nadają się do
/// pieniędzy, bo nie potrafią dokładnie zapisać nawet 0,10 zł - a KSeF
/// odrzuca faktury, na których sumy się nie zgadzają co do grosza.
/// </remarks>
public static class Kwoty
{
    /// <summary>Zero z dokładnością do groszy.</summary>
    public static readonly decimal Zero = 0.00m;

    /// <summary>
    /// Zaokrągla kwotę do groszy metodą "w górę od połowy".
    /// </summary>
    /// <remarks>
    /// Tak zaokrągla się podatek w polskim systemie podatkowym. Uwaga:
    /// domyślne <c>Math.Round</c> w .NET stosuje zaokrąglanie bankierskie
    /// (do najbliższej parzystej), które dałoby tu inne wyniki - dlatego
    /// zawsze przechodzimy przez tę metodę, a nie przez <c>Math.Round</c>
    /// wprost.
    /// </remarks>
    public static decimal Zaokraglij(decimal kwota) =>
        Math.Round(kwota, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Zaokrągla kwotę do pełnych złotych.
    /// </summary>
    /// <remarks>
    /// Tak podaje się kwoty w części deklaracyjnej JPK_V7: końcówki poniżej
    /// 50 groszy pomija się, a od 50 groszy podwyższa do pełnych złotych
    /// (art. 63 § 1 Ordynacji podatkowej). Część ewidencyjna tego samego
    /// pliku zostaje w groszach - to nie pomyłka, tylko dwie różne reguły
    /// w jednym dokumencie.
    /// </remarks>
    public static long ZaokraglijDoZlotych(decimal kwota) =>
        (long)Math.Round(kwota, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Formatuje kwotę tak, jak wymaga tego schemat FA(3): zawsze dwa
    /// miejsca po przecinku, kropka jako separator, bez separatora tysięcy.
    /// </summary>
    public static string NaXml(decimal kwota) =>
        Zaokraglij(kwota).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Kwota zapisana po polsku: przecinek dziesiętny, spacja co trzy cyfry.
    /// </summary>
    /// <remarks>
    /// Do tekstów czytanych przez ludzi - wiadomości do kontrahentów i wydruk.
    /// Wewnątrz programu oraz w plikach dla urzędów liczby chodzą w zapisie
    /// niezależnym od języka; te dwa światy nie mogą się pomieszać.
    /// </remarks>
    public static string NaTekst(decimal kwota) => kwota.ToString("N2", ZapisPolski);

    private static readonly NumberFormatInfo ZapisPolski = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        NumberGroupSizes = [3]
    };

    /// <summary>
    /// Formatuje liczbę (ilość, cenę) bez zbędnych zer i bez notacji
    /// wykładniczej, której wzorce schematu nie przyjmują.
    /// </summary>
    public static string LiczbaNaXml(decimal wartosc, int maksMiejsc)
    {
        decimal zaokraglona = Math.Round(wartosc, maksMiejsc, MidpointRounding.AwayFromZero);
        string tekst = zaokraglona.ToString("0.############################",
            System.Globalization.CultureInfo.InvariantCulture);
        return tekst.Length == 0 ? "0" : tekst;
    }
}
