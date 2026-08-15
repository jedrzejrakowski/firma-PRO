using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Opis zapisanego certyfikatu - do pokazania na ekranie.</summary>
/// <param name="Podmiot">Nazwa wyróżniona posiadacza.</param>
/// <param name="Odcisk">Odcisk SHA-256, po którym rozpoznaje się certyfikat.</param>
public sealed record OpisCertyfikatu(
    string Podmiot,
    string Odcisk,
    DateTimeOffset WaznyDo,
    DateOnly Dzisiaj)
{
    /// <summary>Ile dni zostało do końca ważności; ujemnie, gdy już minęła.</summary>
    public int DniDoKonca =>
        DateOnly.FromDateTime(WaznyDo.UtcDateTime).DayNumber - Dzisiaj.DayNumber;

    public bool Wygasl => DniDoKonca < 0;

    /// <summary>
    /// Czy pora ostrzec o zbliżającym się końcu ważności.
    /// </summary>
    /// <remarks>
    /// Miesiąc wystarcza, żeby spokojnie wystąpić o nowy certyfikat, a nie
    /// jest tak długi, żeby ostrzeżenie spowszedniało.
    /// </remarks>
    public bool WkrotceWygasnie => !Wygasl && DniDoKonca <= 30;
}

/// <summary>
/// Zarządza certyfikatem, którym firma uwierzytelnia się w KSeF.
/// </summary>
/// <remarks>
/// <para>
/// Certyfikat trzymany jest tak samo jak token - zaszyfrowany kluczem
/// aplikacji, nigdy otwartym tekstem. Klucz prywatny pozwala wystawiać
/// faktury w imieniu firmy, więc jest równie wrażliwy.
/// </para>
/// <para>
/// Data ważności zapisywana jest osobno, żeby dało się ostrzec o wygasaniu
/// bez odszyfrowywania certyfikatu przy każdym otwarciu ekranu.
/// </para>
/// </remarks>
public sealed class UslugaCertyfikatuKsef(
    FirmaProDbContext baza,
    IOchronaTokena ochronaTokena,
    TimeProvider czas)
{
    /// <summary>Hasło pliku zapisywanego w bazie.</summary>
    /// <remarks>
    /// Puste - ochroną jest szyfrowanie całego pliku kluczem aplikacji,
    /// a drugie hasło obok pierwszego byłoby tylko kolejnym sekretem
    /// do zgubienia.
    /// </remarks>
    private const string? BezHasla = null;

    private DateOnly Dzisiaj => DateOnly.FromDateTime(czas.GetUtcNow().UtcDateTime);

    /// <summary>Odczytuje certyfikat firmy wraz z kluczem prywatnym.</summary>
    public static X509Certificate2? Odczytaj(Firma firma, IOchronaTokena ochrona)
    {
        ArgumentNullException.ThrowIfNull(firma);
        ArgumentNullException.ThrowIfNull(ochrona);

        if (firma.CertyfikatKsefZaszyfrowany is not { Length: > 0 } zaszyfrowany)
        {
            return null;
        }

        if (ochrona.Odszyfruj(zaszyfrowany) is not { Length: > 0 } wBase64)
        {
            return null;
        }

        return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(wBase64), BezHasla);
    }

    /// <summary>Opis zapisanego certyfikatu albo <c>null</c>, gdy go nie ma.</summary>
    public async Task<OpisCertyfikatu?> OpisAsync(CancellationToken anulowanie = default)
    {
        Firma firma = await WczytajFirmeAsync(anulowanie);

        return firma.CertyfikatKsefZaszyfrowany is { Length: > 0 }
               && firma.CertyfikatWaznyDo is { } waznyDo
            ? new OpisCertyfikatu(
                firma.CertyfikatPodmiot ?? "(brak nazwy)",
                firma.CertyfikatOdcisk ?? string.Empty,
                waznyDo,
                Dzisiaj)
            : null;
    }

    /// <summary>
    /// Zapisuje certyfikat wgrany przez użytkownika.
    /// </summary>
    /// <param name="pfx">Zawartość pliku PFX / P12.</param>
    /// <param name="haslo">Hasło pliku - puste, gdy plik go nie ma.</param>
    public async Task<WynikWalidacji> ZapiszAsync(byte[] pfx, string? haslo,
                                                  CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        if (pfx is not { Length: > 0 })
        {
            walidacja.Blad("Certyfikat", "wskaż plik certyfikatu");
            return walidacja;
        }

        X509Certificate2 certyfikat;
        try
        {
            certyfikat = X509CertificateLoader.LoadPkcs12(
                pfx, string.IsNullOrEmpty(haslo) ? null : haslo);
        }
        catch (CryptographicException blad)
        {
            // Zanim powiemy „nie udało się odczytać", sprawdzamy najczęstszą
            // pomyłkę: wyeksportowany sam certyfikat (.cer, .crt) zamiast
            // pliku z kluczem. Komunikat o błędnym haśle kazałby wtedy szukać
            // zupełnie nie tam.
            if (CzySamCertyfikat(pfx))
            {
                walidacja.Blad("Certyfikat",
                    "plik zawiera sam certyfikat, bez klucza prywatnego. " +
                    "Wyeksportuj go ponownie razem z kluczem, w formacie PFX albo P12");

                return walidacja;
            }

            walidacja.Blad("Certyfikat",
                "nie udało się odczytać pliku - sprawdź hasło i to, czy plik " +
                $"jest w formacie PFX albo P12 ({blad.Message})");

            return walidacja;
        }

        using (certyfikat)
        {
            if (!certyfikat.HasPrivateKey)
            {
                walidacja.Blad("Certyfikat",
                    "plik nie zawiera klucza prywatnego, więc nie da się nim podpisać. " +
                    "Wyeksportuj certyfikat razem z kluczem");

                return walidacja;
            }

            if (certyfikat.NotAfter.ToUniversalTime() < czas.GetUtcNow().UtcDateTime)
            {
                walidacja.Blad("Certyfikat",
                    $"certyfikat stracił ważność {certyfikat.NotAfter:yyyy-MM-dd}");

                return walidacja;
            }

            await ZapiszCertyfikatAsync(certyfikat, anulowanie);
        }

        return walidacja;
    }

    /// <summary>
    /// Wystawia certyfikat samopodpisany do prób na środowisku testowym.
    /// </summary>
    /// <remarks>
    /// Produkcja takiego certyfikatu nie przyjmie i nie ma go tam po co
    /// zakładać - stąd sprawdzenie środowiska.
    /// </remarks>
    public async Task<WynikWalidacji> WystawTestowyAsync(CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();
        Firma firma = await WczytajFirmeAsync(anulowanie);

        if (firma.Srodowisko == SrodowiskoKsef.Produkcja)
        {
            walidacja.Blad("Certyfikat",
                "certyfikat samopodpisany działa wyłącznie na środowisku testowym. " +
                "Na produkcji potrzebny jest certyfikat wydany przez KSeF");

            return walidacja;
        }

        if (!Walidator.NipPoprawny(firma.Nip))
        {
            walidacja.Blad("Certyfikat",
                "najpierw uzupełnij poprawny NIP firmy - to on trafia do certyfikatu");

            return walidacja;
        }

        using X509Certificate2 certyfikat =
            CertyfikatSamopodpisany.DlaFirmy(firma.Nip, firma.Nazwa);

        await ZapiszCertyfikatAsync(certyfikat, anulowanie);

        return walidacja;
    }

    /// <summary>Usuwa zapisany certyfikat.</summary>
    public async Task UsunAsync(CancellationToken anulowanie = default)
    {
        Firma firma = await WczytajFirmeAsync(anulowanie);

        firma.CertyfikatKsefZaszyfrowany = null;
        firma.CertyfikatOdcisk = null;
        firma.CertyfikatPodmiot = null;
        firma.CertyfikatWaznyDo = null;

        await baza.SaveChangesAsync(anulowanie);
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>Czy plik jest samym certyfikatem, bez klucza prywatnego.</summary>
    private static bool CzySamCertyfikat(byte[] plik)
    {
        try
        {
            using X509Certificate2 certyfikat = X509CertificateLoader.LoadCertificate(plik);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private async Task ZapiszCertyfikatAsync(X509Certificate2 certyfikat,
                                             CancellationToken anulowanie)
    {
        Firma firma = await WczytajFirmeAsync(anulowanie);

        byte[] pfx = certyfikat.Export(X509ContentType.Pfx, BezHasla);

        firma.CertyfikatKsefZaszyfrowany =
            ochronaTokena.Zaszyfruj(Convert.ToBase64String(pfx));

        firma.CertyfikatOdcisk = Convert.ToHexString(
            certyfikat.GetCertHash(HashAlgorithmName.SHA256));

        firma.CertyfikatPodmiot = certyfikat.Subject;
        firma.CertyfikatWaznyDo = certyfikat.NotAfter.ToUniversalTime();

        await baza.SaveChangesAsync(anulowanie);
    }

    private async Task<Firma> WczytajFirmeAsync(CancellationToken anulowanie) =>
        await baza.Firmy.SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);
}
