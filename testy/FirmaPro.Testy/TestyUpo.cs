using System.Net;
using FirmaPro.Ksef;

namespace FirmaPro.Testy;

/// <summary>
/// Urzędowe poświadczenie odbioru faktury.
/// </summary>
/// <remarks>
/// UPO jest jedynym dowodem, że faktura weszła do obiegu prawnego - przy
/// kontroli liczy się ono, a nie wpis w naszej bazie. Te testy pilnują dwóch
/// rzeczy: że poświadczenie w ogóle zostaje pobrane i zapisane, oraz że jego
/// brak nie psuje stanu faktury, która przecież została przyjęta.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyUpo(AplikacjaTestowa aplikacja)
{
    [Fact]
    public async Task PoWyslaniuFakturaMaPobranePoswiadczenie()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        var atrapa = new AtrapaKsef();
        aplikacja.Ksef = atrapa;

        try
        {
            await ZapiszTokenAsync(klient);
            string adres = await WystawIWyslijAsync(klient);

            string html = await StronaAsync(klient, adres);

            Assert.Contains("Poświadczenie odbioru", html, StringComparison.Ordinal);
            Assert.Contains("Pobierz UPO", html, StringComparison.Ordinal);
            Assert.DoesNotContain("jeszcze nie pobrane", html, StringComparison.Ordinal);
        }
        finally
        {
            aplikacja.Ksef = null;
            await UsunTokenAsync(klient);
        }
    }

    /// <summary>Zapisane poświadczenie da się pobrać jako plik.</summary>
    [Fact]
    public async Task PoswiadczenieMoznaPobracJakoPlik()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        var atrapa = new AtrapaKsef();
        aplikacja.Ksef = atrapa;

        try
        {
            await ZapiszTokenAsync(klient);
            string adres = await WystawIWyslijAsync(klient);
            Guid id = IdentyfikatorZAdresu(adres);

            using HttpResponseMessage plik = await klient.GetAsync(
                new Uri($"/Faktury/Szczegoly/{id}?handler=Upo", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, plik.StatusCode);
            Assert.Equal("application/xml", plik.Content.Headers.ContentType?.MediaType);

            string tresc = await plik.Content.ReadAsStringAsync();
            Assert.Contains("UPO", tresc, StringComparison.Ordinal);
        }
        finally
        {
            aplikacja.Ksef = null;
            await UsunTokenAsync(klient);
        }
    }

    /// <summary>
    /// Brak gotowego poświadczenia nie może zepsuć przyjętej faktury.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Poświadczenie powstaje z opóźnieniem,
    /// więc pierwsza próba często się nie udaje - a faktura jest już wtedy
    /// w KSeF i jej stan musi to odzwierciedlać.
    /// </remarks>
    [Fact]
    public async Task NiegotowePoswiadczenieNiePsujePrzyjetejFaktury()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        var atrapa = new AtrapaKsef { UpoGotowe = false };
        aplikacja.Ksef = atrapa;

        try
        {
            await ZapiszTokenAsync(klient);
            string adres = await WystawIWyslijAsync(klient);

            string html = await StronaAsync(klient, adres);

            // Faktura przyjęta, poświadczenia jeszcze nie ma.
            Assert.Contains("Przyjęta", html, StringComparison.Ordinal);
            Assert.Contains("jeszcze nie pobrane", html, StringComparison.Ordinal);

            // Ponowna próba, tym razem z gotowym poświadczeniem.
            atrapa.UpoGotowe = true;
            Guid id = IdentyfikatorZAdresu(adres);

            using HttpResponseMessage ponowna = await AplikacjaTestowa.WyslijFormularzAsync(
                klient, $"/Faktury/Szczegoly/{id}?handler=Upo",
                new Dictionary<string, string>(),
                adresFormularza: adres);

            Assert.Equal(HttpStatusCode.Redirect, ponowna.StatusCode);

            string poPobraniu = await StronaAsync(klient, adres);
            Assert.DoesNotContain("jeszcze nie pobrane", poPobraniu, StringComparison.Ordinal);
        }
        finally
        {
            aplikacja.Ksef = null;
            await UsunTokenAsync(klient);
        }
    }

    // ------------------------------------------------------------ pomocnicze

    private static Guid IdentyfikatorZAdresu(string adres) =>
        Guid.Parse(adres.Split('/')[^1]);

    private static async Task<string> StronaAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();
        return await AplikacjaTestowa.TrescAsync(odpowiedz);
    }

    /// <summary>Wystawia fakturę, wysyła ją do KSeF i zwraca adres szczegółów.</summary>
    private static async Task<string> WystawIWyslijAsync(HttpClient klient)
    {
        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = await PierwszyKontrahentAsync(klient),
                ["DataWystawienia"] = "2026-08-12",
                ["DataSprzedazy"] = "2026-08-12",
                ["TerminPlatnosci"] = "2026-08-26",
                ["FormaPlatnosci"] = "Przelew",
                ["Pozycje[0].Nazwa"] = "Usługa do poświadczenia",
                ["Pozycje[0].Jednostka"] = "usł.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "1000",
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);
        string adres = wystawienie.Headers.Location!.OriginalString;

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

        // Pierwsza pozycja listy to „— wybierz —" z pustą wartością.
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
