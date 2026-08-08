namespace FirmaPro.Domena;

/// <summary>Adres podmiotu (typ TAdres schematu FA(3)).</summary>
public sealed class Adres
{
    /// <summary>Dwuliterowy kod kraju wg ISO 3166-1.</summary>
    public string KodKraju { get; set; } = "PL";

    /// <summary>Ulica, numer domu i lokalu (pole AdresL1).</summary>
    public string Linia1 { get; set; } = string.Empty;

    /// <summary>Kod pocztowy i miejscowość (pole AdresL2, nieobowiązkowe).</summary>
    public string? Linia2 { get; set; }

    /// <summary>Adres w jednej linii - na wizualizacje i podsumowania.</summary>
    public string Jednolinijkowy =>
        string.Join(", ", new[] { Linia1, Linia2 }.Where(c => !string.IsNullOrWhiteSpace(c)));
}

/// <summary>
/// Sprzedawca (Podmiot1) albo nabywca (Podmiot2) na fakturze.
/// </summary>
public sealed class Podmiot
{
    public string Nazwa { get; set; } = string.Empty;

    /// <summary>
    /// NIP bez myślników i spacji. Pusty oznacza nabywcę bez identyfikatora
    /// podatkowego (osoba prywatna) - w pliku XML pojawi się wtedy znacznik
    /// BrakID.
    /// </summary>
    public string Nip { get; set; } = string.Empty;

    public Adres Adres { get; set; } = new();

    public string? Email { get; set; }
    public string? Telefon { get; set; }

    /// <summary>Prefiks kraju UE - dla kontrahentów wewnątrzwspólnotowych.</summary>
    public string? KodUe { get; set; }

    /// <summary>Numer VAT UE bez prefiksu kraju.</summary>
    public string? NrVatUe { get; set; }

    /// <summary>
    /// Znacznik jednostki podrzędnej samorządu, wprowadzony w FA(3).
    /// Obowiązkowy po stronie nabywcy - wartość "nie" też trzeba wypisać.
    /// </summary>
    public bool JednostkaPodrzednaJst { get; set; }

    /// <summary>Znacznik członka grupy VAT - również obowiązkowy w FA(3).</summary>
    public bool CzlonekGrupyVat { get; set; }

    /// <summary>True dla nabywcy bez NIP i bez numeru VAT UE.</summary>
    public bool BezIdentyfikatora =>
        string.IsNullOrWhiteSpace(Nip) && string.IsNullOrWhiteSpace(NrVatUe);
}
