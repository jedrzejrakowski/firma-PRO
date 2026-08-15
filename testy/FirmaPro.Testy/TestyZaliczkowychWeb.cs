using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Faktury zaliczkowe, końcowe, korekty korekt i duplikaty - drogą przeglądarki.
/// </summary>
/// <remarks>
/// Zgodność plików FA(3) sprawdza osobny zestaw testów, oficjalnym schematem
/// XSD. Tutaj chodzi o to, czy da się przejść całą drogę: wystawić zaliczkę,
/// rozliczyć ją fakturą końcową i nie rozliczyć jej drugi raz.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed partial class TestyZaliczkowychWeb(AplikacjaTestowa aplikacja)
{
    private static string Dzis =>
        DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<string> KontrahentAsync(HttpClient klient)
    {
        using HttpResponseMessage strona =
            await klient.GetAsync(new Uri("/Faktury/Nowa", UriKind.Relative));

        strona.EnsureSuccessStatusCode();

        Match dopasowanie = WzorzecKontrahenta().Match(await strona.Content.ReadAsStringAsync());
        Assert.True(dopasowanie.Success, "Brak kontrahentów.");

        return dopasowanie.Groups[1].Value;
    }

    /// <summary>Wystawia fakturę zaliczkową i zwraca jej identyfikator.</summary>
    private static async Task<string> WystawZaliczkoweAsync(
        HttpClient klient, string kontrahentId, decimal zaliczka, decimal cenaZamowienia)
    {
        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Zaliczkowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = kontrahentId,
                ["DataWystawienia"] = Dzis,
                ["KwotaZaliczki"] = zaliczka.ToString(CultureInfo.InvariantCulture),
                ["FormaPlatnosci"] = "6",
                ["Zamowienie[0].Nazwa"] = "Wykonanie instalacji",
                ["Zamowienie[0].Jednostka"] = "usł.",
                ["Zamowienie[0].Ilosc"] = "1",
                ["Zamowienie[0].CenaNetto"] = cenaZamowienia.ToString(CultureInfo.InvariantCulture),
                ["Zamowienie[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        return odpowiedz.Headers.Location!.OriginalString["/Faktury/Szczegoly/".Length..];
    }

    [Fact]
    public async Task ZaliczkaRozbijaSieNaStawkiIJestOznaczonaJakoZaplacona()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        // Zamówienie 10 000 zł netto (12 300 brutto), zaliczka 1230 zł brutto.
        string id = await WystawZaliczkoweAsync(klient, await KontrahentAsync(klient), 1230m, 10000m);

        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{id}", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(szczegoly);

        // 1230 brutto przy 23% to 1000 netto i 230 podatku.
        Assert.Contains("1 000,00", html, StringComparison.Ordinal);
        Assert.Contains("230,00", html, StringComparison.Ordinal);

        // Zaliczka jest z definicji zapłacona, więc nie ma jej w należnościach.
        using HttpResponseMessage naleznosci =
            await klient.GetAsync(new Uri("/Naleznosci", UriKind.Relative));

        Assert.DoesNotContain("Zaliczka na poczet",
            await AplikacjaTestowa.TrescAsync(naleznosci), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZaliczkaWyzszaOdZamowieniaNiePrzechodzi()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Zaliczkowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = await KontrahentAsync(klient),
                ["DataWystawienia"] = Dzis,
                ["KwotaZaliczki"] = "99999",
                ["FormaPlatnosci"] = "6",
                ["Zamowienie[0].Nazwa"] = "Drobiazg",
                ["Zamowienie[0].Jednostka"] = "szt.",
                ["Zamowienie[0].Ilosc"] = "1",
                ["Zamowienie[0].CenaNetto"] = "100",
                ["Zamowienie[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);
        Assert.Contains("wyższa niż wartość zamówienia",
            await AplikacjaTestowa.TrescAsync(odpowiedz), StringComparison.Ordinal);
    }

    /// <summary>
    /// Faktura końcowa rozlicza zaliczkę - i tylko raz.
    /// </summary>
    /// <remarks>
    /// Drugie rozliczenie tej samej zaliczki usunęłoby tę samą kwotę
    /// z podstawy opodatkowania dwukrotnie.
    /// </remarks>
    [Fact]
    public async Task ZaliczkeMoznaRozliczycTylkoRaz()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        string kontrahentId = await KontrahentAsync(klient);

        string zaliczkowa = await WystawZaliczkoweAsync(klient, kontrahentId, 1230m, 10000m);

        Dictionary<string, string> koncowa = new()
        {
            ["KontrahentId"] = kontrahentId,
            ["DataWystawienia"] = Dzis,
            ["TerminPlatnosci"] = Dzis,
            ["FormaPlatnosci"] = "6",
            ["Zaliczki"] = zaliczkowa,
            ["Pozycje[0].Nazwa"] = "Wykonanie instalacji",
            ["Pozycje[0].Jednostka"] = "usł.",
            ["Pozycje[0].Ilosc"] = "1",
            ["Pozycje[0].CenaNetto"] = "10000",
            ["Pozycje[0].KodStawki"] = "23"
        };

        using (HttpResponseMessage pierwsza = await AplikacjaTestowa.WyslijFormularzAsync(
                   klient, $"/Faktury/Koncowa/{zaliczkowa}", koncowa,
                   adresFormularza: $"/Faktury/Koncowa/{zaliczkowa}"))
        {
            Assert.Equal(HttpStatusCode.Redirect, pierwsza.StatusCode);
        }

        using HttpResponseMessage druga = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Koncowa/{zaliczkowa}", koncowa,
            adresFormularza: $"/Faktury/Koncowa/{zaliczkowa}");

        Assert.Equal(HttpStatusCode.OK, druga.StatusCode);
        Assert.Contains("już rozliczone fakturą końcową",
            await AplikacjaTestowa.TrescAsync(druga), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakturaKoncowaWskazujeZaliczkeWPlikuXml()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        string kontrahentId = await KontrahentAsync(klient);

        string zaliczkowa = await WystawZaliczkoweAsync(klient, kontrahentId, 615m, 5000m);

        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Koncowa/{zaliczkowa}", new Dictionary<string, string>
            {
                ["KontrahentId"] = kontrahentId,
                ["DataWystawienia"] = Dzis,
                ["TerminPlatnosci"] = Dzis,
                ["FormaPlatnosci"] = "6",
                ["Zaliczki"] = zaliczkowa,
                ["Pozycje[0].Nazwa"] = "Wykonanie instalacji",
                ["Pozycje[0].Jednostka"] = "usł.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "5000",
                ["Pozycje[0].KodStawki"] = "23"
            },
            adresFormularza: $"/Faktury/Koncowa/{zaliczkowa}");

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        string koncowa = wystawienie.Headers.Location!.OriginalString["/Faktury/Szczegoly/".Length..];

        using HttpResponseMessage plik = await klient.GetAsync(
            new Uri($"/Faktury/Szczegoly/{koncowa}?handler=Xml", UriKind.Relative));

        plik.EnsureSuccessStatusCode();
        string xml = await plik.Content.ReadAsStringAsync();

        Assert.Contains("<RodzajFaktury>ROZ</RodzajFaktury>", xml, StringComparison.Ordinal);
        Assert.Contains("<FakturaZaliczkowa>", xml, StringComparison.Ordinal);

        // Zaliczka nie poszła do KSeF, więc wskazujemy ją własnym numerem.
        Assert.Contains("<NrKSeFZN>1</NrKSeFZN>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZaliczkowaNiesieZamowienieWPlikuXml()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string id = await WystawZaliczkoweAsync(klient, await KontrahentAsync(klient), 1230m, 10000m);

        using HttpResponseMessage plik = await klient.GetAsync(
            new Uri($"/Faktury/Szczegoly/{id}?handler=Xml", UriKind.Relative));

        plik.EnsureSuccessStatusCode();
        string xml = await plik.Content.ReadAsStringAsync();

        Assert.Contains("<RodzajFaktury>ZAL</RodzajFaktury>", xml, StringComparison.Ordinal);
        Assert.Contains("<WartoscZamowienia>12300.00</WartoscZamowienia>", xml,
            StringComparison.Ordinal);
        Assert.Contains("<P_7Z>Wykonanie instalacji</P_7Z>", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Korekty numerowane są osobną serią, a korektę można wystawić do korekty.
    /// </summary>
    [Fact]
    public async Task KorektaMaWlasnaSerieIMoznaJaSkorygowac()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        string kontrahentId = await KontrahentAsync(klient);

        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Nowa", new Dictionary<string, string>
            {
                ["KontrahentId"] = kontrahentId,
                ["DataWystawienia"] = Dzis,
                ["Pozycje[0].Nazwa"] = "Usługa do dwóch korekt",
                ["Pozycje[0].Jednostka"] = "szt.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "1000",
                ["Pozycje[0].KodStawki"] = "23"
            });

        string pierwotna = wystawienie.Headers.Location!.OriginalString["/Faktury/Szczegoly/".Length..];

        string pierwszaKorekta = await SkorygujAsync(klient, pierwotna, "Rabat potransakcyjny", "900");

        using (HttpResponseMessage szczegoly =
               await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{pierwszaKorekta}", UriKind.Relative)))
        {
            // Numer korekty pochodzi z osobnej serii, a nie z ciągu sprzedaży.
            Assert.Contains("KOR/", await AplikacjaTestowa.TrescAsync(szczegoly),
                StringComparison.Ordinal);
        }

        // Druga poprawka tej samej faktury to korekta do korekty.
        string drugaKorekta = await SkorygujAsync(klient, pierwszaKorekta, "Kolejna pomyłka", "850");

        Assert.NotEqual(pierwszaKorekta, drugaKorekta);
    }

    private static async Task<string> SkorygujAsync(
        HttpClient klient, string id, string przyczyna, string nowaCena)
    {
        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Faktury/Korekta", new Dictionary<string, string>
            {
                ["KorygowanaId"] = id,
                ["DataWystawienia"] = Dzis,
                ["PrzyczynaKorekty"] = przyczyna,
                ["TypKorekty"] = "WDacieKorekty",
                ["Pozycje[0].Nazwa"] = "Usługa do dwóch korekt",
                ["Pozycje[0].Jednostka"] = "szt.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = nowaCena,
                ["Pozycje[0].KodStawki"] = "23"
            },
            adresFormularza: $"/Faktury/Korekta?id={id}");

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        return odpowiedz.Headers.Location!.OriginalString["/Faktury/Szczegoly/".Length..];
    }

    /// <summary>
    /// Zaliczka ma zapisaną wpłatę, a nie sam znacznik „zapłacona".
    /// </summary>
    /// <remarks>
    /// Ekran zapłaty liczy z wpłat. Bez zapisanej wpłaty faktura zaliczkowa
    /// pokazywała „niezapłacona" mimo znacznika mówiącego coś przeciwnego.
    /// </remarks>
    [Fact]
    public async Task ZaliczkaMaZapisanaWplate()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string id = await WystawZaliczkoweAsync(klient, await KontrahentAsync(klient), 1230m, 10000m);

        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{id}", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(szczegoly);

        Assert.Contains("Otrzymana zaliczka", html, StringComparison.Ordinal);
        Assert.Contains("zapłacona", html, StringComparison.Ordinal);
        Assert.DoesNotContain("niezapłacona", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Faktura końcowa nie żąda drugi raz pieniędzy wpłaconych zaliczką.
    /// </summary>
    [Fact]
    public async Task FakturaKoncowaOdliczaZaliczkeOdKwotyDoZaplaty()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        string kontrahentId = await KontrahentAsync(klient);

        // Zamówienie 10 000 netto (12 300 brutto), zaliczka 2460 brutto.
        string zaliczkowa = await WystawZaliczkoweAsync(klient, kontrahentId, 2460m, 10000m);
        string koncowa = await WystawKoncowaAsync(klient, kontrahentId, zaliczkowa, "10000");

        using HttpResponseMessage szczegoly =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{koncowa}", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(szczegoly);
        string numer = WzorzecNumeru().Match(html).Groups[1].Value;

        Assert.Contains("Rozliczone zaliczki", html, StringComparison.Ordinal);

        // 12 300 wartości dostawy minus 2460 zaliczki to 9840 do dopłaty.
        Assert.Contains("9 840,00", html, StringComparison.Ordinal);

        using HttpResponseMessage naleznosci =
            await klient.GetAsync(new Uri("/Naleznosci", UriKind.Relative));

        // Kwota faktury, wpłacono, pozostaje - należność to sama różnica.
        Assert.Equal(
            ["12 300,00", "2 460,00", "9 840,00"],
            KwotyWWierszu(await AplikacjaTestowa.TrescAsync(naleznosci), numer));
    }

    /// <summary>
    /// Ta sama sprzedaż nie wchodzi do rejestru VAT dwa razy.
    /// </summary>
    /// <remarks>
    /// Podatek od zaliczki wykazano w miesiącu jej otrzymania. Gdyby faktura
    /// końcowa weszła całą wartością dostawy, firma zapłaciłaby VAT podwójnie
    /// od zaliczkowej części.
    /// </remarks>
    [Fact]
    public async Task ZaliczkaNieWchodziDoRejestruDwaRazy()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();
        string kontrahentId = await KontrahentAsync(klient);

        string zaliczkowa = await WystawZaliczkoweAsync(klient, kontrahentId, 2460m, 20000m);
        string koncowa = await WystawKoncowaAsync(klient, kontrahentId, zaliczkowa, "20000");

        using HttpResponseMessage szczegolyKoncowej =
            await klient.GetAsync(new Uri($"/Faktury/Szczegoly/{koncowa}", UriKind.Relative));

        string numerKoncowej = WzorzecNumeru()
            .Match(await AplikacjaTestowa.TrescAsync(szczegolyKoncowej)).Groups[1].Value;

        DateOnly dzis = DateOnly.FromDateTime(DateTime.UtcNow);

        using HttpResponseMessage rejestr = await klient.GetAsync(new Uri(
            $"/Rejestry/Vat?rok={dzis.Year}&miesiac={dzis.Month}", UriKind.Relative));

        rejestr.EnsureSuccessStatusCode();
        string html = await AplikacjaTestowa.TrescAsync(rejestr);

        // Zaliczka wykazana osobno: 2000 netto, 460 podatku, 2460 brutto.
        Assert.Equal(
            ["2 000,00", "460,00", "2 460,00"],
            KwotyWWierszu(html, WzorZaliczkowej(numerKoncowej)));

        // Dostawa 20 000 netto minus zafakturowane 2000 to 18 000 w końcowej.
        Assert.Equal(
            ["18 000,00", "4 140,00", "22 140,00"],
            KwotyWWierszu(html, numerKoncowej));
    }

    /// <summary>
    /// Wyjmuje kwoty z wiersza tabeli opisującego wskazaną fakturę.
    /// </summary>
    /// <remarks>
    /// Porównanie całych komórek zamiast szukania liczby gdziekolwiek na
    /// stronie: inaczej test przechodziłby, gdy właściwa kwota trafi
    /// do sąsiedniego wiersza.
    /// </remarks>
    private static string[] KwotyWWierszu(string html, string numer)
    {
        Match wiersz = Regex.Match(
            html, @"<tr[^>]*>(?:(?!</tr>).)*?" + Regex.Escape(numer) + @"<(?:(?!</tr>).)*?</tr>",
            RegexOptions.Singleline, TimeSpan.FromSeconds(5));

        Assert.True(wiersz.Success, $"Strona nie zawiera wiersza faktury {numer}.");

        return [.. Regex
            .Matches(wiersz.Value, @"<td class=""liczba"">(.*?)</td>",
                     RegexOptions.Singleline, TimeSpan.FromSeconds(5))
            .Select(d => Regex.Replace(d.Groups[1].Value, "<[^>]+>", string.Empty,
                                       RegexOptions.None, TimeSpan.FromSeconds(5)).Trim())];
    }

    /// <summary>Numer zaliczkowej to numer o jeden mniejszy od końcowej.</summary>
    private static string WzorZaliczkowej(string numerKoncowej)
    {
        int ukosnik = numerKoncowej.LastIndexOf('/');
        int kolejny = int.Parse(numerKoncowej[(ukosnik + 1)..], CultureInfo.InvariantCulture);

        return numerKoncowej[..(ukosnik + 1)] + (kolejny - 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Wystawia fakturę końcową do wskazanej zaliczki.</summary>
    private static async Task<string> WystawKoncowaAsync(
        HttpClient klient, string kontrahentId, string zaliczkowa, string cenaDostawy)
    {
        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Faktury/Koncowa/{zaliczkowa}", new Dictionary<string, string>
            {
                ["KontrahentId"] = kontrahentId,
                ["DataWystawienia"] = Dzis,
                ["TerminPlatnosci"] = Dzis,
                ["FormaPlatnosci"] = "6",
                ["Zaliczki"] = zaliczkowa,
                ["Pozycje[0].Nazwa"] = "Wykonanie instalacji",
                ["Pozycje[0].Jednostka"] = "usł.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = cenaDostawy,
                ["Pozycje[0].KodStawki"] = "23"
            },
            adresFormularza: $"/Faktury/Koncowa/{zaliczkowa}");

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        return odpowiedz.Headers.Location!.OriginalString["/Faktury/Szczegoly/".Length..];
    }

    [Fact]
    public async Task DuplikatJestOznaczonyNaWydruku()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string id = await WystawZaliczkoweAsync(klient, await KontrahentAsync(klient), 123m, 1000m);

        using HttpResponseMessage duplikat = await klient.GetAsync(
            new Uri($"/Faktury/Szczegoly/{id}?handler=Pdf&duplikat=true", UriKind.Relative));

        duplikat.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", duplikat.Content.Headers.ContentType?.MediaType);

        string nazwa = duplikat.Content.Headers.ContentDisposition?.FileNameStar
                       ?? duplikat.Content.Headers.ContentDisposition?.FileName
                       ?? string.Empty;

        Assert.Contains("duplikat", nazwa, StringComparison.OrdinalIgnoreCase);

        byte[] pdf = await duplikat.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
    }

    [GeneratedRegex(@"<option value=""([0-9a-fA-F-]{36})""")]
    private static partial Regex WzorzecKontrahenta();

    [GeneratedRegex(@"<h1>Faktura ([^<]+)</h1>")]
    private static partial Regex WzorzecNumeru();
}
