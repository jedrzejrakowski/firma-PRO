using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FirmaPro.Ksef;

/// <summary>
/// Operacje kryptograficzne wymagane przez KSeF.
/// </summary>
/// <remarks>
/// KSeF nie przyjmuje faktur przesłanych otwartym tekstem. Obowiązuje schemat
/// "koperty cyfrowej":
/// <list type="number">
/// <item>dla każdej sesji losowany jest klucz AES-256 i wektor inicjujący,</item>
/// <item>plik faktury szyfrowany jest algorytmem AES-256-CBC z dopełnieniem
/// PKCS#7,</item>
/// <item>sam klucz AES szyfrowany jest kluczem publicznym Ministerstwa
/// Finansów algorytmem RSA-OAEP z funkcją skrótu SHA-256,</item>
/// <item>zaszyfrowany klucz trafia do systemu przy otwarciu sesji,
/// a zaszyfrowane faktury - przy każdej wysyłce.</item>
/// </list>
/// Tą samą metodą szyfrowany jest token KSeF podczas uwierzytelniania.
/// </remarks>
public static class Kryptografia
{
    /// <summary>Długość klucza sesji w bajtach (256 bitów).</summary>
    public const int DlugoscKlucza = 32;

    /// <summary>Długość wektora inicjującego w bajtach (128 bitów).</summary>
    public const int DlugoscWektora = 16;

    /// <summary>Klucz i wektor inicjujący jednej sesji wysyłkowej.</summary>
    public sealed record KluczSesji(byte[] Klucz, byte[] WektorInicjujacy);

    /// <summary>
    /// Losuje klucz sesji.
    /// </summary>
    /// <remarks>
    /// Dokumentacja KSeF zaleca nowy klucz dla każdej sesji - dzięki temu
    /// przechwycenie jednego klucza nie odsłania faktur z pozostałych.
    /// </remarks>
    public static KluczSesji GenerujKluczSesji() => new(
        RandomNumberGenerator.GetBytes(DlugoscKlucza),
        RandomNumberGenerator.GetBytes(DlugoscWektora));

    /// <summary>Szyfruje dane algorytmem AES-256-CBC z dopełnieniem PKCS#7.</summary>
    public static byte[] SzyfrujAes(byte[] dane, KluczSesji klucz)
    {
        ArgumentNullException.ThrowIfNull(dane);
        ArgumentNullException.ThrowIfNull(klucz);

        using Aes aes = Aes.Create();
        aes.Key = klucz.Klucz;
        return aes.EncryptCbc(dane, klucz.WektorInicjujacy, PaddingMode.PKCS7);
    }

    /// <summary>Odwraca działanie <see cref="SzyfrujAes"/>.</summary>
    public static byte[] OdszyfrujAes(byte[] szyfrogram, KluczSesji klucz)
    {
        ArgumentNullException.ThrowIfNull(szyfrogram);
        ArgumentNullException.ThrowIfNull(klucz);

        using Aes aes = Aes.Create();
        aes.Key = klucz.Klucz;
        return aes.DecryptCbc(szyfrogram, klucz.WektorInicjujacy, PaddingMode.PKCS7);
    }

    /// <summary>Wyciąga klucz publiczny RSA z certyfikatu X.509 w formacie DER.</summary>
    public static RSA KluczPublicznyZCertyfikatu(byte[] certyfikatDer)
    {
        using var certyfikat = X509CertificateLoader.LoadCertificate(certyfikatDer);
        return certyfikat.GetRSAPublicKey()
            ?? throw new InvalidOperationException(
                "Certyfikat KSeF nie zawiera klucza RSA - ten program obsługuje " +
                "wyłącznie szyfrowanie RSA-OAEP.");
    }

    /// <summary>Szyfruje dane algorytmem RSA-OAEP z SHA-256 (także w masce MGF1).</summary>
    public static byte[] SzyfrujRsa(byte[] dane, RSA kluczPubliczny)
    {
        ArgumentNullException.ThrowIfNull(kluczPubliczny);
        return kluczPubliczny.Encrypt(dane, RSAEncryptionPadding.OaepSHA256);
    }

    /// <summary>Skrót SHA-256 w Base64 - w takiej postaci przyjmuje go API.</summary>
    public static string SkrotBase64(byte[] dane) =>
        Convert.ToBase64String(SHA256.HashData(dane));

    /// <summary>
    /// Skrót SHA-256 w Base64URL - format używany w linku kodu QR.
    /// </summary>
    /// <remarks>
    /// Base64URL zamienia znaki "+" i "/" na "-" i "_" oraz pomija wyrównanie,
    /// dzięki czemu skrót można wstawić do adresu bez dodatkowego kodowania.
    /// </remarks>
    public static string SkrotBase64Url(byte[] dane) =>
        Base64Url.EncodeToString(SHA256.HashData(dane));
}
