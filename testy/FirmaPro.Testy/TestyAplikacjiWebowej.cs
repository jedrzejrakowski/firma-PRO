using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FirmaPro.Testy;

/// <summary>
/// Testy aplikacji webowej przechodzone tak, jak przechodzi je użytkownik.
/// </summary>
/// <remarks>
/// Sprawdzają rzeczy, których kompilator nie widzi: czy strona w ogóle się
/// otwiera, czy usługi dają się utworzyć, czy zapis formularza nie gubi
/// danych i czy dokumenty jednej firmy nie wyciekają do drugiej.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyAplikacjiWebowej(AplikacjaTestowa aplikacja)
{
    [Fact]
    public async Task StronaFakturBezLogowaniaOdsylaDoFormularza()
    {
        using HttpClient klient = aplikacja.UtworzKlienta();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Faktury", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        Assert.Contains("/Logowanie", odpowiedz.Headers.Location!.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BledneHasloNieWpuszczaDoProgramu()
    {
        using HttpClient klient = aplikacja.UtworzKlienta();

        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Logowanie", new Dictionary<string, string>
            {
                ["Email"] = "demo@firmapro.pl",
                ["Haslo"] = "nie-to-haslo"
            });

        // Formularz wraca ze stanem 200 i komunikatem - nie przekierowaniem.
        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);
        Assert.Contains("Nieprawidłowy adres e-mail lub hasło",
            await AplikacjaTestowa.TrescAsync(odpowiedz), StringComparison.Ordinal);
    }

    /// <summary>
    /// Formularz wystawiania faktury musi się otworzyć.
    /// </summary>
    /// <remarks>
    /// Ta strona jako jedyna sięga po klienta KSeF. Zła rejestracja usług
    /// kończyła się tu błędem 500 mimo poprawnej kompilacji - stąd osobny
    /// test samego otwarcia strony.
    /// </remarks>
    [Fact]
    public async Task FormularzNowejFakturyOtwieraSie()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Faktury/Nowa", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);
        Assert.Contains("Wystaw fakturę", await AplikacjaTestowa.TrescAsync(odpowiedz),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WszystkieStronyDzialajaPoZalogowaniu()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        foreach (string adres in new[] { "/Faktury", "/Kontrahenci", "/Kontrahenci/Nowy", "/Ustawienia" })
        {
            using HttpResponseMessage odpowiedz =
                await klient.GetAsync(new Uri(adres, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);
        }
    }

    /// <summary>
    /// Pełna droga: wystawienie faktury i pobranie jej pliku XML.
    /// </summary>
    [Fact]
    public async Task MoznaWystawicFaktureIPobracJejXml()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string idKontrahenta = await PierwszyKontrahentAsync(klient);
        string dzis = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = idKontrahenta,
                ["DataWystawienia"] = dzis,
                ["DataSprzedazy"] = dzis,
                ["TerminPlatnosci"] = dzis,
                ["FormaPlatnosci"] = "Przelew",
                ["Pozycje[0].Nazwa"] = "Usługa testowa",
                ["Pozycje[0].Jednostka"] = "szt.",
                ["Pozycje[0].Ilosc"] = "2",
                ["Pozycje[0].CenaNetto"] = "100",
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        string adresSzczegolow = Sciezka(wystawienie);
        Assert.StartsWith("/Faktury/Szczegoly/", adresSzczegolow, StringComparison.Ordinal);

        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri(adresSzczegolow, UriKind.Relative));
        szczegoly.EnsureSuccessStatusCode();

        string html = await AplikacjaTestowa.TrescAsync(szczegoly);
        // 2 x 100 zł netto przy stawce 23% daje 246 zł brutto.
        Assert.Contains("246.00", html, StringComparison.Ordinal);

        string idFaktury = adresSzczegolow["/Faktury/Szczegoly/".Length..];
        using HttpResponseMessage plik = await klient.GetAsync(
            new Uri($"/Faktury/Szczegoly/{idFaktury}?handler=Xml", UriKind.Relative));

        plik.EnsureSuccessStatusCode();
        string xml = await plik.Content.ReadAsStringAsync();

        Assert.Contains("http://crd.gov.pl/wzor/2025/06/25/13775/", xml, StringComparison.Ordinal);
        Assert.Contains("<P_13_1>200.00</P_13_1>", xml, StringComparison.Ordinal);
        Assert.Contains("<P_14_1>46.00</P_14_1>", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Wysyłka bez tokena ma skończyć się czytelnym komunikatem.
    /// </summary>
    /// <remarks>
    /// Test celowo nie sięga do sieci: program zatrzymuje się wcześniej, na
    /// braku tokena. Sprawdzamy, że mówi o tym wprost, zamiast wywracać się
    /// błędem serwera.
    /// </remarks>
    [Fact]
    public async Task WysylkaBezTokenaKonczySieKomunikatemANieBledem()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        // Testy dzielą jedną firmę demonstracyjną, więc token mógł zostać po
        // wcześniejszym teście. Kasujemy go, żeby wynik nie zależał od tego,
        // w jakiej kolejności testy się wykonały.
        await UsunTokenAsync(klient);

        string idKontrahenta = await PierwszyKontrahentAsync(klient);
        string dzis = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = idKontrahenta,
                ["DataWystawienia"] = dzis,
                ["Pozycje[0].Nazwa"] = "Usługa bez tokena",
                ["Pozycje[0].Jednostka"] = "szt.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "50",
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        string adresSzczegolow = Sciezka(wystawienie);
        string idFaktury = adresSzczegolow["/Faktury/Szczegoly/".Length..];

        using HttpResponseMessage wysylka = await AplikacjaTestowa.WyslijFormularzAsync(
            klient,
            $"/Faktury/Szczegoly/{idFaktury}?handler=Wyslij",
            new Dictionary<string, string>(),
            adresFormularza: adresSzczegolow);

        Assert.Equal(HttpStatusCode.Redirect, wysylka.StatusCode);

        using HttpResponseMessage po =
            await klient.GetAsync(new Uri(adresSzczegolow, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, po.StatusCode);
        Assert.Contains("Brak tokena KSeF", await AplikacjaTestowa.TrescAsync(po),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Zapis ustawień z pustym polem tokena nie może go skasować.
    /// </summary>
    /// <remarks>
    /// Token nigdy nie wraca na stronę, więc każdy zapis innego pola
    /// wysyłałby puste pole tokena. Gdyby program traktował je jako
    /// „wyczyść", zmiana adresu odcinałaby firmę od KSeF.
    /// </remarks>
    [Fact]
    public async Task ZapisUstawienZPustymPolemNieKasujeTokena()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        Dictionary<string, string> daneFirmy = DaneFirmy();

        // 1. zapisujemy token
        using (HttpResponseMessage zapis = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Ustawienia",
            new Dictionary<string, string>(daneFirmy, StringComparer.Ordinal)
            {
                ["TokenKsef"] = "TOKEN-TESTOWY-0123456789"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
        }

        Assert.Contains("zapisany", await StanTokenaAsync(klient), StringComparison.Ordinal);

        // 2. zapisujemy inne pole, zostawiając pole tokena puste
        using (HttpResponseMessage zapis = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Ustawienia",
            new Dictionary<string, string>(daneFirmy, StringComparer.Ordinal)
            {
                ["MiejsceWystawienia"] = "Gdańsk",
                ["TokenKsef"] = string.Empty
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
        }

        string strona = await StanTokenaAsync(klient);
        Assert.Contains("zapisany", strona, StringComparison.Ordinal);
        Assert.Contains("Gdańsk", strona, StringComparison.Ordinal);

        // 3. dopiero zaznaczenie pola wyboru usuwa token
        using (HttpResponseMessage zapis = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Ustawienia",
            new Dictionary<string, string>(daneFirmy, StringComparer.Ordinal)
            {
                ["UsunToken"] = "true"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
        }

        Assert.DoesNotContain(">zapisany<", await StanTokenaAsync(klient),
            StringComparison.Ordinal);
    }

    /// <summary>Token nigdy nie może pojawić się w kodzie strony.</summary>
    [Fact]
    public async Task ZapisanyTokenNieWracaNaStrone()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        const string Token = "TAJNY-TOKEN-DO-WYKRYCIA-9876";

        using (HttpResponseMessage zapis = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Ustawienia", new Dictionary<string, string>(DaneFirmy(), StringComparer.Ordinal)
            {
                ["TokenKsef"] = Token
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
        }

        Assert.DoesNotContain(Token, await StanTokenaAsync(klient), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>
    /// Ścieżka z nagłówka przekierowania.
    /// </summary>
    /// <remarks>
    /// Serwer odpowiada adresem bezwzględnym; do dalszych żądań i porównań
    /// wygodniejsza jest sama ścieżka.
    /// </remarks>
    private static string Sciezka(HttpResponseMessage odpowiedz)
    {
        Uri adres = odpowiedz.Headers.Location!;
        return adres.IsAbsoluteUri ? adres.PathAndQuery : adres.OriginalString;
    }

    /// <summary>Podstawowe dane firmy demonstracyjnej - komplet wymaganych pól.</summary>
    private static Dictionary<string, string> DaneFirmy() => new(StringComparer.Ordinal)
    {
        ["Nazwa"] = "Moja Firma sp. z o.o.",
        ["Nip"] = "5252248481",
        ["KodKraju"] = "PL",
        ["AdresLinia1"] = "ul. Prosta 51",
        ["AdresLinia2"] = "00-838 Warszawa",
        ["DomyslnyTerminPlatnosciDni"] = "14",
        ["Srodowisko"] = "Test"
    };

    private static async Task UsunTokenAsync(HttpClient klient)
    {
        Dictionary<string, string> pola = DaneFirmy();
        pola["UsunToken"] = "true";

        using HttpResponseMessage zapis =
            await AplikacjaTestowa.WyslijFormularzAsync(klient, "/Ustawienia", pola);

        Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
    }

    private static async Task<string> StanTokenaAsync(HttpClient klient)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Ustawienia", UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();
        return await AplikacjaTestowa.TrescAsync(odpowiedz);
    }

    /// <summary>Wyjmuje identyfikator pierwszego kontrahenta z listy wyboru.</summary>
    private static async Task<string> PierwszyKontrahentAsync(HttpClient klient)
    {
        using HttpResponseMessage strona =
            await klient.GetAsync(new Uri("/Faktury/Nowa", UriKind.Relative));

        strona.EnsureSuccessStatusCode();
        string html = await strona.Content.ReadAsStringAsync();

        // Pierwsza pozycja listy to zachęta „wybierz" z pustą wartością -
        // szukamy dopiero takiej, która niesie identyfikator.
        Match dopasowanie = Regex.Match(html,
            @"<option value=""([0-9a-fA-F-]{36})""",
            RegexOptions.None, TimeSpan.FromSeconds(5));

        Assert.True(dopasowanie.Success, "Formularz nie zawiera listy kontrahentów.");
        return dopasowanie.Groups[1].Value;
    }
}
