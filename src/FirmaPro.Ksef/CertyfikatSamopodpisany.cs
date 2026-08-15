using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FirmaPro.Ksef;

/// <summary>
/// Wystawia certyfikat samopodpisany do prób w środowisku testowym.
/// </summary>
/// <remarks>
/// <para>
/// Środowisko testowe Ministerstwa przyjmuje certyfikaty samopodpisane, więc
/// integrację da się sprawdzić bez występowania o prawdziwy certyfikat KSeF.
/// Na produkcji taki certyfikat zostanie odrzucony i tak ma być - to nie jest
/// obejście, tylko sposób na przejście całej drogi przed pierwszym
/// prawdziwym wystawieniem.
/// </para>
/// <para>
/// Tożsamość firmy zapisywana jest w polu <c>serialNumber</c> w postaci
/// <c>VATPL-0000000000</c>. To po nim KSeF rozpoznaje podmiot, gdy żądanie
/// mówi, że tożsamość wynika z danych certyfikatu.
/// </para>
/// </remarks>
public static class CertyfikatSamopodpisany
{
    /// <summary>Ile lat ma być ważny wystawiony certyfikat.</summary>
    private const int LataWaznosci = 2;

    /// <summary>
    /// Wystawia certyfikat dla firmy o podanym numerze NIP.
    /// </summary>
    /// <param name="nip">NIP firmy - trafia do pola serialNumber.</param>
    /// <param name="nazwa">Nazwa firmy pokazywana w certyfikacie.</param>
    /// <returns>Certyfikat wraz z kluczem prywatnym.</returns>
    public static X509Certificate2 DlaFirmy(string nip, string nazwa)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nip);
        ArgumentException.ThrowIfNullOrWhiteSpace(nazwa);

        var nazwaWyrozniona = new X500DistinguishedName(
            $"2.5.4.5=VATPL-{nip}, CN={Oczysc(nazwa)}, O={Oczysc(nazwa)}, C=PL");

        using RSA klucz = RSA.Create(2048);
        var zadanie = new CertificateRequest(nazwaWyrozniona, klucz,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Certyfikat do uwierzytelniania ma mieć przeznaczenie „podpis
        // cyfrowy" - tego wymaga KSeF od certyfikatów uwierzytelniających.
        zadanie.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        // Godzina wstecz na wypadek rozjechanych zegarów - inaczej certyfikat
        // wystawiony przed chwilą bywa uznany za jeszcze nieważny.
        return zadanie.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddYears(LataWaznosci));
    }

    /// <summary>
    /// Usuwa z nazwy znaki, które rozbiłyby zapis nazwy wyróżnionej.
    /// </summary>
    /// <remarks>
    /// Przecinek albo znak równości w nazwie firmy zostałyby odczytane jako
    /// granica kolejnego pola i certyfikat powstałby z przekłamaną treścią.
    /// </remarks>
    private static string Oczysc(string nazwa) =>
        new([.. nazwa.Where(z => z is not (',' or '=' or '+' or '<' or '>' or '#' or ';' or '"' or '\\'))]);
}
