using System.Net;

namespace FirmaPro.Testy;

/// <summary>
/// Testy powłoki programu: pulpitu, menu bocznego i ekranu faktur.
/// </summary>
/// <remarks>
/// Zmiana wyglądu przesunęła adresy - lista zakupów jest teraz zakładką
/// ekranu faktur, a program otwiera się na pulpicie. Te testy pilnują, żeby
/// stare odnośniki dalej działały, a nowy ekran naprawdę pokazywał oba
/// rodzaje dokumentów.
/// </remarks>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyPowloki(AplikacjaTestowa aplikacja)
{
    [Fact]
    public async Task PulpitPokazujeKafelkiIWykres()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Pulpit", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);

        Assert.Contains("Po terminie", html, StringComparison.Ordinal);
        Assert.Contains("Sprzedaż w tym miesiącu", html, StringComparison.Ordinal);
        Assert.Contains("Sprzedaż miesiąc po miesiącu", html, StringComparison.Ordinal);
        Assert.Contains("Na dziś", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Menu boczne ma być na każdym ekranie i prowadzić do wszystkich działów.
    /// </summary>
    [Fact]
    public async Task MenuBoczneProwadziDoWszystkichDzialow()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Faktury", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);

        Assert.Contains("Sprzedaż i zakupy", html, StringComparison.Ordinal);
        Assert.Contains("Księgowość", html, StringComparison.Ordinal);

        foreach (string adres in new[]
                 {
                     "/Pulpit", "/Faktury", "/Naleznosci", "/Kontrahenci",
                     "/Rejestry/Vat", "/Rejestry/Jpk"
                 })
        {
            Assert.Contains($"href=\"{adres}\"", html, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Ustawienia i użytkownicy siedzą pod trybikiem, a nie w menu bocznym.
    /// </summary>
    [Fact]
    public async Task UstawieniaSaPodTrybikiem()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Pulpit", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);

        Assert.Contains("menu-trybik", html, StringComparison.Ordinal);
        Assert.Contains("Ustawienia firmy", html, StringComparison.Ordinal);
        Assert.Contains("Użytkownicy i role", html, StringComparison.Ordinal);
        Assert.Contains("Wyloguj", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rodzaje faktur schowane pod jednym przyciskiem, a nie rozrzucone po pasku.
    /// </summary>
    [Fact]
    public async Task PrzyciskWystawianiaMaListeRodzajow()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Faktury", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);

        Assert.Contains("menu-rodzaje", html, StringComparison.Ordinal);
        Assert.Contains("Zaliczkowa", html, StringComparison.Ordinal);
        Assert.Contains("Korygująca", html, StringComparison.Ordinal);
        Assert.Contains("/Faktury/Zaliczkowa", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sprzedaż i koszty na jednym ekranie, w dwóch zakładkach.
    /// </summary>
    [Fact]
    public async Task EkranFakturMaZakladkiPrzychodowIKosztow()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Faktury", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);

        Assert.Contains("Przychody", html, StringComparison.Ordinal);
        Assert.Contains("Koszty", html, StringComparison.Ordinal);
        Assert.Contains("panel-przychody", html, StringComparison.Ordinal);
        Assert.Contains("panel-koszty", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adres z zakładką kosztów otwiera od razu właściwy panel.
    /// </summary>
    /// <remarks>
    /// Wybór zakładki siedzi w adresie, więc działa też przy wyłączonych
    /// skryptach i po powrocie przyciskiem „wstecz”.
    /// </remarks>
    [Fact]
    public async Task AdresZWidokiemKosztowOtwieraZakladkeKosztow()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Faktury?widok=koszty", UriKind.Relative));

        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);

        int przychody = html.IndexOf("id=\"panel-przychody\"", StringComparison.Ordinal);
        int koszty = html.IndexOf("id=\"panel-koszty\"", StringComparison.Ordinal);

        Assert.True(przychody > 0 && koszty > przychody);

        // Ukryty jest panel przychodów, a nie kosztów.
        Assert.Contains("id=\"panel-przychody\" role=\"tabpanel\" hidden",
            html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"panel-koszty\" role=\"tabpanel\" hidden",
            html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dawny adres listy zakupów prowadzi do zakładki kosztów.
    /// </summary>
    [Fact]
    public async Task StaryAdresZakupowProwadziDoZakladkiKosztow()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz =
            await klient.GetAsync(new Uri("/Zakupy", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, odpowiedz.StatusCode);
        Assert.Equal("/Faktury?widok=koszty",
            odpowiedz.Headers.Location?.OriginalString);
    }
}
