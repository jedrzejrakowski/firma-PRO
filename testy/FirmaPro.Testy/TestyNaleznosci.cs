using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Wpłaty, należności i przypomnienia - drogą przeglądarki.
/// </summary>
/// <remarks>
/// Pieniądze to jedyna rzecz, o którą użytkownik zapyta program pierwszego
/// dnia i ostatniego. Testy przechodzą więc całą drogę: wystawienie faktury,
/// wpłata, zniknięcie z listy należności i przypomnienie do kontrahenta.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed partial class TestyNaleznosci(AplikacjaTestowa aplikacja)
{
    /// <summary>Wystawia fakturę z podanym terminem i zwraca jej identyfikator.</summary>
    private static async Task<(string Id, string Numer)> WystawAsync(
        HttpClient klient, string nazwa, decimal cena, DateOnly termin)
    {
        using HttpResponseMessage strona =
            await klient.GetAsync(new Uri("/Faktury/Nowa", UriKind.Relative));

        strona.EnsureSuccessStatusCode();

        Match kontrahent = WzorzecKontrahenta().Match(await strona.Content.ReadAsStringAsync());
        Assert.True(kontrahent.Success, "Brak kontrahentów do wystawienia faktury.");

        string dzis = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = kontrahent.Groups[1].Value,
                ["DataWystawienia"] = dzis,
                ["TerminPlatnosci"] = termin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Pozycje[0].Nazwa"] = nazwa,
                ["Pozycje[0].Jednostka"] = "szt.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = cena.ToString(CultureInfo.InvariantCulture),
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        string id = wystawienie.Headers.Location!.OriginalString["/Faktury/Szczegoly/".Length..];

        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{id}", UriKind.Relative));

        Match numer = WzorzecNumeru().Match(await AplikacjaTestowa.TrescAsync(szczegoly));
        Assert.True(numer.Success, "Nie udało się odczytać numeru faktury.");

        return (id, numer.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> WplacAsync(
        HttpClient klient, string id, decimal kwota) =>
        AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Szczegoly/{id}?handler=Wplata",
            new Dictionary<string, string>
            {
                ["KwotaWplaty"] = kwota.ToString(CultureInfo.InvariantCulture),
                ["DataWplaty"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            adresFormularza: $"/Faktury/Szczegoly/{id}");

    [Fact]
    public async Task WplataZmniejszaNaleznoscAPelnaJaZamyka()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        // 1000 zł netto przy stawce 23% daje 1230 zł brutto.
        (string id, string numer) = await WystawAsync(
            klient, "Usługa rozliczana ratami", 1000m,
            DateOnly.FromDateTime(DateTime.Today).AddDays(14));

        using (HttpResponseMessage wplata = await WplacAsync(klient, id, 500m))
        {
            Assert.Equal(HttpStatusCode.Redirect, wplata.StatusCode);
        }

        using (HttpResponseMessage naleznosci =
               await klient.GetAsync(new Uri("/Naleznosci", UriKind.Relative)))
        {
            string html = await AplikacjaTestowa.TrescAsync(naleznosci);
            Assert.Contains(numer, html, StringComparison.Ordinal);
            Assert.Contains("730,00", html, StringComparison.Ordinal);
        }

        using (HttpResponseMessage reszta = await WplacAsync(klient, id, 730m))
        {
            Assert.Equal(HttpStatusCode.Redirect, reszta.StatusCode);
        }

        // Zapłacona faktura znika z listy należności.
        using (HttpResponseMessage naleznosci =
               await klient.GetAsync(new Uri("/Naleznosci", UriKind.Relative)))
        {
            Assert.DoesNotContain(numer, await AplikacjaTestowa.TrescAsync(naleznosci),
                StringComparison.Ordinal);
        }

        // Przy fakturze widać stan i obie wpłaty.
        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{id}", UriKind.Relative));

        string tresc = await AplikacjaTestowa.TrescAsync(szczegoly);
        Assert.Contains("zapłacona", tresc, StringComparison.Ordinal);
        Assert.Contains("500,00", tresc, StringComparison.Ordinal);
        Assert.Contains("730,00", tresc, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsuniecieWplatyPrzywracaNaleznosc()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        (string id, string numer) = await WystawAsync(
            klient, "Usługa z cofniętą wpłatą", 200m,
            DateOnly.FromDateTime(DateTime.Today).AddDays(7));

        using (HttpResponseMessage wplata = await WplacAsync(klient, id, 246m))
        {
            Assert.Equal(HttpStatusCode.Redirect, wplata.StatusCode);
        }

        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{id}", UriKind.Relative));

        Match wplataId = WzorzecWplaty().Match(await AplikacjaTestowa.TrescAsync(szczegoly));
        Assert.True(wplataId.Success, "Nie znaleziono zapisanej wpłaty.");

        using (HttpResponseMessage usuniecie = await AplikacjaTestowa.WyslijFormularzAsync(
                   klient,
                   $"/Faktury/Szczegoly/{id}?handler=UsunWplate&wplataId={wplataId.Groups[1].Value}",
                   new Dictionary<string, string>(),
                   adresFormularza: $"/Faktury/Szczegoly/{id}"))
        {
            Assert.Equal(HttpStatusCode.Redirect, usuniecie.StatusCode);
        }

        using HttpResponseMessage naleznosci =
            await klient.GetAsync(new Uri("/Naleznosci", UriKind.Relative));

        Assert.Contains(numer, await AplikacjaTestowa.TrescAsync(naleznosci),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Wpłata z datą sprzed wystawienia faktury to prawie zawsze pomyłka.
    /// </summary>
    [Fact]
    public async Task WplataPrzedWystawieniemNiePrzechodzi()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        (string id, _) = await WystawAsync(
            klient, "Usługa z pomyloną datą", 100m,
            DateOnly.FromDateTime(DateTime.Today).AddDays(7));

        using HttpResponseMessage wplata = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Szczegoly/{id}?handler=Wplata",
            new Dictionary<string, string>
            {
                ["KwotaWplaty"] = "123",
                ["DataWplaty"] = DateTime.UtcNow.AddDays(-30)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            adresFormularza: $"/Faktury/Szczegoly/{id}");

        Assert.Equal(HttpStatusCode.OK, wplata.StatusCode);
        Assert.Contains("wcześniejsza niż wystawienie faktury",
            await AplikacjaTestowa.TrescAsync(wplata), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrzeterminowanaFakturaJestOznaczonaNaLiscie()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        (_, string numer) = await WystawAsync(
            klient, "Usługa po terminie", 100m,
            DateOnly.FromDateTime(DateTime.Today).AddDays(-10));

        using HttpResponseMessage naleznosci =
            await klient.GetAsync(new Uri("/Naleznosci", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(naleznosci);

        Assert.Contains(numer, html, StringComparison.Ordinal);
        Assert.Contains("po terminie o 10 dni", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrzypomnienieNiesieKwotePozostalaDoZaplaty()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();

        (string id, string numer) = await WystawAsync(
            klient, "Usługa z przypomnieniem", 1000m,
            DateOnly.FromDateTime(DateTime.Today).AddDays(-5));

        await WplacAsync(klient, id, 230m);

        using HttpResponseMessage przypomnienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Szczegoly/{id}?handler=Przypomnij",
            new Dictionary<string, string> { ["Adres"] = "dluznik@klient.example" },
            adresFormularza: $"/Faktury/Szczegoly/{id}");

        Assert.Equal(HttpStatusCode.Redirect, przypomnienie.StatusCode);

        AtrapaPoczty.Wiadomosc wiadomosc = Assert.Single(aplikacja.Poczta.Wyslane);

        Assert.Equal("dluznik@klient.example", wiadomosc.Adres);
        Assert.Contains(numer, wiadomosc.Temat, StringComparison.Ordinal);
        Assert.Contains("Przypomnienie", wiadomosc.Temat, StringComparison.Ordinal);
        Assert.Contains("termin minął 5 dni temu", wiadomosc.Tresc, StringComparison.Ordinal);

        // 1230 zł faktury minus 230 zł wpłaty daje 1000 zł do zapłaty.
        Assert.Contains("Pozostaje do zapłaty: 1 000,00 PLN", wiadomosc.Tresc,
            StringComparison.Ordinal);

        // Faktura leci w załączniku, żeby dłużnik nie musiał jej szukać.
        Assert.Single(wiadomosc.Zalaczniki);
    }

    /// <summary>Przypomnienie o zapłaconej fakturze byłoby wstydliwą pomyłką.</summary>
    [Fact]
    public async Task ZaplaconaFakturaNieDostajePrzypomnienia()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();

        (string id, _) = await WystawAsync(
            klient, "Usługa zapłacona w całości", 100m,
            DateOnly.FromDateTime(DateTime.Today).AddDays(-3));

        await WplacAsync(klient, id, 123m);

        using HttpResponseMessage przypomnienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Szczegoly/{id}?handler=Przypomnij",
            new Dictionary<string, string> { ["Adres"] = "zaplacil@klient.example" },
            adresFormularza: $"/Faktury/Szczegoly/{id}");

        Assert.Equal(HttpStatusCode.OK, przypomnienie.StatusCode);
        Assert.Empty(aplikacja.Poczta.Wyslane);
        Assert.Contains("już zapłacona", await AplikacjaTestowa.TrescAsync(przypomnienie),
            StringComparison.Ordinal);
    }

    [GeneratedRegex(@"<option value=""([0-9a-fA-F-]{36})""")]
    private static partial Regex WzorzecKontrahenta();

    [GeneratedRegex(@"<h1>Faktura ([^<]+)</h1>")]
    private static partial Regex WzorzecNumeru();

    // Wartości trasy trafiają do adresu przed nazwą obsługi zdarzenia.
    [GeneratedRegex(@"wplataId=([0-9a-fA-F-]+)&handler=UsunWplate")]
    private static partial Regex WzorzecWplaty();
}
