using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FirmaPro.Testy;

/// <summary>
/// Wizualizacja faktury ustrukturyzowanej.
/// </summary>
/// <remarks>
/// Po wysłaniu do KSeF fakturą jest plik XML, który tam trafił - wiersze
/// naszej bazy są tylko jego odbiciem. Testy pilnują, żeby program oddawał
/// dokument, a nie odbicie: pobrany plik ma być tym samym co do bajtu,
/// a wydruk ma powstawać z niego.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyWizualizacji(AplikacjaTestowa aplikacja)
{
    /// <summary>
    /// Pobrany plik XML jest dokładnie tym, który poszedł do KSeF.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Dokument złożony ponownie miałby świeży
    /// znacznik czasu wytworzenia, a więc inny skrót SHA-256 - i nie zgadzałby
    /// się ani z kodem QR na wydruku, ani z tym, co widnieje w systemie.
    /// Takim plikiem nie dałoby się niczego udowodnić.
    /// </remarks>
    [Fact]
    public async Task PobranyXmlToDokladnieTenWyslanyDoKsef()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        var atrapa = new AtrapaKsef();
        aplikacja.Ksef = atrapa;

        try
        {
            await ZapiszTokenAsync(klient);
            string adres = await WystawIWyslijAsync(klient);

            Assert.Single(atrapa.OdebraneFaktury);
            byte[] wyslany = atrapa.OdebraneFaktury[0];

            byte[] pobrany = await PobierzAsync(klient, adres + "?handler=Xml");

            Assert.Equal(wyslany, pobrany);

            // Ten sam skrót to ten sam dokument - o to chodzi w kodzie QR.
            Assert.Equal(Convert.ToBase64String(SHA256.HashData(wyslany)),
                         Convert.ToBase64String(SHA256.HashData(pobrany)));
        }
        finally
        {
            aplikacja.Ksef = null;
            await UsunTokenAsync(klient);
        }
    }

    /// <summary>
    /// Ponowne pobranie daje ten sam plik, a nie świeżo złożony.
    /// </summary>
    /// <remarks>
    /// Gdyby program składał dokument przy każdym pobraniu, dwa pliki
    /// pobrane w odstępie sekundy różniłyby się znacznikiem czasu -
    /// i miałyby dwa różne skróty dla jednej faktury.
    /// </remarks>
    [Fact]
    public async Task PowtorzonePobranieDajeTenSamPlik()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Ksef = new AtrapaKsef();

        try
        {
            await ZapiszTokenAsync(klient);
            string adres = await WystawIWyslijAsync(klient);

            byte[] pierwszy = await PobierzAsync(klient, adres + "?handler=Xml");
            byte[] drugi = await PobierzAsync(klient, adres + "?handler=Xml");

            Assert.Equal(pierwszy, drugi);
        }
        finally
        {
            aplikacja.Ksef = null;
            await UsunTokenAsync(klient);
        }
    }

    /// <summary>
    /// Wydruk faktury przyjętej powstaje z zapisanego dokumentu.
    /// </summary>
    /// <remarks>
    /// Sprawdzamy to pośrednio, ale wiążąco: wydruk niesie numer KSeF i kod QR,
    /// czyli nie jest projektem, a jego treść zgadza się z pozycją zapisaną
    /// w wysłanym pliku. Samego układu strony testem się nie sprawdzi -
    /// to trzeba obejrzeć.
    /// </remarks>
    [Fact]
    public async Task WydrukPrzyjetejNiosleNumerKsefIKodQr()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Ksef = new AtrapaKsef();

        try
        {
            await ZapiszTokenAsync(klient);
            string adres = await WystawIWyslijAsync(klient);

            byte[] pdf = await PobierzAsync(klient, adres + "?handler=Pdf");

            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5),
                StringComparison.Ordinal);

            // Kod QR jest odnośnikiem - a odnośniki w PDF nie są kompresowane,
            // więc adres da się znaleźć w samym pliku.
            string tresc = Encoding.Latin1.GetString(pdf);
            Assert.Contains("ksef", tresc, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            aplikacja.Ksef = null;
            await UsunTokenAsync(klient);
        }
    }

    /// <summary>
    /// Faktura niewysłana nadal daje podgląd XML - składany na bieżąco.
    /// </summary>
    /// <remarks>
    /// Przed wysyłką nie ma czego przechowywać, a podejrzenie dokumentu przed
    /// wysłaniem go do urzędu jest właśnie wtedy najbardziej potrzebne.
    /// </remarks>
    [Fact]
    public async Task FakturaNiewyslanaMaPodgladXml()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string adres = await WystawAsync(klient);

        byte[] xml = await PobierzAsync(klient, adres + "?handler=Xml");
        string tekst = Encoding.UTF8.GetString(xml);

        Assert.Contains("<Faktura", tekst, StringComparison.Ordinal);
        Assert.Contains("Usługa do wizualizacji", tekst, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ pomocnicze

    private static async Task<byte[]> PobierzAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();

        return await odpowiedz.Content.ReadAsByteArrayAsync();
    }

    private static async Task<string> WystawAsync(HttpClient klient)
    {
        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = await PierwszyKontrahentAsync(klient),
                ["DataWystawienia"] = "2026-08-12",
                ["DataSprzedazy"] = "2026-08-12",
                ["TerminPlatnosci"] = "2026-08-26",
                ["FormaPlatnosci"] = "Przelew",
                ["Pozycje[0].Nazwa"] = "Usługa do wizualizacji",
                ["Pozycje[0].Jednostka"] = "usł.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "1500",
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        return wystawienie.Headers.Location!.OriginalString;
    }

    private static async Task<string> WystawIWyslijAsync(HttpClient klient)
    {
        string adres = await WystawAsync(klient);

        using HttpResponseMessage wysylka = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, adres + "?handler=Wyslij", new Dictionary<string, string>(),
            adresFormularza: adres);

        Assert.Equal(HttpStatusCode.Redirect, wysylka.StatusCode);

        return adres;
    }

    private static async Task<string> PierwszyKontrahentAsync(HttpClient klient)
    {
        using HttpResponseMessage formularz =
            await klient.GetAsync(new Uri("/Faktury/Nowa", UriKind.Relative));

        string html = await formularz.Content.ReadAsStringAsync();

        int poczatek = html.IndexOf("name=\"KontrahentId\"", StringComparison.Ordinal);
        Assert.True(poczatek > 0, "Formularz nie ma listy kontrahentów.");

        int wartosc = html.IndexOf("<option value=\"", poczatek, StringComparison.Ordinal)
                      + "<option value=\"".Length;

        while (html[wartosc] == '"')
        {
            wartosc = html.IndexOf("<option value=\"", wartosc, StringComparison.Ordinal)
                      + "<option value=\"".Length;
        }

        return html[wartosc..html.IndexOf('"', wartosc)];
    }

    private static async Task ZapiszTokenAsync(HttpClient klient) =>
        (await AplikacjaTestowa.WyslijFormularzAsync(klient, "/Ustawienia",
            DaneUstawien("TOKEN-KSEF-123"))).Dispose();

    private static async Task UsunTokenAsync(HttpClient klient)
    {
        Dictionary<string, string> pola = DaneUstawien(string.Empty);
        pola["UsunToken"] = "true";

        (await AplikacjaTestowa.WyslijFormularzAsync(klient, "/Ustawienia", pola)).Dispose();
    }

    private static Dictionary<string, string> DaneUstawien(string token) =>
        new(StringComparer.Ordinal)
        {
            ["Nazwa"] = "Moja Firma sp. z o.o.",
            ["Nip"] = "5252248481",
            ["KodKraju"] = "PL",
            ["AdresLinia1"] = "ul. Prosta 51",
            ["AdresLinia2"] = "00-838 Warszawa",
            ["DomyslnyTerminPlatnosciDni"] = "14",
            ["Srodowisko"] = "Test",
            ["TypOkresuVat"] = "Miesieczny",
            ["TokenKsef"] = token
        };
}
