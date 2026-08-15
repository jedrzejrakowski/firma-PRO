using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FirmaPro.Testy;

/// <summary>
/// Certyfikat samopodpisany udający certyfikat KSeF.
/// </summary>
/// <remarks>
/// Środowisko testowe Ministerstwa przyjmuje certyfikaty samopodpisane,
/// więc taki sam certyfikat posłuży zarówno testom, jak i pierwszej próbie
/// na żywym środowisku testowym.
///
/// Tożsamość firmy zapisana jest w polu <c>serialNumber</c> w postaci
/// <c>VATPL-0000000000</c> - to po nim KSeF rozpoznaje podmiot, gdy żądanie
/// mówi <c>certificateSubject</c>.
/// </remarks>
internal static class CertyfikatTestowy
{
    public static X509Certificate2 DlaFirmy(string nip, string nazwa = "Firma testowa",
                                            bool krzywaEliptyczna = false)
    {
        var nazwaWyrozniona = new X500DistinguishedName(
            $"2.5.4.5=VATPL-{nip}, CN={nazwa}, O={nazwa}, C=PL");

        DateTimeOffset od = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset Do = DateTimeOffset.UtcNow.AddYears(2);

        if (krzywaEliptyczna)
        {
            using ECDsa klucz = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var zadanieEc = new CertificateRequest(nazwaWyrozniona, klucz, HashAlgorithmName.SHA256);

            return Wyeksportuj(zadanieEc.CreateSelfSigned(od, Do));
        }

        using RSA rsa = RSA.Create(2048);
        var zadanie = new CertificateRequest(nazwaWyrozniona, rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return Wyeksportuj(zadanie.CreateSelfSigned(od, Do));
    }

    /// <summary>
    /// Przepuszcza certyfikat przez format PFX.
    /// </summary>
    /// <remarks>
    /// Tą samą drogą pójdzie certyfikat wgrany przez użytkownika, więc test
    /// pracuje na dokładnie takim obiekcie, jaki powstanie w programie -
    /// razem z kluczem prywatnym odtworzonym z pliku.
    /// </remarks>
    private static X509Certificate2 Wyeksportuj(X509Certificate2 certyfikat)
    {
        using (certyfikat)
        {
            byte[] pfx = certyfikat.Export(X509ContentType.Pfx, "próba");
            return X509CertificateLoader.LoadPkcs12(pfx, "próba");
        }
    }
}
