using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FirmaPro.Web.Uslugi;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Wysyłanie faktury kontrahentowi pocztą.
/// </summary>
/// <remarks>
/// Testy patrzą na to, co naprawdę poszłoby do klienta: adres, temat,
/// treść i załączniki. Sprawdzenie samego „przycisk zadziałał" niczego by
/// nie mówiło o tym, czy kontrahent dostanie fakturę, którą da się otworzyć.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed partial class TestyWysylkiPoczta(AplikacjaTestowa aplikacja)
{
    /// <summary>Wystawia fakturę i zwraca jej identyfikator.</summary>
    private static async Task<string> WystawFaktureAsync(HttpClient klient, string nazwa)
    {
        string idKontrahenta = await PierwszyKontrahentAsync(klient);
        string dzis = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = idKontrahenta,
                ["DataWystawienia"] = dzis,
                ["TerminPlatnosci"] = dzis,
                ["Pozycje[0].Nazwa"] = nazwa,
                ["Pozycje[0].Jednostka"] = "szt.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "300",
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        string adres = wystawienie.Headers.Location!.OriginalString;
        return adres["/Faktury/Szczegoly/".Length..];
    }

    private static async Task<string> PierwszyKontrahentAsync(HttpClient klient)
    {
        using HttpResponseMessage strona =
            await klient.GetAsync(new Uri("/Faktury/Nowa", UriKind.Relative));

        strona.EnsureSuccessStatusCode();

        Match dopasowanie = WzorzecKontrahenta().Match(await strona.Content.ReadAsStringAsync());
        Assert.True(dopasowanie.Success, "Brak kontrahentów do wystawienia faktury.");

        return dopasowanie.Groups[1].Value;
    }

    [Fact]
    public async Task FakturaIdzieDoKontrahentaZZalacznikiemPdf()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();

        string id = await WystawFaktureAsync(klient, "Usługa do wysyłki");

        using HttpResponseMessage wysylka = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Szczegoly/{id}?handler=Poczta",
            new Dictionary<string, string>
            {
                ["Adres"] = "ksiegowosc@klient.example",
                ["Wiadomosc"] = "Faktura za sierpień - dziękujemy za zamówienie.",
                ["DolaczXml"] = "false"
            },
            adresFormularza: $"/Faktury/Szczegoly/{id}");

        Assert.Equal(HttpStatusCode.Redirect, wysylka.StatusCode);

        AtrapaPoczty.Wiadomosc wiadomosc = Assert.Single(aplikacja.Poczta.Wyslane);

        Assert.Equal("ksiegowosc@klient.example", wiadomosc.Adres);
        Assert.Contains("Faktura", wiadomosc.Temat, StringComparison.Ordinal);
        Assert.Contains("Faktura za sierpień", wiadomosc.Tresc, StringComparison.Ordinal);

        // 300 zł netto przy stawce 23% daje 369 zł brutto. Kwota w wiadomości
        // zapisana jest po polsku - czyta ją kontrahent, nie program.
        Assert.Contains("369,00 PLN", wiadomosc.Tresc, StringComparison.Ordinal);

        Zalacznik zalacznik = Assert.Single(wiadomosc.Zalaczniki);
        Assert.EndsWith(".pdf", zalacznik.Nazwa, StringComparison.Ordinal);
        Assert.Equal("application/pdf", zalacznik.TypTresci);

        // Załącznik ma być prawdziwym plikiem PDF, a nie czymś, co się tak nazywa.
        Assert.Equal("%PDF-"u8.ToArray(), zalacznik.Dane[..5]);

        // Ukośniki z numeru faktury nie mogą trafić do nazwy pliku.
        Assert.DoesNotContain('/', zalacznik.Nazwa);
    }

    [Fact]
    public async Task NaZyczenieDolaczanyJestTakzePlikXml()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();

        string id = await WystawFaktureAsync(klient, "Usługa z XML-em");

        using HttpResponseMessage wysylka = await AplikacjaTestowa.WyslijParyAsync(
            klient, $"/Faktury/Szczegoly/{id}?handler=Poczta",
            [
                new("Adres", "biuro@klient.example"),
                new("DolaczXml", "true"),
                new("DolaczXml", "false")
            ],
            adresFormularza: $"/Faktury/Szczegoly/{id}");

        Assert.Equal(HttpStatusCode.Redirect, wysylka.StatusCode);

        AtrapaPoczty.Wiadomosc wiadomosc = Assert.Single(aplikacja.Poczta.Wyslane);
        Assert.Equal(2, wiadomosc.Zalaczniki.Count);

        Zalacznik xml = wiadomosc.Zalaczniki.Single(z => z.Nazwa.EndsWith(".xml", StringComparison.Ordinal));
        Assert.Contains("http://crd.gov.pl/wzor/2025/06/25/13775/",
            Encoding.UTF8.GetString(xml.Dane), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlednyAdresNieWysylaNiczego()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();

        string id = await WystawFaktureAsync(klient, "Usługa z błędnym adresem");

        using HttpResponseMessage wysylka = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Szczegoly/{id}?handler=Poczta",
            new Dictionary<string, string>
            {
                ["Adres"] = "to-nie-jest-adres",
                ["DolaczXml"] = "false"
            },
            adresFormularza: $"/Faktury/Szczegoly/{id}");

        Assert.Equal(HttpStatusCode.OK, wysylka.StatusCode);
        Assert.Empty(aplikacja.Poczta.Wyslane);
        Assert.Contains("adres e-mail", await AplikacjaTestowa.TrescAsync(wysylka),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Każda wysyłka zostawia ślad widoczny na ekranie faktury.
    /// </summary>
    /// <remarks>
    /// Przy sporze o to, czy kontrahent fakturę dostał, liczy się cała
    /// historia - także druga wysyłka pod poprawiony adres.
    /// </remarks>
    [Fact]
    public async Task HistoriaWysylekWidacPrzyFakturze()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();

        string id = await WystawFaktureAsync(klient, "Usługa wysyłana dwa razy");

        foreach (string adres in new[] { "pierwszy@klient.example", "drugi@klient.example" })
        {
            using HttpResponseMessage wysylka = await AplikacjaTestowa.WyslijFormularzAsync(
                klient, $"/Faktury/Szczegoly/{id}?handler=Poczta",
                new Dictionary<string, string> { ["Adres"] = adres, ["DolaczXml"] = "false" },
                adresFormularza: $"/Faktury/Szczegoly/{id}");

            Assert.Equal(HttpStatusCode.Redirect, wysylka.StatusCode);
        }

        using HttpResponseMessage strona =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{id}", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(strona);

        Assert.Contains("pierwszy@klient.example", html, StringComparison.Ordinal);
        Assert.Contains("drugi@klient.example", html, StringComparison.Ordinal);
        Assert.Equal(2, aplikacja.Poczta.Wyslane.Count);
    }

    /// <summary>
    /// Bez skonfigurowanej poczty ekran nie obiecuje wysyłki.
    /// </summary>
    /// <remarks>
    /// Komunikat „wysłano", po którym nic nie dociera, jest gorszy niż
    /// uczciwe wskazanie, że trzeba wysłać samemu.
    /// </remarks>
    [Fact]
    public async Task BezPocztyEkranMowiOTymWprost()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();
        aplikacja.Poczta.Dziala = false;

        try
        {
            string id = await WystawFaktureAsync(klient, "Usługa bez poczty");

            using HttpResponseMessage strona =
                await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{id}", UriKind.Relative));

            Assert.Contains("Poczta nie jest skonfigurowana",
                await AplikacjaTestowa.TrescAsync(strona), StringComparison.Ordinal);

            // Nawet gdy ktoś wyśle formularz mimo braku ekranu, nic nie leci.
            using HttpResponseMessage proba = await AplikacjaTestowa.WyslijFormularzAsync(
                klient, $"/Faktury/Szczegoly/{id}?handler=Poczta",
                new Dictionary<string, string>
                {
                    ["Adres"] = "ktos@klient.example",
                    ["DolaczXml"] = "false"
                },
                adresFormularza: $"/Faktury/Szczegoly/{id}");

            Assert.Equal(HttpStatusCode.OK, proba.StatusCode);
            Assert.Empty(aplikacja.Poczta.Wyslane);
        }
        finally
        {
            aplikacja.Poczta.Dziala = true;
        }
    }


    /// <summary>
    /// Niedostępny serwer poczty kończy się komunikatem, a nie stroną błędu.
    /// </summary>
    /// <remarks>
    /// Poczta bywa wyłączona albo odrzuca hasło. Użytkownik ma się o tym
    /// dowiedzieć i móc spróbować ponownie - a ślad wysyłki nie może zostać
    /// zapisany, bo nic nie poszło.
    /// </remarks>
    [Fact]
    public async Task AwariaSerweraPocztyNieWywracaStrony()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        aplikacja.Poczta.Wyslane.Clear();
        aplikacja.Poczta.Awaria = true;

        try
        {
            string id = await WystawFaktureAsync(klient, "Usługa przy awarii poczty");

            using HttpResponseMessage wysylka = await AplikacjaTestowa.WyslijFormularzAsync(
                klient, $"/Faktury/Szczegoly/{id}?handler=Poczta",
                new Dictionary<string, string>
                {
                    ["Adres"] = "ktos@klient.example",
                    ["DolaczXml"] = "false"
                },
                adresFormularza: $"/Faktury/Szczegoly/{id}");

            Assert.Equal(HttpStatusCode.OK, wysylka.StatusCode);

            string tresc = await AplikacjaTestowa.TrescAsync(wysylka);
            Assert.Contains("Serwer poczty nie odpowiada", tresc, StringComparison.Ordinal);

            // Nic nie poszło, więc nie ma też śladu w historii wysyłek.
            // Wpisany adres zostaje w formularzu - żeby dało się spróbować
            // ponownie bez przepisywania go od nowa.
            Assert.Empty(aplikacja.Poczta.Wyslane);
            Assert.DoesNotContain("<th>Wysłano</th>", tresc, StringComparison.Ordinal);
            Assert.Contains("value=\"ktos@klient.example\"", tresc, StringComparison.Ordinal);
        }
        finally
        {
            aplikacja.Poczta.Awaria = false;
        }
    }

    // Pierwszy kontrahent z listy wyboru na formularzu faktury.
    [GeneratedRegex(@"<option value=""([0-9a-fA-F-]{36})""")]
    private static partial Regex WzorzecKontrahenta();
}
