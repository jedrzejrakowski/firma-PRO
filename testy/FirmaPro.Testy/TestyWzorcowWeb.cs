using System.Globalization;
using System.Net;

namespace FirmaPro.Testy;

/// <summary>
/// Faktury cykliczne przechodzone tak, jak przechodzi je użytkownik.
/// </summary>
/// <remarks>
/// Najważniejsze jest tu jedno: program <b>nie wystawia faktur sam</b>.
/// Pokazuje, co czeka, a dokument powstaje po kliknięciu. Testy pilnują
/// obu stron tej umowy - że przypomni, i że nie wystawi bez pytania.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyWzorcowWeb(AplikacjaTestowa aplikacja)
{
    [Fact]
    public async Task WzorzecZakladaSieIWystawiaFakture()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string kontrahent = await PierwszyKontrahentAsync(klient);
        DateOnly dzisiaj = DateOnly.FromDateTime(DateTime.UtcNow);

        using HttpResponseMessage zalozenie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Cykliczne/Wzorzec", new Dictionary<string, string>
            {
                ["Nazwa"] = "Abonament testowy",
                ["KontrahentId"] = kontrahent,
                ["Rytm"] = "Miesiecznie",
                // Dzień dzisiejszy, żeby faktura wypadła od razu.
                ["DzienMiesiaca"] = Math.Min(dzisiaj.Day, 28).ToString(
                    CultureInfo.InvariantCulture),
                ["TerminPlatnosciDni"] = "14",
                ["FormaPlatnosci"] = "6",
                ["Od"] = dzisiaj.AddMonths(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Aktywny"] = "true",
                ["Pozycje[0].Nazwa"] = "Stała obsługa informatyczna",
                ["Pozycje[0].Jednostka"] = "mies.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "800",
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, zalozenie.StatusCode);

        // Wzorzec czeka na wystawienie, ale faktury jeszcze nie ma.
        string lista = await StronaAsync(klient, "/Cykliczne");
        Assert.Contains("Czeka na wystawienie", lista, StringComparison.Ordinal);
        Assert.Contains("Abonament testowy", lista, StringComparison.Ordinal);

        Guid id = IdentyfikatorWzorca(lista);

        using HttpResponseMessage wystawienie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, $"/Cykliczne?handler=Wystaw&id={id}",
            new Dictionary<string, string>(),
            adresFormularza: "/Cykliczne");

        Assert.Equal(HttpStatusCode.Redirect, wystawienie.StatusCode);

        string po = await StronaAsync(klient, "/Cykliczne");
        Assert.Contains("Wystawiono fakturę", po, StringComparison.Ordinal);

        // Faktura naprawdę powstała i ma treść z wzorca. Szukamy jej po
        // numerze z komunikatu, a nie „ostatniej na liście" - w bazie
        // testowej leżą faktury z innych testów.
        string numer = NumerZKomunikatu(po);
        string szczegoly = await SzczegolyFakturyAsync(klient, numer);

        Assert.Contains("Stała obsługa informatyczna", szczegoly, StringComparison.Ordinal);
        Assert.Contains("mies.", szczegoly, StringComparison.Ordinal);
    }

    /// <summary>
    /// Wzorzec z datą w przyszłości nie wystawia niczego przed terminem.
    /// </summary>
    [Fact]
    public async Task WzorzecPrzedTerminemNieWystawiaFaktury()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        string kontrahent = await PierwszyKontrahentAsync(klient);
        DateOnly zaMiesiac = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);

        using HttpResponseMessage zalozenie = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Cykliczne/Wzorzec", new Dictionary<string, string>
            {
                ["Nazwa"] = "Wzorzec na przyszłość",
                ["KontrahentId"] = kontrahent,
                ["Rytm"] = "Miesiecznie",
                ["DzienMiesiaca"] = Math.Min(zaMiesiac.Day, 28).ToString(
                    CultureInfo.InvariantCulture),
                ["TerminPlatnosciDni"] = "7",
                ["FormaPlatnosci"] = "6",
                ["Od"] = zaMiesiac.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Aktywny"] = "true",
                ["Pozycje[0].Nazwa"] = "Usługa przyszła",
                ["Pozycje[0].Jednostka"] = "usł.",
                ["Pozycje[0].Ilosc"] = "1",
                ["Pozycje[0].CenaNetto"] = "100",
                ["Pozycje[0].KodStawki"] = "23"
            });

        Assert.Equal(HttpStatusCode.Redirect, zalozenie.StatusCode);

        string lista = await StronaAsync(klient, "/Cykliczne");

        Assert.Contains("Wzorzec na przyszłość", lista, StringComparison.Ordinal);
        Assert.DoesNotContain("Usługa przyszła",
            await StronaAsync(klient, "/Faktury"), StringComparison.Ordinal);
    }

    /// <summary>Wzorzec bez pozycji nie ma czego wystawiać.</summary>
    [Fact]
    public async Task WzorzecBezPozycjiNiePrzechodzi()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Cykliczne/Wzorzec", new Dictionary<string, string>
            {
                ["Nazwa"] = "Pusty wzorzec",
                ["KontrahentId"] = await PierwszyKontrahentAsync(klient),
                ["Rytm"] = "Miesiecznie",
                ["DzienMiesiaca"] = "1",
                ["TerminPlatnosciDni"] = "14",
                ["FormaPlatnosci"] = "6",
                ["Od"] = "2026-08-01",
                ["Aktywny"] = "true"
            });

        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);
        Assert.Contains("przynajmniej jedną pozycję",
            await AplikacjaTestowa.TrescAsync(odpowiedz), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ pomocnicze

    private static async Task<string> StronaAsync(HttpClient klient, string adres)
    {
        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri(adres, UriKind.Relative));

        odpowiedz.EnsureSuccessStatusCode();
        return await AplikacjaTestowa.TrescAsync(odpowiedz);
    }

    /// <summary>
    /// Wyjmuje numer wzorca z odnośnika „Popraw".
    /// </summary>
    /// <remarks>
    /// Adres odnośnika jest jedynym miejscem, w którym numer wzorca pojawia
    /// się na stronie w niezmienionej postaci - kolejność parametrów
    /// w adresie formularza zależy od generatora odnośników.
    /// </remarks>
    private static Guid IdentyfikatorWzorca(string html)
    {
        const string wzor = "/Cykliczne/Wzorzec?id=";
        int poczatek = html.IndexOf(wzor, StringComparison.Ordinal);

        Assert.True(poczatek > 0, "Na liście nie ma odnośnika do wzorca.");

        return Guid.Parse(html.AsSpan(poczatek + wzor.Length, 36));
    }

    /// <summary>Wyjmuje numer faktury z komunikatu o wystawieniu.</summary>
    private static string NumerZKomunikatu(string html)
    {
        const string wzor = "Wystawiono fakturę ";
        int poczatek = html.IndexOf(wzor, StringComparison.Ordinal) + wzor.Length;

        Assert.True(poczatek > wzor.Length, "Brak komunikatu o wystawieniu faktury.");

        int koniec = html.IndexOf(' ', poczatek);
        return html[poczatek..koniec];
    }

    /// <summary>Otwiera szczegóły faktury o wskazanym numerze.</summary>
    private static async Task<string> SzczegolyFakturyAsync(HttpClient klient, string numer)
    {
        string wyniki = await StronaAsync(klient,
            "/Szukaj?q=" + Uri.EscapeDataString(numer));

        const string wzor = "/Faktury/Szczegoly/";
        int poczatek = wyniki.IndexOf(wzor, StringComparison.Ordinal);

        Assert.True(poczatek > 0, $"Szukanie nie znalazło faktury {numer}.");

        return await StronaAsync(klient,
            wyniki[poczatek..(poczatek + wzor.Length + 36)]);
    }

    private static async Task<string> PierwszyKontrahentAsync(HttpClient klient)
    {
        using HttpResponseMessage formularz =
            await klient.GetAsync(new Uri("/Cykliczne/Wzorzec", UriKind.Relative));

        string html = await formularz.Content.ReadAsStringAsync();

        int poczatek = html.IndexOf("id=\"nabywca\"", StringComparison.Ordinal);
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
}
