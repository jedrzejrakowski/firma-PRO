using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FirmaPro.Testy;

/// <summary>
/// Spis z natury przechodzony tak, jak przechodzi go użytkownik.
/// </summary>
/// <remarks>
/// Droga jest krótka, ale każdy jej odcinek waży na dochodzie rocznym:
/// założenie arkusza, dopisanie pozycji, rozpoznanie spisu jako remanentu
/// końcowego i wreszcie wejście jego wartości do rachunku. Zerwanie
/// któregokolwiek kończy się tym samym - zaniżonym dochodem.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestySpisuWeb(AplikacjaTestowa aplikacja)
{
    private static readonly int Rok = DateTime.UtcNow.Year;

    [Fact]
    public async Task PustaStronaZapowiadaBrakSpisu()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, $"/Ksiega/Spis?rok={Rok - 3}");

        Assert.Contains("Spis z natury", html, StringComparison.Ordinal);
        Assert.Contains("Rozliczenie roczne", html, StringComparison.Ordinal);

        // Brak spisu na koniec roku zaniża dochód - to musi być powiedziane
        // wprost, bo rachunek i tak się policzy i będzie wyglądał poprawnie.
        Assert.Contains("Brak spisu na koniec roku", html, StringComparison.Ordinal);
    }

    /// <summary>Pozycja dopisana do arkusza wchodzi do rozliczenia rocznego.</summary>
    [Fact]
    public async Task PozycjaWchodziDoRozliczenia()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        int rok = Rok - 4;
        string spis = await ZalozAsync(klient, rok, new DateOnly(rok, 12, 31));

        using (HttpResponseMessage dodanie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Ksiega/Spis?handler=Dodaj&rok={rok}&spis={spis}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Nazwa"] = "Deska sosnowa 25 mm",
                ["Jednostka"] = "m3",
                ["Ilosc"] = "3",
                ["CenaJednostkowa"] = "185",
                ["Wycena"] = "CenaZakupu"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, dodanie.StatusCode);
        }

        string html = await StronaAsync(klient, $"/Ksiega/Spis?rok={rok}&spis={spis}");

        Assert.Contains("Deska sosnowa 25 mm", html, StringComparison.Ordinal);

        // 3 × 185 = 555 zł - i ta sama kwota ma stać w remanencie końcowym.
        Assert.Contains("555,00", html, StringComparison.Ordinal);
        Assert.Contains("remanent końcowy", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Brak spisu na koniec roku", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Zamknięty spis nie przyjmuje już pozycji.
    /// </summary>
    /// <remarks>
    /// Spis podpisuje się i przechowuje razem z księgą - dopisanie do niego
    /// pozycji po fakcie znaczyłoby, że dokument w segregatorze i ten
    /// w programie to dwie różne rzeczy.
    /// </remarks>
    [Fact]
    public async Task ZamknietySpisNiePrzyjmujePozycji()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        int rok = Rok - 5;
        string spis = await ZalozAsync(klient, rok, new DateOnly(rok, 12, 31));

        using (HttpResponseMessage zamkniecie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Ksiega/Spis?handler=Zamknij&rok={rok}&spis={spis}",
            new Dictionary<string, string>(StringComparer.Ordinal)))
        {
            Assert.Equal(HttpStatusCode.Redirect, zamkniecie.StatusCode);
        }

        using HttpResponseMessage dodanie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Ksiega/Spis?handler=Dodaj&rok={rok}&spis={spis}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Nazwa"] = "Dopisek po zamknięciu",
                ["Jednostka"] = "szt.",
                ["Ilosc"] = "1",
                ["CenaJednostkowa"] = "100",
                ["Wycena"] = "CenaZakupu"
            });

        Assert.Equal(HttpStatusCode.OK, dodanie.StatusCode);

        string html = await AplikacjaTestowa.TrescAsync(dodanie);

        Assert.Contains("zamknięty", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dopisek po zamknięciu", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dwa spisy na ten sam dzień to jeden spis za dużo.
    /// </summary>
    /// <remarks>
    /// Przy dwóch arkuszach z tą samą datą nie wiadomo, który jest remanentem
    /// - a od tego zależy dochód roczny.
    /// </remarks>
    [Fact]
    public async Task DrugiSpisNaTenSamDzienJestOdrzucany()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        int rok = Rok - 6;
        var dzien = new DateOnly(rok, 12, 31);

        await ZalozAsync(klient, rok, dzien);

        using HttpResponseMessage powtorka = await WyslijZalozenieAsync(klient, rok, dzien);

        Assert.Equal(HttpStatusCode.OK, powtorka.StatusCode);

        string html = await AplikacjaTestowa.TrescAsync(powtorka);
        Assert.Contains("już istnieje", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ pomocnicze

    private static Task<HttpResponseMessage> WyslijZalozenieAsync(
        HttpClient klient, int rok, DateOnly dzien) =>
        AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Ksiega/Spis?handler=Zaloz&rok={rok}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DataSpisu"] = dzien.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["UwagiSpisu"] = "spis roczny"
            });

    private static async Task<string> ZalozAsync(HttpClient klient, int rok, DateOnly dzien)
    {
        using HttpResponseMessage odpowiedz = await WyslijZalozenieAsync(klient, rok, dzien);

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);

        Match dopasowanie = Regex.Match(odpowiedz.Headers.Location!.OriginalString,
            @"spis=([0-9a-fA-F-]{36})", RegexOptions.None, TimeSpan.FromSeconds(5));

        Assert.True(dopasowanie.Success, "Przekierowanie nie wskazuje założonego spisu.");

        return dopasowanie.Groups[1].Value;
    }

    private static async Task<string> StronaAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();

        return await AplikacjaTestowa.TrescAsync(odpowiedz);
    }
}
