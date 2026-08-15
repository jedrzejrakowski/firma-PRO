using System.Security.Cryptography;
using System.Security.Cryptography.Xml;

namespace FirmaPro.Ksef;

/// <summary>
/// Uczy podpis XML algorytmu ECDSA z SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// Biblioteka podpisu XML zna od ręki wyłącznie RSA. Certyfikaty bywają
/// jednak wydawane na krzywych eliptycznych - o wyborze decyduje wystawca,
/// nie my - a wtedy podpisanie kończyłoby się komunikatem „nie udało się
/// utworzyć opisu podpisu", z którego nikt niczego nie wyczyta.
/// </para>
/// <para>
/// Rejestracja jest globalna dla procesu i wykonuje się raz, przy pierwszym
/// użyciu. Nie da się jej zrobić inaczej: tak zaprojektowano rejestr
/// algorytmów kryptograficznych w bibliotece standardowej.
/// </para>
/// <para>
/// Klasa jest publiczna wbrew temu, że nikt jej z zewnątrz nie woła:
/// rejestr algorytmów tworzy opis przez odbicie i odmawia przyjęcia typu
/// niewidocznego spoza zestawu.
/// </para>
/// </remarks>
public static class PodpisEcdsa
{
    public const string Algorytm = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";

    private static readonly Lock Zamek = new();
    private static bool _zarejestrowano;

    public static void ZarejestrujRaz()
    {
        if (_zarejestrowano)
        {
            return;
        }

        lock (Zamek)
        {
            if (_zarejestrowano)
            {
                return;
            }

            CryptoConfig.AddAlgorithm(typeof(Opis), Algorytm);
            _zarejestrowano = true;
        }
    }

    /// <summary>Opis algorytmu, którego szuka biblioteka podpisu po adresie.</summary>
    public sealed class Opis : SignatureDescription
    {
        public Opis()
        {
            KeyAlgorithm = typeof(ECDsa).AssemblyQualifiedName!;
            DigestAlgorithm = typeof(SHA256).AssemblyQualifiedName!;
            FormatterAlgorithm = typeof(Formater).AssemblyQualifiedName!;
            DeformatterAlgorithm = typeof(Deformater).AssemblyQualifiedName!;
        }

        public override HashAlgorithm CreateDigest() => SHA256.Create();

        public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key) =>
            new Formater(key);

        public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key) =>
            new Deformater(key);
    }

    /// <summary>Składa podpis ze skrótu.</summary>
    private sealed class Formater(AsymmetricAlgorithm klucz) : AsymmetricSignatureFormatter
    {
        private ECDsa _klucz = Wymagany(klucz);

        public override void SetKey(AsymmetricAlgorithm key) => _klucz = Wymagany(key);

        // Skrót jest z góry ustalony na SHA-256 - inny nie wchodzi w grę,
        // bo tego wymaga KSeF.
        public override void SetHashAlgorithm(string strName)
        {
        }

        public override byte[] CreateSignature(byte[] rgbHash) =>
            _klucz.SignHash(rgbHash);
    }

    /// <summary>Sprawdza podpis względem skrótu.</summary>
    private sealed class Deformater(AsymmetricAlgorithm klucz) : AsymmetricSignatureDeformatter
    {
        private ECDsa _klucz = Wymagany(klucz);

        public override void SetKey(AsymmetricAlgorithm key) => _klucz = Wymagany(key);

        public override void SetHashAlgorithm(string strName)
        {
        }

        public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature) =>
            _klucz.VerifyHash(rgbHash, rgbSignature);
    }

    private static ECDsa Wymagany(AsymmetricAlgorithm klucz) =>
        klucz as ECDsa
        ?? throw new BladKsefException(
            "Ten algorytm podpisu wymaga klucza na krzywej eliptycznej.");
}
