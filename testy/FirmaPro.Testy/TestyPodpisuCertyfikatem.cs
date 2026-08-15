using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using FirmaPro.Ksef;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Uwierzytelnianie certyfikatem - droga, która od 2027 roku zastąpi token.
/// </summary>
/// <remarks>
/// Atrapa serwera weryfikuje podpis kluczem publicznym z certyfikatu
/// dołączonego do dokumentu, więc test sprawdza prawdziwą kryptografię,
/// a nie zgodność nazw pól.
/// </remarks>
public sealed class TestyPodpisuCertyfikatem
{
    private const string Nip = "5252248481";

    [Fact]
    public void DokumentZadaniaMaWymaganaStrukture()
    {
        string xml = ZadanieUwierzytelnienia.Zbuduj("20260808-CR-ABC", Nip);

        var dokument = new XmlDocument();
        dokument.LoadXml(xml);

        XmlNamespaceManager przestrzenie = new(dokument.NameTable);
        przestrzenie.AddNamespace("a", ZadanieUwierzytelnienia.Przestrzen);

        Assert.Equal("AuthTokenRequest", dokument.DocumentElement!.LocalName);
        Assert.Equal(ZadanieUwierzytelnienia.Przestrzen, dokument.DocumentElement.NamespaceURI);

        Assert.Equal("20260808-CR-ABC",
            dokument.SelectSingleNode("//a:Challenge", przestrzenie)?.InnerText);

        // NIP siedzi w elemencie nazwanym rodzajem identyfikatora, a nie
        // w atrybucie - tego wymaga schemat.
        Assert.Equal(Nip,
            dokument.SelectSingleNode("//a:ContextIdentifier/a:Nip", przestrzenie)?.InnerText);

        Assert.Equal("certificateSubject",
            dokument.SelectSingleNode("//a:SubjectIdentifierType", przestrzenie)?.InnerText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PodpisJestPoprawnyDlaObuRodzajowKluczy(bool krzywaEliptyczna)
    {
        using X509Certificate2 certyfikat =
            CertyfikatTestowy.DlaFirmy(Nip, krzywaEliptyczna: krzywaEliptyczna);

        string podpisany = PodpisXades.Zloz(
            ZadanieUwierzytelnienia.Zbuduj("20260808-CR-ABC", Nip), certyfikat);

        Assert.True(Sprawdz(podpisany), "Podpis nie przeszedł weryfikacji.");
    }

    /// <summary>
    /// Podpis obejmuje także blok XAdES, a nie samą treść dokumentu.
    /// </summary>
    /// <remarks>
    /// To właśnie ten drugi odsyłacz odróżnia XAdES od zwykłego podpisu XML
    /// i bez niego KSeF dokument odrzuca.
    /// </remarks>
    [Fact]
    public void PodpisObejmujePodpisaneWlasciwosci()
    {
        using X509Certificate2 certyfikat = CertyfikatTestowy.DlaFirmy(Nip);

        var dokument = new XmlDocument { PreserveWhitespace = true };
        dokument.LoadXml(PodpisXades.Zloz(
            ZadanieUwierzytelnienia.Zbuduj("20260808-CR-ABC", Nip), certyfikat));

        XmlNamespaceManager przestrzenie = new(dokument.NameTable);
        przestrzenie.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        przestrzenie.AddNamespace("xades", "http://uri.etsi.org/01903/v1.3.2#");

        Assert.NotNull(dokument.SelectSingleNode("//xades:SignedProperties", przestrzenie));
        Assert.NotNull(dokument.SelectSingleNode("//xades:SigningTime", przestrzenie));
        Assert.NotNull(dokument.SelectSingleNode("//xades:CertDigest", przestrzenie));

        // Dwa odsyłacze: treść dokumentu i podpisane właściwości.
        Assert.Equal(2,
            dokument.SelectNodes("//ds:SignedInfo/ds:Reference", przestrzenie)!.Count);
    }

    /// <summary>Zmiana treści po podpisaniu unieważnia podpis.</summary>
    [Fact]
    public void ZmianaTresciPsujePodpis()
    {
        using X509Certificate2 certyfikat = CertyfikatTestowy.DlaFirmy(Nip);

        string podpisany = PodpisXades.Zloz(
            ZadanieUwierzytelnienia.Zbuduj("20260808-CR-ABC", Nip), certyfikat);

        Assert.False(Sprawdz(podpisany.Replace(Nip, "7010001453", StringComparison.Ordinal)),
            "Podmieniony NIP powinien unieważnić podpis.");
    }

    [Fact]
    public void CertyfikatBezKluczaPrywatnegoJestOdrzucany()
    {
        using X509Certificate2 zKluczem = CertyfikatTestowy.DlaFirmy(Nip);
        using var bezKlucza = X509CertificateLoader.LoadCertificate(zKluczem.RawData);

        BladKsefException blad = Assert.Throws<BladKsefException>(() =>
            PodpisXades.Zloz(ZadanieUwierzytelnienia.Zbuduj("20260808-CR-ABC", Nip), bezKlucza));

        Assert.Contains("klucza prywatnego", blad.Message, StringComparison.Ordinal);
    }

    /// <summary>Cała droga uwierzytelnienia certyfikatem, aż do tokena dostępowego.</summary>
    [Fact]
    public async Task UwierzytelnienieCertyfikatemDochodziDoKonca()
    {
        using var atrapa = new AtrapaKsef();
        using X509Certificate2 certyfikat = CertyfikatTestowy.DlaFirmy(Nip);

        KlientKsef klient = atrapa.UtworzKlienta();
        await klient.UwierzytelnijCertyfikatemAsync(Nip, certyfikat);

        Assert.True(atrapa.PodpisPoprawny, "Atrapa nie uznała podpisu.");
        Assert.Equal(AtrapaKsef.Wyzwanie, atrapa.PodpisaneWyzwanie);
        Assert.Equal(Nip, atrapa.PodpisanyNip);

        // Dowód, że doszliśmy do końca: bez tokena dostępowego sesji nie da
        // się otworzyć.
        Assert.Equal(AtrapaKsef.NumerSesji, await klient.OtworzSesjeAsync());
    }

    /// <summary>Sprawdza podpis tak, jak zrobi to KSeF - kluczem z dokumentu.</summary>
    private static bool Sprawdz(string podpisanyXml)
    {
        var dokument = new XmlDocument { PreserveWhitespace = true };
        dokument.LoadXml(podpisanyXml);

        var podpis = new SignedXml(dokument);
        podpis.LoadXml((XmlElement)dokument
            .GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);

        return podpis.CheckSignature();
    }
}
