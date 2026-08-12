using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Role sprawdzane tak, jak sprawdza je przeglądarka.
/// </summary>
/// <remarks>
/// Ukrycie przycisku nie jest zabezpieczeniem, więc testy nie patrzą na to,
/// co widać, tylko wysyłają żądania wprost pod adresy - dokładnie tak, jak
/// zrobiłby ktoś, kto zna adres i chce go użyć mimo braku uprawnień.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed partial class TestyRolWebowych(AplikacjaTestowa aplikacja)
{
    private const string Haslo = "bardzodlugiehaslo";

    /// <summary>Zakłada świeżą firmę i zwraca zalogowanego właściciela.</summary>
    private async Task<HttpClient> ZalozFirmeAsync(string znacznik)
    {
        HttpClient klient = aplikacja.UtworzKlienta();

        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Rejestracja", new Dictionary<string, string>
            {
                ["Email"] = $"wlasciciel-{znacznik}@example.pl",
                ["Haslo"] = Haslo,
                ["PowtorzHaslo"] = Haslo,
                ["NazwaFirmy"] = $"Firma {znacznik}",
                ["Nip"] = "5252248481"
            });

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        Assert.Equal("/Ustawienia", odpowiedz.Headers.Location?.OriginalString);

        return klient;
    }

    /// <summary>Zaprasza kogoś do firmy i przyjmuje zaproszenie nowym kontem.</summary>
    private static async Task<HttpClient> DolaczAsync(
        AplikacjaTestowa aplikacja, HttpClient wlasciciel, string email, string rola)
    {
        using HttpResponseMessage zaproszenie = await AplikacjaTestowa.WyslijFormularzAsync(
            wlasciciel, "/Uzytkownicy?handler=Zaprosc",
            new Dictionary<string, string> { ["Email"] = email, ["Rola"] = rola },
            adresFormularza: "/Uzytkownicy");

        zaproszenie.EnsureSuccessStatusCode();

        string html = await AplikacjaTestowa.TrescAsync(zaproszenie);
        Match odnosnik = WzorzecOdnosnika().Match(html);
        Assert.True(odnosnik.Success, "Strona nie pokazała odnośnika do zaproszenia.");

        string adres = odnosnik.Groups[1].Value;

        HttpClient zapraszany = aplikacja.UtworzKlienta();
        using HttpResponseMessage przyjecie = await AplikacjaTestowa.WyslijFormularzAsync(
            zapraszany, adres,
            new Dictionary<string, string>
            {
                ["Kod"] = adres.Split("kod=")[^1],
                ["ImieINazwisko"] = email,
                ["Haslo"] = Haslo
            },
            adresFormularza: adres);

        Assert.Equal(HttpStatusCode.Redirect, przyjecie.StatusCode);
        Assert.Equal("/Faktury", przyjecie.Headers.Location?.OriginalString);

        return zapraszany;
    }

    [Fact]
    public async Task WlascicielWchodziDoUstawienIDostepu()
    {
        using HttpClient wlasciciel = await ZalozFirmeAsync("wlasc");

        foreach (string adres in new[] { "/Ustawienia", "/Uzytkownicy" })
        {
            using HttpResponseMessage odpowiedz =
                await wlasciciel.GetAsync(new Uri(adres, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);
        }
    }

    /// <summary>
    /// Księgowy prowadzi księgi, ale nie rozdaje dostępu ani nie zmienia
    /// ustawień firmy - w tym tokena KSeF.
    /// </summary>
    [Fact]
    public async Task KsiegowyNieWchodziDoUstawienAniDoDostepu()
    {
        using HttpClient wlasciciel = await ZalozFirmeAsync("ksieg");
        using HttpClient ksiegowy = await DolaczAsync(
            aplikacja, wlasciciel, "ksiegowa-ksieg@example.pl", "Ksiegowy");

        foreach (string adres in new[] { "/Ustawienia", "/Uzytkownicy" })
        {
            using HttpResponseMessage odpowiedz =
                await ksiegowy.GetAsync(new Uri(adres, UriKind.Relative));

            Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
            Assert.Contains("/BrakUprawnien", odpowiedz.Headers.Location!.OriginalString,
                StringComparison.Ordinal);
        }

        // Kartoteki i faktury pozostają dla niego otwarte.
        using HttpResponseMessage kontrahenci =
            await ksiegowy.GetAsync(new Uri("/Kontrahenci/Nowy", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, kontrahenci.StatusCode);
    }

    [Fact]
    public async Task KsiegowyMozeDodacKontrahenta()
    {
        using HttpClient wlasciciel = await ZalozFirmeAsync("kdod");
        using HttpClient ksiegowy = await DolaczAsync(
            aplikacja, wlasciciel, "ksiegowa-kdod@example.pl", "Ksiegowy");

        using HttpResponseMessage zapis = await AplikacjaTestowa.WyslijFormularzAsync(
            ksiegowy, "/Kontrahenci/Nowy", new Dictionary<string, string>
            {
                ["Nazwa"] = "Klient księgowego",
                ["Nip"] = "7010001453",
                ["AdresLinia1"] = "ul. Długa 1",
                ["AdresLinia2"] = "80-827 Gdańsk"
            });

        Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
    }

    /// <summary>
    /// Rola podglądu nie zmienia niczego, nawet gdy ktoś zna adres formularza.
    /// </summary>
    [Fact]
    public async Task PodgladCzytaAleNieZapisuje()
    {
        using HttpClient wlasciciel = await ZalozFirmeAsync("podgl");
        using HttpClient podglad = await DolaczAsync(
            aplikacja, wlasciciel, "podglad-podgl@example.pl", "Podglad");

        using HttpResponseMessage lista =
            await podglad.GetAsync(new Uri("/Faktury", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);

        using HttpResponseMessage zapis = await AplikacjaTestowa.WyslijFormularzAsync(
            podglad, "/Kontrahenci/Nowy", new Dictionary<string, string>
            {
                ["Nazwa"] = "Kontrahent spod podglądu",
                ["Nip"] = "7010001453",
                ["AdresLinia1"] = "ul. Długa 1",
                ["AdresLinia2"] = "80-827 Gdańsk"
            });

        Assert.Equal(HttpStatusCode.Redirect, zapis.StatusCode);
        Assert.Contains("/BrakUprawnien", zapis.Headers.Location!.OriginalString,
            StringComparison.Ordinal);

        // Zapisu naprawdę nie było - nie tylko przekierowania.
        using HttpResponseMessage kartoteka =
            await wlasciciel.GetAsync(new Uri("/Kontrahenci", UriKind.Relative));

        Assert.DoesNotContain("Kontrahent spod podglądu",
            await AplikacjaTestowa.TrescAsync(kartoteka), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PodgladMozeSieWylogowac()
    {
        using HttpClient wlasciciel = await ZalozFirmeAsync("wylog");
        using HttpClient podglad = await DolaczAsync(
            aplikacja, wlasciciel, "podglad-wylog@example.pl", "Podglad");

        using HttpResponseMessage wylogowanie = await AplikacjaTestowa.WyslijFormularzAsync(
            podglad, "/Wyloguj", new Dictionary<string, string>(),
            adresFormularza: "/Faktury");

        Assert.Equal(HttpStatusCode.Redirect, wylogowanie.StatusCode);
        Assert.Equal("/Logowanie", wylogowanie.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// Jedno konto w dwóch firmach przełącza się między nimi i widzi dane
    /// tylko tej, w której akurat pracuje.
    /// </summary>
    [Fact]
    public async Task PrzelaczenieFirmyZmieniaWidoczneDane()
    {
        using HttpClient pierwsza = await ZalozFirmeAsync("prz1");
        using HttpClient druga = await ZalozFirmeAsync("prz2");

        // Właściciel pierwszej firmy zaprasza właściciela drugiej.
        using HttpResponseMessage zaproszenie = await AplikacjaTestowa.WyslijFormularzAsync(
            pierwsza, "/Uzytkownicy?handler=Zaprosc",
            new Dictionary<string, string>
            {
                ["Email"] = "wlasciciel-prz2@example.pl",
                ["Rola"] = "Ksiegowy"
            },
            adresFormularza: "/Uzytkownicy");

        string adres = WzorzecOdnosnika().Match(
            await AplikacjaTestowa.TrescAsync(zaproszenie)).Groups[1].Value;

        using HttpResponseMessage przyjecie = await AplikacjaTestowa.WyslijFormularzAsync(
            druga, adres,
            new Dictionary<string, string>
            {
                ["Kod"] = adres.Split("kod=")[^1],
                ["Haslo"] = Haslo
            },
            adresFormularza: adres);

        Assert.Equal(HttpStatusCode.Redirect, przyjecie.StatusCode);

        // Po przyjęciu zaproszenia pracuje w pierwszej firmie...
        using (HttpResponseMessage faktury =
               await druga.GetAsync(new Uri("/Faktury", UriKind.Relative)))
        {
            Assert.Contains("Firma prz1", await AplikacjaTestowa.TrescAsync(faktury),
                StringComparison.Ordinal);
        }

        // ...a jako księgowy nie ma tam wstępu do ustawień.
        using (HttpResponseMessage ustawienia =
               await druga.GetAsync(new Uri("/Ustawienia", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.Redirect, ustawienia.StatusCode);
        }
    }

    // Odnośnik zaproszenia wypisany na stronie właściciela.
    [GeneratedRegex(@"(/Zaproszenie\?kod=[A-Za-z0-9_-]+)")]
    private static partial Regex WzorzecOdnosnika();
}
