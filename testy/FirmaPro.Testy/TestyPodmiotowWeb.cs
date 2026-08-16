using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FirmaPro.Testy;

/// <summary>
/// Podmioty trzecie przechodzone tak, jak przechodzi je użytkownik.
/// </summary>
/// <remarks>
/// Cała droga: wpisanie w formularzu, zapis przy fakturze, pokazanie na
/// ekranie i wreszcie sekcja Podmiot3 w pliku dla KSeF. Zerwanie tej drogi
/// w którymkolwiek miejscu kończy się tym samym: jednostka podrzędna albo
/// faktor nie dostaje dokumentu.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyPodmiotowWeb(AplikacjaTestowa aplikacja)
{
    [Fact]
    public async Task PodmiotZFormularzaTrafiaNaFaktureIDoPliku()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string adres = await WystawAsync(klient, new Dictionary<string, string>
        {
            ["PodmiotyInne[0].Nazwa"] = "Oddział w Krakowie",
            ["PodmiotyInne[0].Nip"] = "1180000001",
            ["PodmiotyInne[0].AdresLinia1"] = "ul. Długa 5",
            ["PodmiotyInne[0].Rola"] = "Odbiorca"
        });

        string html = await StronaAsync(klient, adres);

        Assert.Contains("Podmioty inne", html, StringComparison.Ordinal);
        Assert.Contains("Oddział w Krakowie", html, StringComparison.Ordinal);
        Assert.Contains("Odbiorca", html, StringComparison.Ordinal);

        // Najważniejsze: podmiot musi być w dokumencie, a nie tylko na ekranie.
        string xml = await TekstAsync(klient, adres + "?handler=Xml");

        Assert.Contains("<Podmiot3>", xml, StringComparison.Ordinal);
        Assert.Contains("Oddział w Krakowie", xml, StringComparison.Ordinal);
        Assert.Contains("<Rola>2</Rola>", xml, StringComparison.Ordinal);
    }

    /// <summary>Dodatkowy nabywca niesie swój udział w należności.</summary>
    [Fact]
    public async Task DodatkowyNabywcaMaUdzial()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string adres = await WystawAsync(klient, new Dictionary<string, string>
        {
            ["PodmiotyInne[0].Nazwa"] = "Druga Spółka sp. z o.o.",
            ["PodmiotyInne[0].Nip"] = "7010001453",
            ["PodmiotyInne[0].Rola"] = "DodatkowyNabywca",
            ["PodmiotyInne[0].Udzial"] = "40"
        });

        string xml = await TekstAsync(klient, adres + "?handler=Xml");

        Assert.Contains("<Rola>4</Rola>", xml, StringComparison.Ordinal);
        Assert.Contains("<Udzial>40</Udzial>", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Podmiot bez roli zatrzymuje wystawienie.
    /// </summary>
    /// <remarks>
    /// Dokument bez roli i tak zostałby odrzucony przez schemat - lepiej
    /// powiedzieć to przy formularzu niż po wysyłce do urzędu.
    /// </remarks>
    [Fact]
    public async Task PodmiotBezRoliZatrzymujeWystawienie()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz = await WyslijAsync(klient,
            new Dictionary<string, string>
            {
                ["PodmiotyInne[0].Nazwa"] = "Ktoś bez roli",
                ["PodmiotyInne[0].Nip"] = "1180000001"
            });

        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);
        Assert.Contains("rolę", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pusty wiersz formularza nie tworzy pustego podmiotu.</summary>
    /// <remarks>
    /// Formularz zawsze pokazuje jeden wiersz do wypełnienia - gdyby pusty
    /// trafiał do dokumentu, każda zwykła faktura miałaby sekcję Podmiot3
    /// bez nazwy i bez roli, czyli niezgodną ze schematem.
    /// </remarks>
    [Fact]
    public async Task PustyWierszNieTworzyPodmiotu()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string adres = await WystawAsync(klient, new Dictionary<string, string>
        {
            ["PodmiotyInne[0].Nazwa"] = string.Empty,
            ["PodmiotyInne[0].Nip"] = string.Empty
        });

        string xml = await TekstAsync(klient, adres + "?handler=Xml");

        Assert.DoesNotContain("<Podmiot3>", xml, StringComparison.Ordinal);
    }

    /// <summary>Formularz faktury ma sekcję podmiotów trzecich.</summary>
    [Fact]
    public async Task FormularzMaSekcjePodmiotow()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, "/Faktury/Nowa");

        Assert.Contains("Podmioty inne", html, StringComparison.Ordinal);
        Assert.Contains("PodmiotyInne[0].Nazwa", html, StringComparison.Ordinal);

        // Wszystkie role muszą być do wyboru - lista skrócona zmuszałaby
        // użytkownika do opisywania słowami czegoś, co schemat już nazywa.
        Assert.Contains("Faktor", html, StringComparison.Ordinal);
        Assert.Contains("Dodatkowy nabywca", html, StringComparison.Ordinal);
        Assert.Contains("Członek grupy VAT", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ pomocnicze

    private static async Task<HttpResponseMessage> WyslijAsync(
        HttpClient klient, Dictionary<string, string> podmiot)
    {
        string dzis = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var pola = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KontrahentId"] = await PierwszyKontrahentAsync(klient),
            ["DataWystawienia"] = dzis,
            ["DataSprzedazy"] = dzis,
            ["TerminPlatnosci"] = dzis,
            ["FormaPlatnosci"] = "Przelew",
            ["Pozycje[0].Nazwa"] = "Usługa dla oddziału",
            ["Pozycje[0].Jednostka"] = "usł.",
            ["Pozycje[0].Ilosc"] = "1",
            ["Pozycje[0].CenaNetto"] = "1000",
            ["Pozycje[0].KodStawki"] = "23"
        };

        foreach ((string klucz, string wartosc) in podmiot)
        {
            pola[klucz] = wartosc;
        }

        return await AplikacjaTestowa.WyslijFormularzAsync(klient, "/Faktury/Nowa", pola);
    }

    private static async Task<string> WystawAsync(HttpClient klient,
                                                  Dictionary<string, string> podmiot)
    {
        using HttpResponseMessage odpowiedz = await WyslijAsync(klient, podmiot);

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);

        return odpowiedz.Headers.Location!.OriginalString;
    }

    private static async Task<string> StronaAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();

        return await AplikacjaTestowa.TrescAsync(odpowiedz);
    }

    private static async Task<string> TekstAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();

        return Encoding.UTF8.GetString(await odpowiedz.Content.ReadAsByteArrayAsync());
    }

    private static async Task<string> PierwszyKontrahentAsync(HttpClient klient)
    {
        string html = await StronaAsync(klient, "/Faktury/Nowa");

        Match dopasowanie = Regex.Match(html,
            @"<option value=""([0-9a-fA-F-]{36})""",
            RegexOptions.None, TimeSpan.FromSeconds(5));

        Assert.True(dopasowanie.Success, "Formularz nie zawiera listy kontrahentów.");

        return dopasowanie.Groups[1].Value;
    }
}
