using System.Security.Cryptography.X509Certificates;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Jedno miejsce, w którym program przedstawia się systemowi KSeF.
/// </summary>
/// <remarks>
/// Wysyłka faktury, import zakupów i sprawdzenie połączenia potrzebują tego
/// samego: zalogować się metodą wybraną przez firmę. Trzymanie tego wyboru
/// w jednym miejscu sprawia, że przejście z tokena na certyfikat jest zmianą
/// ustawienia, a nie poprawką w kilku usługach naraz - a przy okazji nie da
/// się dodać czwartego miejsca, które o nowej metodzie nie wie.
/// </remarks>
public static class UwierzytelnienieKsef
{
    /// <summary>Loguje klienta metodą wybraną w ustawieniach firmy.</summary>
    /// <exception cref="BladKsefException">
    /// Gdy brakuje tokena albo certyfikatu - z komunikatem mówiącym,
    /// czego dokładnie brakuje.
    /// </exception>
    public static async Task ZalogujAsync(IKlientKsef klient, Firma firma,
                                          IOchronaTokena ochrona,
                                          CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(klient);
        ArgumentNullException.ThrowIfNull(firma);

        if (firma.MetodaUwierzytelnienia == MetodaUwierzytelnieniaKsef.Certyfikat)
        {
            using X509Certificate2 certyfikat = UslugaCertyfikatuKsef.Odczytaj(firma, ochrona)
                ?? throw new BladKsefException(
                    "Wybrano uwierzytelnianie certyfikatem, ale w ustawieniach firmy " +
                    "nie ma zapisanego certyfikatu.");

            await klient.UwierzytelnijCertyfikatemAsync(firma.Nip, certyfikat, anulowanie);
            return;
        }

        string token = OdczytajToken(firma, ochrona)
            ?? throw new BladKsefException(
                "Brak tokena KSeF. Uzupełnij go w ustawieniach firmy.");

        await klient.UwierzytelnijAsync(firma.Nip, token, anulowanie);
    }

    /// <summary>
    /// Odczytuje token KSeF firmy.
    /// </summary>
    /// <remarks>
    /// Zmienna środowiskowa ma pierwszeństwo przed wartością z bazy, żeby dało
    /// się pracować, nie zapisując sekretu w ogóle.
    /// </remarks>
    public static string? OdczytajToken(Firma firma, IOchronaTokena ochrona)
    {
        ArgumentNullException.ThrowIfNull(firma);
        ArgumentNullException.ThrowIfNull(ochrona);

        string? zeSrodowiska = Environment.GetEnvironmentVariable("KSEF_TOKEN");
        if (!string.IsNullOrWhiteSpace(zeSrodowiska))
        {
            return zeSrodowiska;
        }

        return firma.TokenKsefZaszyfrowany is { Length: > 0 } zaszyfrowany
            ? ochrona.Odszyfruj(zaszyfrowany)
            : null;
    }
}
