using System.Net;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Ekran sprawdzenia połączenia z KSeF, drogą przeglądarki.
/// </summary>
[Collection(KolekcjaAplikacji.Nazwa)]
public sealed class TestyDiagnostykiWeb(AplikacjaTestowa aplikacja)
{
    [Fact]
    public async Task StronaPokazujeSrodowiskoIPrzyciskSprawdzenia()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage strona =
            await klient.GetAsync(new Uri("/Ksef", UriKind.Relative));

        strona.EnsureSuccessStatusCode();
        string html = await AplikacjaTestowa.TrescAsync(strona);

        Assert.Contains("Sprawdź połączenie", html, StringComparison.Ordinal);
        Assert.Contains("Test", html, StringComparison.Ordinal);

        // Dopóki nikt nie kliknął, nie ma wyników - strona sama z siebie
        // nie rusza sieci przy każdym otwarciu.
        Assert.DoesNotContain("Uwierzytelnienie tokenem", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bez tokena sprawdzenie zatrzymuje się na kroku, który za to odpowiada.
    /// </summary>
    /// <remarks>
    /// Firma demonstracyjna nie ma tokena, więc jest to zarazem sprawdzenie,
    /// że ekran nie próbuje łączyć się z prawdziwym KSeF podczas testów.
    /// </remarks>
    [Fact]
    public async Task BezTokenaEkranWskazujeWlasciwyKrok()
    {
        using HttpClient klient = await aplikacja.ZalogujAsync();

        using HttpResponseMessage odpowiedz = await AplikacjaTestowa.WyslijFormularzAsync(
            klient, "/Ksef", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, odpowiedz.StatusCode);
        string html = await AplikacjaTestowa.TrescAsync(odpowiedz);

        Assert.Contains("Sprawdzenie nie przeszło", html, StringComparison.Ordinal);
        Assert.Contains("Nie zapisano tokena KSeF", html, StringComparison.Ordinal);
        Assert.Contains("Nie sprawdzano", html, StringComparison.Ordinal);
    }
}
