using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FirmaPro.Ksef;

namespace FirmaPro.Testy;

/// <summary>
/// Certyfikaty na potrzeby testów.
/// </summary>
/// <remarks>
/// Odmiana RSA powstaje tym samym kodem, którego używa program przy
/// wystawianiu certyfikatu testowego - dzięki temu testy sprawdzają to, co
/// naprawdę trafi do rąk użytkownika, a nie osobną, podobną implementację.
/// </remarks>
internal static class CertyfikatTestowy
{
    public static X509Certificate2 DlaFirmy(string nip, string nazwa = "Firma testowa",
                                            bool krzywaEliptyczna = false)
    {
        if (!krzywaEliptyczna)
        {
            return PrzezPlik(CertyfikatSamopodpisany.DlaFirmy(nip, nazwa));
        }

        var nazwaWyrozniona = new X500DistinguishedName(
            $"2.5.4.5=VATPL-{nip}, CN={nazwa}, O={nazwa}, C=PL");

        using ECDsa klucz = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var zadanie = new CertificateRequest(nazwaWyrozniona, klucz, HashAlgorithmName.SHA256);

        return PrzezPlik(zadanie.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(2)));
    }

    /// <summary>
    /// Przepuszcza certyfikat przez format PFX.
    /// </summary>
    /// <remarks>
    /// Tą samą drogą pójdzie certyfikat zapisany w bazie, więc test pracuje
    /// na dokładnie takim obiekcie, jaki powstanie w programie - razem
    /// z kluczem prywatnym odtworzonym z pliku.
    /// </remarks>
    private static X509Certificate2 PrzezPlik(X509Certificate2 certyfikat)
    {
        using (certyfikat)
        {
            return X509CertificateLoader.LoadPkcs12(
                certyfikat.Export(X509ContentType.Pfx, "próba"), "próba");
        }
    }
}
