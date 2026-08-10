using FirmaPro.Domena;

namespace FirmaPro.Jpk;

/// <summary>
/// Dane podatnika i pliku, których nie ma w rejestrze.
/// </summary>
/// <remarks>
/// Nagłówek JPK wymaga informacji niezwiązanych z żadną fakturą: kodu urzędu
/// skarbowego, celu złożenia i danych kontaktowych. Zebrane są tu razem,
/// żeby generator nie sięgał po nie do bazy.
/// </remarks>
public sealed record DanePliku
{
    /// <summary>NIP podatnika - bez kresek i spacji.</summary>
    public required string Nip { get; init; }

    /// <summary>Pełna nazwa podatnika.</summary>
    public required string Nazwa { get; init; }

    /// <summary>
    /// Czterocyfrowy kod urzędu skarbowego, do którego trafia plik.
    /// </summary>
    /// <remarks>
    /// Kody publikuje Ministerstwo Finansów; różnią się dla osób fizycznych
    /// i pozostałych podatników w tym samym mieście.
    /// </remarks>
    public required string KodUrzedu { get; init; }

    /// <summary>Adres e-mail podatnika - nagłówek wymaga go do kontaktu.</summary>
    public string? Email { get; init; }

    public string? Telefon { get; init; }

    public CelZlozenia Cel { get; init; } = CelZlozenia.Pierwotny;

    /// <summary>Chwila wytworzenia pliku; podawana, żeby dało się ją ustalić w testach.</summary>
    public DateTimeOffset DataWytworzenia { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Nazwa programu wpisywana do nagłówka.</summary>
    public string NazwaSystemu { get; init; } = "Firma PRO";
}
