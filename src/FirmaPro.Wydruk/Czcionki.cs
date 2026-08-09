using System.Reflection;
using PdfSharp.Fonts;

namespace FirmaPro.Wydruk;

/// <summary>
/// Dostarcza czcionki dołączone do programu.
/// </summary>
/// <remarks>
/// <para>
/// PDFsharp domyślnie szuka czcionek w systemie. Na serwerze z Linuksem
/// zwykle nie ma ich wcale, a nawet gdy są, ta sama faktura wyglądałaby
/// inaczej u każdego odbiorcy. Czcionka jedzie więc razem z programem jako
/// zasób osadzony w bibliotece.
/// </para>
/// <para>
/// Liberation Sans wybrano z dwóch powodów: ma komplet polskich znaków
/// i jest na licencji SIL OFL, która wprost dopuszcza dołączanie czcionki
/// do sprzedawanego oprogramowania.
/// </para>
/// </remarks>
internal sealed class DostawcaCzcionek : IFontResolver
{
    /// <summary>Nazwa rodziny używana w całym wydruku.</summary>
    public const string Rodzina = "Liberation Sans";

    private const string Zwykla = "LiberationSans-Regular";
    private const string Pogrubiona = "LiberationSans-Bold";

    private static readonly Lock Zamek = new();
    private static bool _zarejestrowano;

    /// <summary>
    /// Rejestruje dostawcę czcionek w PDFsharp.
    /// </summary>
    /// <remarks>
    /// PDFsharp trzyma dostawcę w ustawieniu globalnym i nie pozwala go
    /// podmienić po utworzeniu pierwszego dokumentu. Rejestracja musi więc
    /// wykonać się dokładnie raz, także wtedy, gdy dwa żądania wystartują
    /// równocześnie - stąd zamek.
    /// </remarks>
    public static void Zarejestruj()
    {
        if (_zarejestrowano)
        {
            return;
        }

        lock (Zamek)
        {
            if (_zarejestrowano)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new DostawcaCzcionek();
            _zarejestrowano = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        // Kursywy nie dołączamy - na fakturze nie ma jej gdzie użyć, a każdy
        // krój to kolejne pół megabajta w wydaniu programu. Prośbę o kursywę
        // obsługujemy krojem zwykłym, zamiast wywracać wydruk.
        new(isBold ? Pogrubiona : Zwykla);

    public byte[]? GetFont(string faceName)
    {
        string nazwaZasobu = $"FirmaPro.Wydruk.Czcionki.{faceName}.ttf";

        using Stream? zasob = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(nazwaZasobu);

        if (zasob is null)
        {
            throw new InvalidOperationException(
                $"Brak czcionki {faceName} w zasobach programu. " +
                "Sprawdź, czy plik TTF jest dołączony jako EmbeddedResource.");
        }

        using var pamiec = new MemoryStream();
        zasob.CopyTo(pamiec);

        return pamiec.ToArray();
    }
}
