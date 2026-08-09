using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FirmaPro.Testy;

/// <summary>
/// Aplikacja webowa uruchomiona w pamięci, na własnej bazie testowej.
/// </summary>
/// <remarks>
/// <para>
/// Program startuje tak samo jak w produkcji: wykonuje migracje i zakłada
/// dane demonstracyjne. Testy przechodzą więc tę samą drogę co użytkownik -
/// łącznie z rejestracją usług w kontenerze. Poprzednia usterka („nie da się
/// utworzyć klienta KSeF") ujawniała się wyłącznie w czasie działania
/// programu, bo kompilacja niczego takiego nie sprawdza.
/// </para>
/// <para>
/// Sieć nie jest tu potrzebna: testy dochodzą najdalej do momentu, w którym
/// program stwierdza brak tokena, i nie próbują wołać KSeF.
/// </para>
/// </remarks>
public sealed partial class AplikacjaTestowa : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly BazaTestowa _baza = new();

    public async Task InitializeAsync() => await _baza.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _baza.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:Baza", _baza.Polaczenie);
        builder.UseEnvironment("Development");

        // Zapytania SQL w dzienniku zaśmiecałyby wynik testów.
        builder.ConfigureLogging(dziennik => dziennik.SetMinimumLevel(LogLevel.Warning));
    }

    /// <summary>
    /// Tworzy klienta, który nie podąża za przekierowaniami.
    /// </summary>
    /// <remarks>
    /// Samo przekierowanie bywa treścią testu: żądanie strony bez logowania
    /// ma odesłać do formularza logowania, a nie pokazać dane.
    /// </remarks>
    public HttpClient UtworzKlienta() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    /// <summary>Loguje się na konto demonstracyjne i zwraca gotowego klienta.</summary>
    public async Task<HttpClient> ZalogujAsync()
    {
        HttpClient klient = UtworzKlienta();

        HttpResponseMessage odpowiedz = await WyslijFormularzAsync(klient, "/Logowanie",
            new Dictionary<string, string>
            {
                ["Email"] = "demo@firmapro.pl",
                ["Haslo"] = "demo1234"
            });

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        Assert.Equal("/Faktury", odpowiedz.Headers.Location?.OriginalString);

        return klient;
    }

    /// <summary>
    /// Wysyła formularz pod wskazany adres, dołączając token zabezpieczający.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core odrzuca żądania POST bez tokena chroniącego przed
    /// podszyciem się (CSRF). Test musi więc najpierw pobrać stronę i wyjąć
    /// token z formularza - dokładnie tak, jak robi to przeglądarka.
    /// </remarks>
    public static async Task<HttpResponseMessage> WyslijFormularzAsync(
        HttpClient klient,
        string adres,
        IDictionary<string, string> pola,
        string? adresFormularza = null)
    {
        ArgumentNullException.ThrowIfNull(klient);
        ArgumentNullException.ThrowIfNull(pola);

        string zrodlo = adresFormularza ?? adres;
        using HttpResponseMessage strona = await klient.GetAsync(new Uri(zrodlo, UriKind.Relative));
        strona.EnsureSuccessStatusCode();

        string tresc = await strona.Content.ReadAsStringAsync();

        var wszystkie = new Dictionary<string, string>(pola, StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = TokenFormularza(tresc)
        };

        using var zawartosc = new FormUrlEncodedContent(wszystkie);
        return await klient.PostAsync(new Uri(adres, UriKind.Relative), zawartosc);
    }

    /// <summary>
    /// Pobiera treść strony w postaci czytelnej dla testu.
    /// </summary>
    /// <remarks>
    /// Razor zamienia polskie znaki na encje (<c>&amp;#x142;</c> zamiast „ł"),
    /// więc szukanie w surowym kodzie strony słowa z ogonkiem nigdy by się nie
    /// udało. Rozkodowanie sprawia, że testy porównują to, co widzi człowiek.
    /// </remarks>
    public static async Task<string> TrescAsync(HttpResponseMessage odpowiedz)
    {
        ArgumentNullException.ThrowIfNull(odpowiedz);
        return WebUtility.HtmlDecode(await odpowiedz.Content.ReadAsStringAsync());
    }

    /// <summary>Wyjmuje token zabezpieczający z kodu strony.</summary>
    private static string TokenFormularza(string html)
    {
        Match dopasowanie = WzorzecTokena().Match(html);

        Assert.True(dopasowanie.Success,
            "Formularz nie zawiera tokena zabezpieczającego przed podszyciem.");

        return WebUtility.HtmlDecode(dopasowanie.Groups[1].Value);
    }

    // Kolejność atrybutów w wygenerowanym znaczniku nie jest gwarantowana,
    // więc szukamy nazwy pola, a wartości dopiero za nią.
    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*?value=""([^""]+)""")]
    private static partial Regex WzorzecTokena();
}

/// <summary>
/// Zbiór testów współdzielących jedną uruchomioną aplikację.
/// </summary>
[CollectionDefinition(Nazwa)]
public sealed class KolekcjaAplikacji : ICollectionFixture<AplikacjaTestowa>
{
    public const string Nazwa = "aplikacja";
}
