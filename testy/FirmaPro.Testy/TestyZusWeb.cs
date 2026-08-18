using System.Globalization;
using System.Net;

namespace FirmaPro.Testy;

/// <summary>
/// ZUS przechodzony tak, jak przechodzi go użytkownik.
/// </summary>
/// <remarks>
/// Najważniejszy odcinek tej drogi to ostatni: potwierdzenie zapłaty ma
/// przenieść składkę do księgi. Dopóki tego nie robi, kwota z ekranu ZUS
/// i dochód z księgi żyją osobno, a podatek wychodzi za wysoki.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyZusWeb(AplikacjaTestowa aplikacja)
{
    private const int Rok = 2025;

    [Fact]
    public async Task EkranPokazujeDwanascieMiesiecyITerminy()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, $"/Zus?rok={Rok}");

        Assert.Contains("Składki ZUS", html, StringComparison.Ordinal);
        Assert.Contains("styczeń", html, StringComparison.Ordinal);
        Assert.Contains("grudzień", html, StringComparison.Ordinal);

        // Termin za styczeń to 20 lutego - dzień roboczy, więc bez przesunięcia.
        Assert.Contains("2025-02-20", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rok bez wpisanych kwot nie liczy się kwotami z innego roku.
    /// </summary>
    /// <remarks>
    /// Wynik wyglądałby poprawnie, a niedopłata wyszłaby dopiero przy kontroli.
    /// </remarks>
    [Fact]
    public async Task NieznanyRokMowiWprostZeGoNieZna()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, "/Zus?rok=2019");

        Assert.Contains("nie zna kwot ZUS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Roczne rozliczenie zdrowotnej", html, StringComparison.Ordinal);
    }

    /// <summary>Zmiana tytułu ubezpieczenia zmienia kwotę składki.</summary>
    [Fact]
    public async Task TytulUbezpieczeniaZmieniaSkladke()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        await ZapiszUstawieniaAsync(klient, "Pelny");
        string pelny = await StronaAsync(klient, $"/Zus?rok={Rok}");

        // Podstawa pełna 2025: 5203,80 zł, a z niej składki społeczne 1646,47.
        Assert.Contains("5 203,80", pelny, StringComparison.Ordinal);
        Assert.Contains("1 646,47", pelny, StringComparison.Ordinal);

        await ZapiszUstawieniaAsync(klient, "Preferencyjny");
        string preferencyjny = await StronaAsync(klient, $"/Zus?rok={Rok}");

        // Podstawa preferencyjna 2025: 1399,80 zł, składki 442,90.
        Assert.Contains("1 399,80", preferencyjny, StringComparison.Ordinal);
        Assert.Contains("442,90", preferencyjny, StringComparison.Ordinal);

        // Sprawdzamy sumę składek, a nie samą podstawę: podstawa pełna stoi
        // w podpowiedzi pod formularzem niezależnie od wybranego tytułu.
        Assert.DoesNotContain("1 646,47", preferencyjny, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mały ZUS Plus bez dochodu za rok poprzedni jest zatrzymywany.
    /// </summary>
    /// <remarks>
    /// Bez tej kwoty podstawa byłaby wzięta z powietrza, a składka wyglądałaby
    /// na policzoną.
    /// </remarks>
    [Fact]
    public async Task MalyZusPlusBezDochoduJestZatrzymywany()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Ustawienia&rok={Rok}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tytul"] = "MalyZusPlus",
                ["StopaWypadkowa"] = "0.0167",
                ["DniProwadzeniaPoprzedniegoRoku"] = "365"
            });

        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);
        Assert.Contains("wymaga podania dochodu", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Potwierdzenie zapłaty przenosi Fundusz Pracy do księgi.
    /// </summary>
    /// <remarks>
    /// To jest ten odcinek, na którym cały moduł ma sens. Fundusz Pracy jest
    /// kosztem zawsze, więc musi trafić do kolumny 13 nawet wtedy, gdy składki
    /// społeczne podatnik odlicza od dochodu.
    /// </remarks>
    [Fact]
    public async Task ZaplaconaSkladkaWchodziDoKsiegi()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        await ZapiszUstawieniaAsync(klient, "Pelny");

        using (HttpResponseMessage zaplata = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Zaplac&rok={Rok}&miesiac=3",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dzien"] = "2025-04-18"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zaplata.StatusCode);
        }

        string ksiega = await StronaAsync(klient, "/Ksiega?okres=2025-04");

        Assert.Contains("ZUS/03/2025", ksiega, StringComparison.Ordinal);
        Assert.Contains("Fundusz Pracy", ksiega, StringComparison.Ordinal);

        // Sam Fundusz Pracy - 127,49 zł. Społeczne poszły w odliczenie,
        // więc w księdze ich nie ma.
        Assert.Contains("127,49", ksiega, StringComparison.Ordinal);
        Assert.DoesNotContain("Składki społeczne i Fundusz Pracy", ksiega,
                              StringComparison.Ordinal);
    }

    /// <summary>Wybór „w kosztach” wpisuje do księgi także społeczne.</summary>
    [Fact]
    public async Task SkladkiWKosztachWchodzaWCalosci()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        await ZapiszUstawieniaAsync(klient, "Pelny", wKosztach: true);

        using (HttpResponseMessage zaplata = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Zaplac&rok={Rok}&miesiac=6",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dzien"] = "2025-07-18"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zaplata.StatusCode);
        }

        string ksiega = await StronaAsync(klient, "/Ksiega?okres=2025-07");

        Assert.Contains("Składki społeczne i Fundusz Pracy", ksiega,
                        StringComparison.Ordinal);

        // 1015,78 + 416,30 + 127,49 + 86,90 + 127,49 = 1773,96 zł.
        Assert.Contains("1 773,96", ksiega, StringComparison.Ordinal);
    }

    /// <summary>Cofnięcie zapłaty zabiera zapis także z księgi.</summary>
    [Fact]
    public async Task CofniecieZaplatyZabieraZapisZKsiegi()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        await ZapiszUstawieniaAsync(klient, "Pelny");

        await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Zaplac&rok={Rok}&miesiac=9",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dzien"] = "2025-10-17"
            });

        Assert.Contains("ZUS/09/2025",
            await StronaAsync(klient, "/Ksiega?okres=2025-10"), StringComparison.Ordinal);

        await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Cofnij&rok={Rok}&miesiac=9",
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.DoesNotContain("ZUS/09/2025",
            await StronaAsync(klient, "/Ksiega?okres=2025-10"), StringComparison.Ordinal);
    }

    /// <summary>Ekran mówi, że przy skali zdrowotnej nie odlicza się wcale.</summary>
    /// <remarks>
    /// Pytanie „gdzie tu odliczyć zdrowotną” pada najczęściej, a odpowiedź
    /// brzmi „nigdzie” - lepiej powiedzieć to wprost niż zostawić puste pole.
    /// </remarks>
    [Fact]
    public async Task PrzySkaliEkranMowiZeZdrowotnaNieJestOdliczana()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, $"/Zus?rok={Rok}");

        Assert.Contains("nie odlicza się", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ pomocnicze

    private static async Task ZapiszUstawieniaAsync(HttpClient klient, string tytul,
                                                    bool wKosztach = false)
    {
        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Ustawienia&rok={Rok}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tytul"] = tytul,
                ["Chorobowe"] = "true",
                ["SpoleczneWKosztach"] = wKosztach ? "true" : "false",
                ["StopaWypadkowa"] =
                    0.0167m.ToString(CultureInfo.InvariantCulture),
                ["DniProwadzeniaPoprzedniegoRoku"] = "365",
                ["DochodPoprzedniegoRoku"] = "60000"
            });

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
    }

    private static async Task<string> StronaAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();

        return await AplikacjaTestowa.TrescAsync(odpowiedz);
    }
}
