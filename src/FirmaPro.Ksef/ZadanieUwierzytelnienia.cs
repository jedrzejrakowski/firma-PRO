using System.Xml.Linq;

namespace FirmaPro.Ksef;

/// <summary>
/// Dokument, którym program przedstawia się systemowi przy uwierzytelnianiu
/// certyfikatem.
/// </summary>
/// <remarks>
/// Zbudowany dokument jest następnie podpisywany (<see cref="PodpisXades"/>)
/// i wysyłany w całości. Wyzwanie wewnątrz wiąże podpis z jedną, konkretną
/// próbą logowania - przechwycony dokument nie da się użyć ponownie.
/// </remarks>
public static class ZadanieUwierzytelnienia
{
    /// <summary>Przestrzeń nazw dokumentu uwierzytelniającego KSeF 2.0.</summary>
    public const string Przestrzen = "http://ksef.mf.gov.pl/auth/token/2.0";

    /// <summary>
    /// Sposób rozpoznania podpisującego na podstawie certyfikatu.
    /// </summary>
    /// <remarks>
    /// <c>certificateSubject</c> oznacza, że system odczyta tożsamość z pól
    /// certyfikatu - dla firmy jest to numer NIP zapisany w polu
    /// <c>serialNumber</c> w postaci <c>VATPL-0000000000</c>. Druga
    /// możliwość, <c>certificateFingerprint</c>, służy certyfikatom bez
    /// takiego pola i wymaga wcześniejszego zgłoszenia odcisku w systemie.
    /// </remarks>
    public const string PoDanychCertyfikatu = "certificateSubject";

    /// <summary>Buduje dokument żądania dla firmy rozpoznawanej po numerze NIP.</summary>
    /// <param name="wyzwanie">Wartość otrzymana z <c>auth/challenge</c>.</param>
    /// <param name="nip">NIP firmy, w imieniu której działa program.</param>
    public static string Zbuduj(string wyzwanie, string nip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wyzwanie);
        ArgumentException.ThrowIfNullOrWhiteSpace(nip);

        XNamespace ns = Przestrzen;

        // Kolejność elementów jest częścią schematu, a nie kwestią gustu -
        // dokument z przestawionymi polami zostanie odrzucony.
        var dokument = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "AuthTokenRequest",
                new XElement(ns + "Challenge", wyzwanie),
                new XElement(ns + "ContextIdentifier",
                    new XElement(ns + "Nip", nip)),
                new XElement(ns + "SubjectIdentifierType", PoDanychCertyfikatu)));

        return dokument.Declaration + Environment.NewLine + dokument.ToString();
    }
}
