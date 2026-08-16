using System.Globalization;
using PdfSharp.Drawing;

namespace FirmaPro.Wydruk;

/// <summary>
/// Wymiary, kroje i kolory wydruku - zebrane w jednym miejscu.
/// </summary>
/// <remarks>
/// Wszystkie odległości podawane są w milimetrach, bo tak myśli się
/// o kartce papieru. PDFsharp liczy w punktach, więc każda wartość
/// przechodzi przez <see cref="Mm"/>.
/// </remarks>
internal static class Styl
{
    /// <summary>Zamienia milimetry na punkty typograficzne.</summary>
    public static double Mm(double milimetry) => milimetry * 72.0 / 25.4;

    // --- geometria strony ---------------------------------------------------

    public static double SzerokoscStrony => Mm(210);
    public static double WysokoscStrony => Mm(297);

    public static double MarginesLewy => Mm(15);
    public static double MarginesPrawy => Mm(15);
    public static double MarginesGorny => Mm(14);
    public static double MarginesDolny => Mm(16);

    public static double Lewa => MarginesLewy;
    public static double Prawa => SzerokoscStrony - MarginesPrawy;
    public static double SzerokoscTresci => Prawa - Lewa;

    /// <summary>Linia, poniżej której nie rysujemy już treści.</summary>
    public static double DolnaGranica => WysokoscStrony - MarginesDolny;

    // --- kroje --------------------------------------------------------------

    public static XFont Tytul { get; } = Krój(15, XFontStyleEx.Bold);
    public static XFont NaglowekSekcji { get; } = Krój(8, XFontStyleEx.Bold);
    public static XFont Zwykla { get; } = Krój(8);
    public static XFont Wyrozniona { get; } = Krój(8, XFontStyleEx.Bold);
    public static XFont Mala { get; } = Krój(6.8);
    public static XFont MalaWyrozniona { get; } = Krój(6.8, XFontStyleEx.Bold);
    public static XFont Tabela { get; } = Krój(7.3);
    public static XFont TabelaNaglowek { get; } = Krój(7.3, XFontStyleEx.Bold);
    public static XFont DoZaplaty { get; } = Krój(13, XFontStyleEx.Bold);

    private static XFont Krój(double rozmiar, XFontStyleEx styl = XFontStyleEx.Regular)
    {
        DostawcaCzcionek.Zarejestruj();
        return new XFont(DostawcaCzcionek.Rodzina, rozmiar, styl);
    }

    // --- kolory -------------------------------------------------------------

    public static XBrush Tekst { get; } = new XSolidBrush(XColor.FromArgb(22, 32, 43));
    public static XBrush TekstSzary { get; } = new XSolidBrush(XColor.FromArgb(92, 107, 122));
    public static XBrush Granat { get; } = new XSolidBrush(XColor.FromArgb(31, 78, 121));
    public static XBrush Czerwien { get; } = new XSolidBrush(XColor.FromArgb(176, 0, 32));
    public static XBrush TloNaglowka { get; } = new XSolidBrush(XColor.FromArgb(31, 78, 121));
    public static XBrush TloJasne { get; } = new XSolidBrush(XColor.FromArgb(240, 243, 247));
    public static XBrush TloOstrzezenia { get; } = new XSolidBrush(XColor.FromArgb(253, 236, 239));
    public static XBrush Biel { get; } = XBrushes.White;

    /// <summary>Czysta czerń - wyłącznie do kodu QR, dla kontrastu przy odczycie.</summary>
    public static XBrush Czern { get; } = XBrushes.Black;

    public static XPen Linia { get; } = new(XColor.FromArgb(180, 190, 200), 0.5);
    public static XPen LiniaJasna { get; } = new(XColor.FromArgb(213, 222, 231), 0.4);

    // --- liczby i daty ------------------------------------------------------

    /// <summary>
    /// Format liczb: spacja nierozdzielająca jako separator tysięcy, przecinek
    /// jako separator dziesiętny.
    /// </summary>
    /// <remarks>
    /// Format budujemy sami, zamiast sięgać po kulturę „pl-PL". Program ma
    /// drukować tak samo niezależnie od tego, jakie kultury są zainstalowane
    /// na serwerze - a bywa, że nie ma żadnej.
    /// </remarks>
    private static readonly NumberFormatInfo FormatLiczb = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        NumberGroupSizes = [3]
    };

    /// <summary>Kwota z dwoma miejscami po przecinku.</summary>
    public static string Kwota(decimal wartosc) =>
        wartosc.ToString("N2", FormatLiczb);

    /// <summary>
    /// Ilość bez zbędnych zer na końcu.
    /// </summary>
    /// <remarks>
    /// „2" czyta się lepiej niż „2,000000", ale ułamkowe ilości (0,25 godziny)
    /// muszą zostać widoczne. Format z krzyżykami drukuje miejsca po przecinku
    /// tylko wtedy, gdy niosą jakąś wartość.
    /// </remarks>
    public static string Ilosc(decimal wartosc) =>
        wartosc.ToString("0.######", FormatLiczb);

    /// <summary>
    /// Kurs waluty - tyle miejsc po przecinku, ile podał bank.
    /// </summary>
    /// <remarks>
    /// NBP notuje zwykle cztery miejsca, ale przy walutach o niskim nominale
    /// bywa ich więcej. Obcięcie do dwóch zmieniłoby kurs, a razem z nim
    /// kwotę podatku, którą wydruk ma udokumentować.
    /// </remarks>
    public static string Kurs(decimal wartosc) =>
        wartosc.ToString("0.0###########", FormatLiczb);

    /// <summary>Data w zapisie rok-miesiąc-dzień.</summary>
    public static string Data(DateOnly data) =>
        data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Numer rachunku rozbity na grupy, żeby dało się go przepisać.</summary>
    public static string Rachunek(string numer)
    {
        string cyfry = new((numer ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (cyfry.Length != 26)
        {
            return cyfry;
        }

        // Zapis przyjęty w Polsce: dwie cyfry kontrolne, potem grupy po cztery.
        var czesci = new List<string> { cyfry[..2] };
        for (int i = 2; i < cyfry.Length; i += 4)
        {
            czesci.Add(cyfry.Substring(i, Math.Min(4, cyfry.Length - i)));
        }

        return string.Join(' ', czesci);
    }
}
