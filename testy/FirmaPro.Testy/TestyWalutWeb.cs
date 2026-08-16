using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FirmaPro.Web.Uslugi;

namespace FirmaPro.Testy;

/// <summary>
/// Faktura walutowa przechodzona tak, jak przechodzi ją użytkownik.
/// </summary>
/// <remarks>
/// Kurs pobiera się raz - w chwili wystawienia - i zostaje przy dokumencie
/// na zawsze. Testy pilnują obu końców tej drogi: że kurs trafia do bazy
/// i na ekran, oraz że przy niedostępnym serwisie NBP program odmawia
/// wystawienia, zamiast po cichu przyjąć kurs jeden do jednego.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyWalutWeb(AplikacjaTestowa aplikacja)
{
    /// <summary>Faktura w euro pokazuje kurs i kwotę podatku w złotych.</summary>
    [Fact]
    public async Task FakturaWEuroPokazujeKursIPodatekWZlotych()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Kursy.Blad = null;

        string html = await WystawAsync(klient, "EUR", "1000");

        // 1000 EUR netto przy stawce 23% to 230 EUR podatku.
        Assert.Contains("230,00", html, StringComparison.Ordinal);
        Assert.Contains("1 230,00 EUR", html, StringComparison.Ordinal);

        // Kurs razem z numerem tabeli - to on jest dowodem przeliczenia.
        Assert.Contains("1 EUR = 4,2837 PLN", html, StringComparison.Ordinal);
        Assert.Contains("160/A/NBP/2026", html, StringComparison.Ordinal);

        // 230 EUR po 4,2837 daje 985,25 zł.
        Assert.Contains("985,25 PLN", html, StringComparison.Ordinal);
    }

    /// <summary>Faktura złotowa nie pokazuje żadnego kursu.</summary>
    /// <remarks>
    /// Kurs jeden do jednego wypisany przy każdej zwykłej fakturze byłby
    /// szumem, a przy okazji sugerowałby, że coś tu przeliczono.
    /// </remarks>
    [Fact]
    public async Task FakturaZlotowaNiePokazujeKursu()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Kursy.Blad = null;

        string html = await WystawAsync(klient, "PLN", "500");

        Assert.DoesNotContain("tabela NBP", html, StringComparison.Ordinal);
        Assert.DoesNotContain("VAT w złotych", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bez kursu faktura walutowa w ogóle nie powstaje.
    /// </summary>
    /// <remarks>
    /// To najważniejszy test w tym zbiorze. Wystawienie dokumentu z kursem
    /// przyjętym „na oko" oznacza zaniżony albo zawyżony podatek, a faktury
    /// wysłanej do KSeF nie da się już poprawić inaczej niż korektą.
    /// </remarks>
    [Fact]
    public async Task NiedostepnyKursWstrzymujeWystawienie()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        aplikacja.Kursy.Blad = new BladKursuException(
            "Nie udało się połączyć z serwisem kursów NBP.");

        try
        {
            using HttpResponseMessage odpowiedz = await WyslijAsync(klient, "EUR", "1000");

            // Formularz wraca z błędem, zamiast przekierować do gotowej faktury.
            Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);

            string html = await AplikacjaTestowa.TrescAsync(odpowiedz);
            Assert.Contains("kursów NBP", html, StringComparison.Ordinal);
        }
        finally
        {
            aplikacja.Kursy.Blad = null;
        }
    }

    /// <summary>Faktura złotowa wystawia się nawet wtedy, gdy NBP milczy.</summary>
    /// <remarks>
    /// Przeważająca większość faktur jest w złotych. Gdyby awaria serwisu
    /// kursów zatrzymywała także je, program stawałby w miejscu z powodu,
    /// który go nie dotyczy.
    /// </remarks>
    [Fact]
    public async Task AwariaKursowNieBlokujeFakturZlotowych()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        aplikacja.Kursy.Blad = new BladKursuException("NBP nie odpowiada.");

        try
        {
            using HttpResponseMessage odpowiedz = await WyslijAsync(klient, "PLN", "100");

            Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        }
        finally
        {
            aplikacja.Kursy.Blad = null;
        }
    }

    // ------------------------------------------------------------ pomocnicze

    private static async Task<HttpResponseMessage> WyslijAsync(HttpClient klient,
                                                               string waluta, string cena)
    {
        string kontrahent = await PierwszyKontrahentAsync(klient);
        string dzis = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = kontrahent,
                ["DataWystawienia"] = dzis,
                ["DataSprzedazy"] = dzis,
                ["TerminPlatnosci"] = dzis,
                ["FormaPlatnosci"] = "Przelew",
                ["Waluta"] = waluta,
                ["Pozycje[0].Nazwa"] = "Usługa programistyczna",
                ["Pozycje[0].Jednostka"] = "usł.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = cena,
                ["Pozycje[0].KodStawki"] = "23"
            });
    }

    private static async Task<string> WystawAsync(HttpClient klient, string waluta, string cena)
    {
        using HttpResponseMessage wystawienie = await WyslijAsync(klient, waluta, cena);

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        string adres = wystawienie.Headers.Location!.OriginalString;
        Assert.StartsWith("/Faktury/Szczegoly/", adres, StringComparison.Ordinal);

        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        szczegoly.EnsureSuccessStatusCode();

        return await AplikacjaTestowa.TrescAsync(szczegoly);
    }

    private static async Task<string> PierwszyKontrahentAsync(HttpClient klient)
    {
        using HttpResponseMessage strona =
            await klient.GetAsync(new Uri("/Faktury/Nowa", UriKind.Relative));

        strona.EnsureSuccessStatusCode();
        string html = await strona.Content.ReadAsStringAsync();

        Match dopasowanie = Regex.Match(html,
            @"<option value=""([0-9a-fA-F-]{36})""",
            RegexOptions.None, TimeSpan.FromSeconds(5));

        Assert.True(dopasowanie.Success, "Formularz nie zawiera listy kontrahentów.");

        return dopasowanie.Groups[1].Value;
    }
}
