using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace FirmaPro.Ksef;

/// <summary>
/// Składa podpis XAdES na dokumencie uwierzytelniającym KSeF.
/// </summary>
/// <remarks>
/// <para>
/// KSeF przyjmuje dwie drogi uwierzytelnienia: token (ciąg znaków szyfrowany
/// kluczem publicznym systemu) oraz <b>podpis certyfikatem</b>. Ta druga
/// staje się jedyną od 2027 roku, a różnica nie sprowadza się do sposobu
/// podpisania: token niesie uprawnienia nadane przy jego tworzeniu,
/// a certyfikat sam z siebie nie daje żadnych - potwierdza wyłącznie
/// tożsamość, a uprawnienia muszą być nadane wcześniej w systemie.
/// </para>
/// <para>
/// Podpis jest otaczający (enveloped): element <c>Signature</c> dopisywany
/// jest do korzenia podpisywanego dokumentu, a sam podpis obejmuje dwie
/// rzeczy - całą treść dokumentu oraz blok <c>SignedProperties</c> ze
/// znacznikiem czasu i odciskiem certyfikatu. Ten drugi odsyłacz odróżnia
/// XAdES od zwykłego podpisu XML-DSig i bez niego KSeF dokument odrzuca.
/// </para>
/// </remarks>
public static class PodpisXades
{
    /// <summary>Przestrzeń nazw rozszerzenia XAdES (ETSI TS 101 903).</summary>
    private const string PrzestrzenXades = "http://uri.etsi.org/01903/v1.3.2#";

    /// <summary>Typ odsyłacza wskazującego podpisane właściwości.</summary>
    private const string TypPodpisanychWlasciwosci = "http://uri.etsi.org/01903#SignedProperties";

    private const string IdentyfikatorPodpisu = "Signature";
    private const string IdentyfikatorWlasciwosci = "SignedProperties";

    /// <summary>
    /// Zegar podpisu cofnięty o minutę.
    /// </summary>
    /// <remarks>
    /// Zegar serwera KSeF i nasz nie muszą chodzić co do sekundy. Znacznik
    /// z przyszłości bywa odrzucany jako złożony przed początkiem ważności
    /// certyfikatu, więc minuta zapasu kosztuje nas nic, a oszczędza
    /// niepowtarzalnych błędów „podpis z przyszłości".
    /// </remarks>
    private static readonly TimeSpan ZapasZegara = TimeSpan.FromMinutes(-1);

    /// <summary>
    /// Podpisuje dokument i zwraca go w postaci tekstowej.
    /// </summary>
    /// <param name="xml">Dokument do podpisania (żądanie uwierzytelnienia).</param>
    /// <param name="certyfikat">Certyfikat wraz z kluczem prywatnym.</param>
    /// <param name="czas">Zegar - w testach podstawiany, żeby wynik był powtarzalny.</param>
    public static string Zloz(string xml, X509Certificate2 certyfikat, TimeProvider? czas = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        // Zachowanie białych znaków jest tu warunkiem poprawności, a nie
        // kwestią wyglądu: podpis liczony jest z bajtów dokumentu, więc
        // przeformatowanie go po drodze unieważniłoby skrót.
        var dokument = new XmlDocument { PreserveWhitespace = true };
        dokument.LoadXml(xml);

        return Zloz(dokument, certyfikat, czas).OuterXml;
    }

    /// <summary>Podpisuje dokument w miejscu i zwraca go dla wygody wywołania.</summary>
    public static XmlDocument Zloz(XmlDocument dokument, X509Certificate2 certyfikat,
                                   TimeProvider? czas = null)
    {
        ArgumentNullException.ThrowIfNull(dokument);
        ArgumentNullException.ThrowIfNull(certyfikat);

        if (dokument.DocumentElement is null)
        {
            throw new ArgumentException("Dokument nie ma elementu głównego.", nameof(dokument));
        }

        if (!certyfikat.HasPrivateKey)
        {
            throw new BladKsefException(
                "Certyfikat nie zawiera klucza prywatnego, więc nie da się nim podpisać. " +
                "Wgraj plik PFX (PKCS#12) razem z kluczem, a nie sam certyfikat.");
        }

        var podpis = new PodpisZWlasciwosciami(dokument);
        UstawKlucz(podpis, certyfikat);

        podpis.Signature.Id = IdentyfikatorPodpisu;
        podpis.KeyInfo = new KeyInfo();
        podpis.KeyInfo.AddClause(new KeyInfoX509Data(certyfikat));

        DodajOdsylaczDoTresci(podpis);
        DodajOdsylaczDoWlasciwosci(podpis);

        DateTimeOffset znacznik = (czas ?? TimeProvider.System).GetUtcNow().Add(ZapasZegara);
        podpis.DodajDane(new DataObject { Data = ZbudujWlasciwosci(certyfikat, znacznik) });

        podpis.ComputeSignature();

        dokument.DocumentElement.AppendChild(dokument.ImportNode(podpis.GetXml(), true));

        return dokument;
    }

    private static void UstawKlucz(SignedXml podpis, X509Certificate2 certyfikat)
    {
        // Certyfikaty KSeF bywają wydawane zarówno na kluczu RSA, jak i na
        // krzywych eliptycznych - obsługujemy oba, bo o wyborze decyduje
        // wystawca, nie my.
        if (certyfikat.GetRSAPrivateKey() is { } rsa)
        {
            podpis.SigningKey = rsa;
            return;
        }

        if (certyfikat.GetECDsaPrivateKey() is { } ecdsa)
        {
            // Biblioteka podpisu XML nie zna ECDSA z pudełka - trzeba jej
            // ten algorytm najpierw pokazać.
            PodpisEcdsa.ZarejestrujRaz();

            podpis.SigningKey = ecdsa;
            podpis.SignedInfo!.SignatureMethod = PodpisEcdsa.Algorytm;
            return;
        }

        throw new BladKsefException(
            "Nie udało się odczytać klucza prywatnego z certyfikatu. " +
            "Obsługiwane są klucze RSA oraz na krzywych eliptycznych.");
    }

    /// <summary>Odsyłacz obejmujący cały dokument, bez samego podpisu.</summary>
    private static void DodajOdsylaczDoTresci(SignedXml podpis)
    {
        var odsylacz = new Reference(string.Empty) { DigestMethod = SignedXml.XmlDsigSHA256Url };

        odsylacz.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        odsylacz.AddTransform(new XmlDsigExcC14NTransform());

        podpis.AddReference(odsylacz);
    }

    /// <summary>Odsyłacz do bloku XAdES - to on czyni z podpisu XAdES.</summary>
    private static void DodajOdsylaczDoWlasciwosci(SignedXml podpis)
    {
        var odsylacz = new Reference("#" + IdentyfikatorWlasciwosci)
        {
            Type = TypPodpisanychWlasciwosci,
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };

        odsylacz.AddTransform(new XmlDsigExcC14NTransform());

        podpis.AddReference(odsylacz);
    }

    /// <summary>
    /// Buduje blok podpisanych właściwości: kiedy i czym podpisano.
    /// </summary>
    /// <remarks>
    /// Odcisk certyfikatu wewnątrz podpisanego bloku wiąże podpis z konkretnym
    /// certyfikatem. Bez tego ktoś mógłby podmienić <c>KeyInfo</c> na inny
    /// certyfikat, nie ruszając samego podpisu.
    /// </remarks>
    private static XmlNodeList ZbudujWlasciwosci(X509Certificate2 certyfikat,
                                                 DateTimeOffset znacznik)
    {
        string odcisk = Convert.ToBase64String(certyfikat.GetCertHash(HashAlgorithmName.SHA256));

        // Numer seryjny zapisywany jest dziesiętnie, a nie szesnastkowo -
        // tak wymaga XAdES, mimo że certyfikat pokazuje go zwykle inaczej.
        string numerSeryjny = new BigInteger(certyfikat.GetSerialNumber())
            .ToString(CultureInfo.InvariantCulture);

        var dokument = new XmlDocument();
        dokument.LoadXml(
            $"""
             <xades:QualifyingProperties Target="#{IdentyfikatorPodpisu}"
                 xmlns:xades="{PrzestrzenXades}" xmlns="{SignedXml.XmlDsigNamespaceUrl}">
               <xades:SignedProperties Id="{IdentyfikatorWlasciwosci}">
                 <xades:SignedSignatureProperties>
                   <xades:SigningTime>{znacznik:O}</xades:SigningTime>
                   <xades:SigningCertificate>
                     <xades:Cert>
                       <xades:CertDigest>
                         <DigestMethod Algorithm="{SignedXml.XmlDsigSHA256Url}" />
                         <DigestValue>{odcisk}</DigestValue>
                       </xades:CertDigest>
                       <xades:IssuerSerial>
                         <X509IssuerName>{certyfikat.Issuer}</X509IssuerName>
                         <X509SerialNumber>{numerSeryjny}</X509SerialNumber>
                       </xades:IssuerSerial>
                     </xades:Cert>
                   </xades:SigningCertificate>
                 </xades:SignedSignatureProperties>
               </xades:SignedProperties>
             </xades:QualifyingProperties>
             """);

        return dokument.ChildNodes;
    }

    /// <summary>
    /// Podpis widzący identyfikatory wewnątrz własnych bloków danych.
    /// </summary>
    /// <remarks>
    /// Wbudowana klasa szuka elementu o danym identyfikatorze wyłącznie
    /// w podpisywanym dokumencie. Blok XAdES żyje natomiast wewnątrz podpisu,
    /// więc bez tego rozszerzenia odsyłacz <c>#SignedProperties</c> nie miałby
    /// czego wskazać i podpisanie kończyłoby się wyjątkiem.
    /// </remarks>
    private sealed class PodpisZWlasciwosciami(XmlDocument dokument) : SignedXml(dokument)
    {
        private readonly List<DataObject> _bloki = [];

        public void DodajDane(DataObject blok)
        {
            _bloki.Add(blok);
            AddObject(blok);
        }

        public override XmlElement? GetIdElement(XmlDocument? dokument, string identyfikator)
        {
            if (base.GetIdElement(dokument, identyfikator) is { } znaleziony)
            {
                return znaleziony;
            }

            return _bloki
                .SelectMany(blok => blok.Data.Cast<XmlNode>())
                .Select(wezel => wezel.SelectSingleNode($"//*[@Id='{identyfikator}']") as XmlElement)
                .FirstOrDefault(element => element is not null);
        }
    }
}
