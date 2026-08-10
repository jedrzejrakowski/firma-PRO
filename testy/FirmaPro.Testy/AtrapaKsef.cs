using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FirmaPro.Ksef;

namespace FirmaPro.Testy;

/// <summary>
/// Atrapa serwera KSeF używana w testach.
/// </summary>
/// <remarks>
/// Atrapa nie udaje odpowiedzi na ślepo - odszyfrowuje to, co wysyła klient:
/// token rozszyfrowuje kluczem prywatnym i porównuje z oczekiwanym ciągiem
/// "token|znacznik", klucz sesji rozszyfrowuje algorytmem RSA-OAEP, a fakturę
/// odszyfrowuje tym kluczem i porównuje z oryginałem razem ze skrótami
/// SHA-256 i długościami przesłanymi w żądaniu.
///
/// Dzięki temu test wykrywa błąd w kopercie kryptograficznej, a nie tylko
/// literówkę w nazwie pola.
/// </remarks>
public sealed class AtrapaKsef : HttpMessageHandler
{
    public const string NumerSesji = "20260808-SO-ABCDEF0000-1234567890-AB";
    public const string NumerFakturyRef = "20260808-EE-ABCDEF0000-1234567890-CD";
    public const string NumerKsef = "5252248481-20260808-010080DD2B5E-26";
    public const string TokenDostepu = "testowy.token.dostepowy";
    public const long ZnacznikMs = 1786000000000;

    private readonly RSA _kluczPrywatny;
    private readonly byte[] _certyfikatDer;
    private readonly string _oczekiwanyToken;
    private readonly byte[] _xmlFakturyZakupowej;

    public AtrapaKsef(string oczekiwanyToken = "TOKEN-KSEF-123",
                      byte[]? xmlFakturyZakupowej = null)
    {
        _oczekiwanyToken = oczekiwanyToken;
        _xmlFakturyZakupowej = xmlFakturyZakupowej ?? Encoding.UTF8.GetBytes("<Faktura/>");
        (_kluczPrywatny, _certyfikatDer) = ZbudujCertyfikat();
    }

    // Ślady wywołań - test może sprawdzić, co dokładnie zostało wysłane.
    public List<string> Wywolania { get; } = [];
    public string? OdszyfrowanyToken { get; private set; }
    public byte[]? KluczSesji { get; private set; }
    public byte[]? WektorSesji { get; private set; }
    public List<byte[]> OdebraneFaktury { get; } = [];
    public bool SesjaZamknieta { get; private set; }

    /// <summary>
    /// Faktury zakupowe zwracane przez zapytanie o metadane.
    /// </summary>
    /// <remarks>
    /// Test może podmienić zawartość, żeby sprawdzić zachowanie przy innych
    /// datach albo kwotach. Domyślnie jest jedna faktura - tyle wystarcza
    /// testom klienta, które sprawdzają samo odwzorowanie pól.
    /// </remarks>
    public List<MetadaneFaktury> FakturyZakupowe { get; } =
    [
        new MetadaneFaktury(
            "7010001453-20260807-0AAAAA-BBBBBB-CC",
            "FS/7/2026",
            new DateOnly(2026, 8, 7),
            new DateTimeOffset(2026, 8, 7, 9, 15, 0, TimeSpan.Zero),
            new SprzedawcaMetadanych("7010001453", "Dostawca sp. z o.o."),
            1000.00m,
            230.00m,
            1230.00m,
            "PLN")
    ];

    /// <summary>Tworzy samopodpisany certyfikat udający certyfikat KSeF.</summary>
    private static (RSA, byte[]) ZbudujCertyfikat()
    {
        RSA klucz = RSA.Create(2048);
        var zadanie = new CertificateRequest(
            "CN=Ministerstwo Finansów", klucz, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using X509Certificate2 certyfikat = zadanie.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        return (klucz, certyfikat.Export(X509ContentType.Cert));
    }

    private byte[] OdszyfrujRsa(string daneBase64) =>
        _kluczPrywatny.Decrypt(Convert.FromBase64String(daneBase64),
            RSAEncryptionPadding.OaepSHA256);

    private object Certyfikat(string przeznaczenie) => new
    {
        certificate = Convert.ToBase64String(_certyfikatDer),
        publicKeyId = "klucz-" + przeznaczenie,
        validFrom = "2025-01-01T00:00:00+00:00",
        validTo = "2030-01-01T00:00:00+00:00",
        usage = new[] { przeznaczenie }
    };

    // Nazwy parametrów muszą odpowiadać deklaracji z klasy bazowej.
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string sciezka = request.RequestUri!.AbsolutePath.Split("/v2/")[^1].TrimStart('/');
        string bezZapytania = sciezka.Split('?')[0];
        Wywolania.Add($"{request.Method} {bezZapytania}");

        string tresc = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return bezZapytania switch
        {
            "security/public-key-certificates" => Json(HttpStatusCode.OK,
                new[] { Certyfikat("KsefTokenEncryption"), Certyfikat("SymmetricKeyEncryption") }),

            "auth/challenge" => Json(HttpStatusCode.OK, new
            {
                challenge = "20260808-CR-ABC",
                timestamp = "2026-08-08T10:00:00+00:00",
                timestampMs = ZnacznikMs
            }),

            "auth/ksef-token" => ObsluzUwierzytelnienie(tresc),

            "auth/token/redeem" => Json(HttpStatusCode.OK, new
            {
                accessToken = new { token = TokenDostepu, validUntil = "2026-08-08T12:00:00+00:00" },
                refreshToken = new { token = "odswiezajacy", validUntil = "2026-08-09T10:00:00+00:00" }
            }),

            "sessions/online" => ObsluzOtwarcieSesji(tresc),

            _ => ObsluzPozostale(bezZapytania, tresc)
        };
    }

    private HttpResponseMessage ObsluzUwierzytelnienie(string tresc)
    {
        using JsonDocument dokument = JsonDocument.Parse(tresc);
        JsonElement korzen = dokument.RootElement;

        OdszyfrowanyToken = Encoding.UTF8.GetString(
            OdszyfrujRsa(korzen.GetProperty("encryptedToken").GetString()!));

        string oczekiwany = $"{_oczekiwanyToken}|{ZnacznikMs}";
        if (OdszyfrowanyToken != oczekiwany)
        {
            return Json(HttpStatusCode.Unauthorized, new
            {
                status = new { code = 401, description = "Błędny token KSeF" }
            });
        }

        if (korzen.GetProperty("contextIdentifier").GetProperty("type").GetString() != "Nip")
        {
            return Json(HttpStatusCode.BadRequest, new { title = "Zły typ kontekstu" });
        }

        return Json(HttpStatusCode.Accepted, new
        {
            referenceNumber = "20260808-AU-0001",
            authenticationToken = new
            {
                token = "tymczasowy.token.operacji",
                validUntil = "2026-08-08T11:00:00+00:00"
            }
        });
    }

    private HttpResponseMessage ObsluzOtwarcieSesji(string tresc)
    {
        using JsonDocument dokument = JsonDocument.Parse(tresc);
        JsonElement korzen = dokument.RootElement;
        JsonElement szyfrowanie = korzen.GetProperty("encryption");

        KluczSesji = OdszyfrujRsa(szyfrowanie.GetProperty("encryptedSymmetricKey").GetString()!);
        WektorSesji = Convert.FromBase64String(
            szyfrowanie.GetProperty("initializationVector").GetString()!);

        if (KluczSesji.Length != 32 || WektorSesji.Length != 16)
        {
            return Json(HttpStatusCode.BadRequest, new { title = "Zła długość klucza lub wektora" });
        }

        if (korzen.GetProperty("formCode").GetProperty("systemCode").GetString() != "FA (3)")
        {
            return Json(HttpStatusCode.BadRequest, new { title = "Nieobsługiwany wzór faktury" });
        }

        return Json(HttpStatusCode.Created, new
        {
            referenceNumber = NumerSesji,
            validUntil = "2026-08-08T22:00:00+00:00"
        });
    }

    private HttpResponseMessage ObsluzPozostale(string sciezka, string tresc)
    {
        // Odpytywanie o stan uwierzytelnienia - pozostałe ścieżki "auth/"
        // obsłużone są wcześniej, więc tu trafia już tylko sprawdzanie stanu.
        if (sciezka.StartsWith("auth/", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, new
            {
                startDate = "2026-08-08T10:00:00+00:00",
                status = new { code = 200, description = "Uwierzytelniono" }
            });
        }

        if (sciezka.EndsWith("/invoices", StringComparison.Ordinal) && tresc.Length > 0)
        {
            return ObsluzWyslanieFaktury(tresc);
        }

        if (sciezka.EndsWith("/close", StringComparison.Ordinal))
        {
            SesjaZamknieta = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            };
        }

        if (sciezka.EndsWith("/upo", StringComparison.Ordinal))
        {
            return Tekst("<UPO>potwierdzenie odbioru</UPO>");
        }

        if (sciezka.StartsWith($"sessions/{NumerSesji}/invoices/", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, new
            {
                referenceNumber = NumerFakturyRef,
                ksefNumber = NumerKsef,
                invoiceNumber = "FV/2026/08/1",
                invoicingDate = "2026-08-08T10:05:00+00:00",
                status = new { code = 200, description = "Faktura przyjęta" }
            });
        }

        if (sciezka.StartsWith("invoices/query/metadata", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK,
                new { hasMore = false, invoices = FakturyZakupowe });
        }

        if (sciezka.StartsWith("invoices/ksef/", StringComparison.Ordinal))
        {
            return Tekst(Encoding.UTF8.GetString(_xmlFakturyZakupowej));
        }

        return Json(HttpStatusCode.NotFound, new { title = $"Nieznana ścieżka {sciezka}" });
    }

    private HttpResponseMessage ObsluzWyslanieFaktury(string tresc)
    {
        using JsonDocument dokument = JsonDocument.Parse(tresc);
        JsonElement korzen = dokument.RootElement;

        byte[] zaszyfrowana = Convert.FromBase64String(
            korzen.GetProperty("encryptedInvoiceContent").GetString()!);

        using Aes aes = Aes.Create();
        aes.Key = KluczSesji!;
        byte[] faktura = aes.DecryptCbc(zaszyfrowana, WektorSesji!, PaddingMode.PKCS7);

        // Sprawdzamy to, co realnie sprawdza KSeF: zgodność skrótów
        // i rozmiarów zadeklarowanych w żądaniu z faktyczną treścią.
        string skrotFaktury = Convert.ToBase64String(SHA256.HashData(faktura));
        string skrotSzyfrogramu = Convert.ToBase64String(SHA256.HashData(zaszyfrowana));

        if (korzen.GetProperty("invoiceHash").GetString() != skrotFaktury)
        {
            return Json(HttpStatusCode.BadRequest, new { title = "Błędny skrót faktury" });
        }

        if (korzen.GetProperty("invoiceSize").GetInt64() != faktura.Length)
        {
            return Json(HttpStatusCode.BadRequest, new { title = "Błędny rozmiar faktury" });
        }

        if (korzen.GetProperty("encryptedInvoiceHash").GetString() != skrotSzyfrogramu)
        {
            return Json(HttpStatusCode.BadRequest, new { title = "Błędny skrót szyfrogramu" });
        }

        OdebraneFaktury.Add(faktura);
        return Json(HttpStatusCode.Accepted, new { referenceNumber = NumerFakturyRef });
    }

    private static HttpResponseMessage Json(HttpStatusCode kod, object tresc) =>
        new(kod)
        {
            Content = new StringContent(JsonSerializer.Serialize(tresc),
                Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Tekst(string tresc) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(tresc, Encoding.UTF8, "application/xml")
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kluczPrywatny.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Tworzy klienta rozmawiającego z tą atrapą.</summary>
    public KlientKsef UtworzKlienta() => new(
        new HttpClient(this, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api-test.ksef.mf.gov.pl/v2/")
        },
        Domena.SrodowiskoKsef.Test,
        odstepOdpytywania: TimeSpan.Zero);
}
