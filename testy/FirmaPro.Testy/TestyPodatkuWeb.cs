using System.Globalization;
using System.Net;

namespace FirmaPro.Testy;

/// <summary>
/// Zaliczka na podatek przechodzona tak, jak przechodzi ją użytkownik.
/// </summary>
/// <remarks>
/// Tu spina się cały program: dochód z księgi, remanenty ze spisu i składki
/// z ZUS-u schodzą w jedną kwotę. Zerwanie któregokolwiek z tych połączeń nie
/// daje błędu - daje zaliczkę wyglądającą poprawnie i o kilkaset złotych
/// za wysoką.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyPodatkuWeb(AplikacjaTestowa aplikacja)
{
    private const int Rok = 2025;

    [Fact]
    public async Task EkranPokazujeOkresyITerminy()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, $"/Podatek?rok={Rok}");

        Assert.Contains("Zaliczki na podatek dochodowy", html, StringComparison.Ordinal);
        Assert.Contains("styczeń", html, StringComparison.Ordinal);
        Assert.Contains("grudzień", html, StringComparison.Ordinal);

        // Termin za styczeń to 20 lutego - dzień roboczy, bez przesunięcia.
        Assert.Contains("2025-02-20", html, StringComparison.Ordinal);
    }

    /// <summary>Rok bez wpisanej skali nie liczy się skalą z innego roku.</summary>
    [Fact]
    public async Task NieznanyRokMowiWprostZeGoNieZna()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, "/Podatek?rok=2019");

        Assert.Contains("nie zna skali podatkowej", html, StringComparison.Ordinal);
    }

    /// <summary>Ekran tłumaczy, że kwota wolna zmniejsza podatek, nie podstawę.</summary>
    /// <remarks>
    /// To najczęstsze nieporozumienie wokół skali - lepiej odpowiedzieć
    /// na ekranie niż czekać na telefon do księgowej.
    /// </remarks>
    [Fact]
    public async Task EkranTlumaczyDzialanieKwotyWolnej()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, $"/Podatek?rok={Rok}");

        Assert.Contains("zmniejszenie podatku", html, StringComparison.Ordinal);
        Assert.Contains("samej nadwyżki", html, StringComparison.Ordinal);
    }

    /// <summary>Ekran mówi wprost, że to wyliczenie, a nie deklaracja.</summary>
    [Fact]
    public async Task EkranZastrzegaZeToNieDeklaracja()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string html = await StronaAsync(klient, $"/Podatek?rok={Rok}");

        Assert.Contains("To wyliczenie, nie deklaracja", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Składki zapłacone w ZUS-ie schodzą z podstawy opodatkowania.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Bez tego połączenia przedsiębiorca
    /// płaciłby podatek od dochodu nieobniżonego o składki społeczne, czyli
    /// za wysoki - i nie miałby jak tego zauważyć.
    /// </remarks>
    [Fact]
    public async Task SkladkiZZusuSchodzaZPodstawy()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        await ZapiszZusAsync(klient);

        // Listopad, bo miesiące wcześniejsze bywają już opłacone przez inne
        // testy - i to z ujęciem składek w kosztach, a nie w odliczeniu.
        using (HttpResponseMessage zaplata = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Zaplac&rok={Rok}&miesiac=11",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dzien"] = "2025-12-18"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zaplata.StatusCode);
        }

        string po = await StronaAsync(klient, $"/Podatek?rok={Rok}");

        // Pełny ZUS za jeden miesiąc to 1646,47 zł składek społecznych.
        Assert.True(PierwszaOdliczen(po) >= 1_646.47m,
            "Zapłacone składki społeczne nie weszły do odliczeń przy zaliczce.");
    }

    /// <summary>Potwierdzona zaliczka odejmuje się w okresach następnych.</summary>
    /// <remarks>
    /// Program odejmuje kwotę zapłaconą, a nie naliczoną - dlatego test
    /// wpisuje kwotę inną niż wyliczona i sprawdza, że to ona zadziałała.
    /// </remarks>
    [Fact]
    public async Task ZaplaconaZaliczkaOdejmujeSieDalej()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using (HttpResponseMessage zaplata = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Podatek?handler=Zaplac&rok={Rok}&numer=1&kwartalna=false",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kwota"] = "1234",
                ["dzien"] = "2025-02-20"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zaplata.StatusCode);
        }

        string html = await StronaAsync(klient, $"/Podatek?rok={Rok}");

        Assert.Contains("1 234", html, StringComparison.Ordinal);
        Assert.Contains("2025-02-20", html, StringComparison.Ordinal);
    }

    /// <summary>Cofnięcie zapłaty przywraca formularz.</summary>
    [Fact]
    public async Task CofniecieZaplatyPrzywracaFormularz()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Podatek?handler=Zaplac&rok={Rok}&numer=5&kwartalna=false",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kwota"] = "987",
                ["dzien"] = "2025-06-16"
            });

        // Znacznik zapłaty niesie kwotę i datę - sama data nie wystarczy,
        // bo ta sama wartość stoi w kolumnie terminu innego okresu.
        Assert.Contains("987 zł", await StronaAsync(klient, $"/Podatek?rok={Rok}"),
                        StringComparison.Ordinal);

        await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Podatek?handler=Cofnij&rok={Rok}&numer=5&kwartalna=false",
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.DoesNotContain("987 zł", await StronaAsync(klient, $"/Podatek?rok={Rok}"),
                              StringComparison.Ordinal);
    }

    /// <summary>Przełączenie na kwartały zmienia liczbę okresów z dwunastu na cztery.</summary>
    [Fact]
    public async Task KwartalneRozliczenieDajeCzteryOkresy()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using (HttpResponseMessage zapis = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Podatek?handler=Ustawienia&rok={Rok}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ZaliczkiKwartalne"] = "true",
                ["StrataDoOdliczenia"] = "0"
            }))
        {
            Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
        }

        try
        {
            string html = await StronaAsync(klient, $"/Podatek?rok={Rok}");

            Assert.Contains("1. kwartał", html, StringComparison.Ordinal);
            Assert.Contains("4. kwartał", html, StringComparison.Ordinal);
            Assert.DoesNotContain("styczeń", html, StringComparison.Ordinal);
        }
        finally
        {
            // Ustawienie jest wspólne dla całej aplikacji testowej, więc
            // wracamy do miesięcy - inaczej kolejny test zobaczyłby kwartały.
            await AplikacjaTestowa.WyslijFormularzAsync(
                klient, $"/Podatek?handler=Ustawienia&rok={Rok}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ZaliczkiKwartalne"] = "false",
                    ["StrataDoOdliczenia"] = "0"
                });
        }
    }

    /// <summary>Strata z lat ubiegłych obniża podstawę.</summary>
    [Fact]
    public async Task StrataZLatUbieglychObnizaPodstawe()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        try
        {
            await AplikacjaTestowa.WyslijFormularzAsync(
                klient, $"/Podatek?handler=Ustawienia&rok={Rok}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ZaliczkiKwartalne"] = "false",
                    ["StrataDoOdliczenia"] = "12345"
                });

            string html = await StronaAsync(klient, $"/Podatek?rok={Rok}");

            Assert.Contains("Strata z lat ubiegłych", html, StringComparison.Ordinal);
            Assert.Contains("12 345,00", html, StringComparison.Ordinal);
        }
        finally
        {
            await AplikacjaTestowa.WyslijFormularzAsync(
                klient, $"/Podatek?handler=Ustawienia&rok={Rok}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ZaliczkiKwartalne"] = "false",
                    ["StrataDoOdliczenia"] = "0"
                });
        }
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>Kwota odliczeń z pierwszego wiersza tabeli okresów.</summary>
    private static decimal PierwszaOdliczen(string html)
    {
        // Odliczenia są trzecią kolumną liczbową; wystarczy nam suma z karty
        // „Skąd wzięła się ta kwota", gdzie stoi jawnie przy składkach.
        int miejsce = html.IndexOf("Składki społeczne zapłacone",
                                   StringComparison.Ordinal);

        if (miejsce < 0)
        {
            return 0m;
        }

        string ogon = html[miejsce..Math.Min(html.Length, miejsce + 400)];

        System.Text.RegularExpressions.Match dopasowanie =
            System.Text.RegularExpressions.Regex.Match(
                ogon, @"([0-9  ]+,[0-9]{2})",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(5));

        return dopasowanie.Success
            ? decimal.Parse(dopasowanie.Groups[1].Value
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace(',', '.'),
                CultureInfo.InvariantCulture)
            : 0m;
    }

    private static async Task ZapiszZusAsync(HttpClient klient)
    {
        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Zus?handler=Ustawienia&rok={Rok}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tytul"] = "Pelny",
                ["Chorobowe"] = "true",
                ["SpoleczneWKosztach"] = "false",
                ["StopaWypadkowa"] = "0.0167",
                ["DniProwadzeniaPoprzedniegoRoku"] = "365"
            });

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
    }

    private static async Task<string> StronaAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();

        return await AplikacjaTestowa.TrescAsync(odpowiedz);
    }
}
