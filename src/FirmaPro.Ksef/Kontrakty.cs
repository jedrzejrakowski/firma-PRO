using System.Text.Json.Serialization;

namespace FirmaPro.Ksef;

// Kontrakty odwzorowują specyfikację OpenAPI opublikowaną przez Ministerstwo
// Finansów (repozytorium CIRFMF/ksef-docs). Nazwy pól w JSON są w camelCase,
// dlatego przy każdym podana jest jawnie - to zabezpiecza przed zmianą
// domyślnych ustawień serializacji w przyszłości.

/// <summary>Odpowiedź na żądanie wyzwania uwierzytelniającego.</summary>
public sealed record OdpowiedzWyzwania(
    [property: JsonPropertyName("challenge")] string Wyzwanie,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Znacznik,
    [property: JsonPropertyName("timestampMs")] long ZnacznikMs);

/// <summary>Certyfikat klucza publicznego udostępniany przez KSeF.</summary>
public sealed record CertyfikatPubliczny(
    [property: JsonPropertyName("certificate")] string Certyfikat,
    [property: JsonPropertyName("publicKeyId")] string IdentyfikatorKlucza,
    [property: JsonPropertyName("validFrom")] DateTimeOffset WaznyOd,
    [property: JsonPropertyName("validTo")] DateTimeOffset WaznyDo,
    [property: JsonPropertyName("usage")] IReadOnlyList<string> Przeznaczenie);

/// <summary>Identyfikator kontekstu, w którym następuje uwierzytelnienie.</summary>
public sealed record IdentyfikatorKontekstu(
    [property: JsonPropertyName("type")] string Typ,
    [property: JsonPropertyName("value")] string Wartosc);

/// <summary>Żądanie uwierzytelnienia tokenem KSeF.</summary>
public sealed record ZadanieTokenem(
    [property: JsonPropertyName("challenge")] string Wyzwanie,
    [property: JsonPropertyName("contextIdentifier")] IdentyfikatorKontekstu Kontekst,
    [property: JsonPropertyName("encryptedToken")] string ZaszyfrowanyToken,
    [property: JsonPropertyName("publicKeyId")] string IdentyfikatorKlucza);

/// <summary>Token wraz z terminem ważności.</summary>
public sealed record TokenZTerminem(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("validUntil")] DateTimeOffset WaznyDo);

/// <summary>Odpowiedź rozpoczynająca operację uwierzytelnienia.</summary>
public sealed record OdpowiedzUwierzytelnienia(
    [property: JsonPropertyName("referenceNumber")] string NumerReferencyjny,
    [property: JsonPropertyName("authenticationToken")] TokenZTerminem TokenOperacji);

/// <summary>Stan operacji zwracany przez API.</summary>
public sealed record StanOperacji(
    [property: JsonPropertyName("code")] int Kod,
    [property: JsonPropertyName("description")] string? Opis,
    [property: JsonPropertyName("details")] IReadOnlyList<string>? Szczegoly);

/// <summary>Odpowiedź o stanie procesu uwierzytelniania.</summary>
public sealed record OdpowiedzStanuUwierzytelnienia(
    [property: JsonPropertyName("status")] StanOperacji Status);

/// <summary>Para tokenów wydana po pomyślnym uwierzytelnieniu.</summary>
public sealed record OdpowiedzTokenow(
    [property: JsonPropertyName("accessToken")] TokenZTerminem TokenDostepu,
    [property: JsonPropertyName("refreshToken")] TokenZTerminem TokenOdswiezajacy);

/// <summary>Kod formularza określający wersję wzoru faktury.</summary>
public sealed record KodFormularza(
    [property: JsonPropertyName("systemCode")] string KodSystemowy,
    [property: JsonPropertyName("schemaVersion")] string WersjaSchemy,
    [property: JsonPropertyName("value")] string Wartosc);

/// <summary>Informacje o szyfrowaniu przekazywane przy otwarciu sesji.</summary>
public sealed record InformacjeSzyfrowania(
    [property: JsonPropertyName("encryptedSymmetricKey")] string ZaszyfrowanyKlucz,
    [property: JsonPropertyName("initializationVector")] string WektorInicjujacy,
    [property: JsonPropertyName("publicKeyId")] string IdentyfikatorKlucza);

/// <summary>Żądanie otwarcia sesji interaktywnej.</summary>
public sealed record ZadanieOtwarciaSesji(
    [property: JsonPropertyName("formCode")] KodFormularza KodFormularza,
    [property: JsonPropertyName("encryption")] InformacjeSzyfrowania Szyfrowanie);

/// <summary>Odpowiedź z numerem otwartej sesji.</summary>
public sealed record OdpowiedzSesji(
    [property: JsonPropertyName("referenceNumber")] string NumerReferencyjny,
    [property: JsonPropertyName("validUntil")] DateTimeOffset WaznaDo);

/// <summary>Żądanie wysłania pojedynczej faktury.</summary>
public sealed record ZadanieWyslaniaFaktury(
    [property: JsonPropertyName("invoiceHash")] string SkrotFaktury,
    [property: JsonPropertyName("invoiceSize")] long RozmiarFaktury,
    [property: JsonPropertyName("encryptedInvoiceHash")] string SkrotSzyfrogramu,
    [property: JsonPropertyName("encryptedInvoiceSize")] long RozmiarSzyfrogramu,
    [property: JsonPropertyName("encryptedInvoiceContent")] string TrescSzyfrogramu);

/// <summary>Odpowiedź z numerem referencyjnym przyjętego dokumentu.</summary>
public sealed record OdpowiedzWyslania(
    [property: JsonPropertyName("referenceNumber")] string NumerReferencyjny);

/// <summary>Stan weryfikacji faktury w ramach sesji.</summary>
public sealed record OdpowiedzStanuFaktury(
    [property: JsonPropertyName("referenceNumber")] string NumerReferencyjny,
    [property: JsonPropertyName("ksefNumber")] string? NumerKsef,
    [property: JsonPropertyName("invoiceNumber")] string? NumerFaktury,
    [property: JsonPropertyName("invoicingDate")] DateTimeOffset? DataPrzyjecia,
    [property: JsonPropertyName("status")] StanOperacji Status);

/// <summary>Zakres dat w zapytaniu o listę faktur.</summary>
public sealed record ZakresDat(
    [property: JsonPropertyName("dateType")] string TypDaty,
    [property: JsonPropertyName("from")] DateTimeOffset Od,
    [property: JsonPropertyName("to")] DateTimeOffset Do);

/// <summary>Zapytanie o metadane faktur.</summary>
public sealed record ZapytanieOFaktury(
    [property: JsonPropertyName("subjectType")] string TypPodmiotu,
    [property: JsonPropertyName("dateRange")] ZakresDat Zakres);

/// <summary>Dane sprzedawcy w metadanych faktury.</summary>
public sealed record SprzedawcaMetadanych(
    [property: JsonPropertyName("nip")] string? Nip,
    [property: JsonPropertyName("name")] string? Nazwa);

/// <summary>Metadane pojedynczej faktury zwrócone przez zapytanie.</summary>
public sealed record MetadaneFaktury(
    [property: JsonPropertyName("ksefNumber")] string NumerKsef,
    [property: JsonPropertyName("invoiceNumber")] string? NumerFaktury,
    [property: JsonPropertyName("issueDate")] DateOnly? DataWystawienia,
    [property: JsonPropertyName("invoicingDate")] DateTimeOffset? DataPrzyjecia,
    [property: JsonPropertyName("seller")] SprzedawcaMetadanych? Sprzedawca,
    [property: JsonPropertyName("netAmount")] decimal Netto,
    [property: JsonPropertyName("vatAmount")] decimal Vat,
    [property: JsonPropertyName("grossAmount")] decimal Brutto,
    [property: JsonPropertyName("currency")] string? Waluta);

/// <summary>Strona wyników zapytania o faktury.</summary>
public sealed record OdpowiedzListyFaktur(
    [property: JsonPropertyName("hasMore")] bool CzyWiecej,
    [property: JsonPropertyName("invoices")] IReadOnlyList<MetadaneFaktury>? Faktury);
