namespace FirmaPro.Domena;

/// <summary>
/// Rola podmiotu trzeciego na fakturze.
/// </summary>
/// <remarks>
/// Numery odpowiadają wprost wartościom pola Rola w strukturze FA(3) -
/// nie wolno ich zmieniać, bo trafiają do pliku wysyłanego do KSeF.
/// </remarks>
public enum RolaPodmiotu
{
    /// <summary>Faktor - gdy wierzytelność z faktury została sprzedana.</summary>
    Faktor = 1,

    /// <summary>
    /// Odbiorca - oddział albo jednostka wewnętrzna nabywcy.
    /// </summary>
    /// <remarks>
    /// Sam nie jest nabywcą w rozumieniu ustawy, ale to do niego trafia
    /// towar i to on bywa wskazany na dokumencie.
    /// </remarks>
    Odbiorca = 2,

    /// <summary>Podmiot pierwotny - przejęty lub przekształcony sprzedawca.</summary>
    PodmiotPierwotny = 3,

    /// <summary>Dodatkowy nabywca - kolejny kupujący obok wskazanego w Podmiot2.</summary>
    DodatkowyNabywca = 4,

    /// <summary>Wystawca faktury w imieniu podatnika - np. biuro rachunkowe.</summary>
    WystawcaFaktury = 5,

    /// <summary>Dokonujący płatności - reguluje zobowiązanie w miejsce nabywcy.</summary>
    DokonujacyPlatnosci = 6,

    /// <summary>Jednostka samorządu terytorialnego - wystawca.</summary>
    JstWystawca = 7,

    /// <summary>Jednostka samorządu terytorialnego - odbiorca.</summary>
    JstOdbiorca = 8,

    /// <summary>Członek grupy VAT - wystawca.</summary>
    CzlonekGrupyVatWystawca = 9,

    /// <summary>Członek grupy VAT - odbiorca.</summary>
    CzlonekGrupyVatOdbiorca = 10
}

/// <summary>
/// Podmiot trzeci związany z fakturą.
/// </summary>
/// <remarks>
/// <para>
/// Nie każda faktura ma dwie strony. Gmina wystawia dokument, ale towar
/// odbiera podległa jej jednostka; wierzytelność bywa sprzedana faktorowi;
/// fakturę w imieniu podatnika wystawia biuro rachunkowe; obok nabywcy
/// stoi drugi kupujący z własnym udziałem. Struktura FA(3) przewiduje na to
/// sekcję Podmiot3 - do stu wystąpień, każde z własną rolą.
/// </para>
/// <para>
/// Bez tej sekcji jednostka podrzędna samorządu w ogóle nie zobaczy faktury
/// w swoim KSeF: system udostępnia dokument właśnie na podstawie numeru NIP
/// podanego tutaj, z rolą 8.
/// </para>
/// </remarks>
public sealed class PodmiotInny
{
    /// <summary>Dane identyfikacyjne i adresowe podmiotu.</summary>
    public Podmiot Dane { get; set; } = new();

    /// <summary>
    /// Rola z listy przewidzianej przez strukturę.
    /// </summary>
    /// <remarks>
    /// Puste, gdy rola nie mieści się w liście - wtedy trzeba ją opisać
    /// słowami w <see cref="OpisRoli"/>. Schemat pozwala wybrać jedno albo
    /// drugie, nigdy obie naraz.
    /// </remarks>
    public RolaPodmiotu? Rola { get; set; }

    /// <summary>Opis roli, gdy nie ma jej na liście.</summary>
    public string? OpisRoli { get; set; }

    /// <summary>
    /// Procentowy udział dodatkowego nabywcy.
    /// </summary>
    /// <remarks>
    /// Ma sens wyłącznie przy roli „dodatkowy nabywca". Różnica między setką
    /// a sumą udziałów przypada nabywcy z sekcji Podmiot2; przy pustym polu
    /// udziały uznaje się za równe.
    /// </remarks>
    public decimal? Udzial { get; set; }

    /// <summary>Numer klienta, którym podmiot posługuje się w umowie.</summary>
    public string? NrKlienta { get; set; }

    /// <summary>Czy podmiot ma określoną rolę - własną albo z listy.</summary>
    public bool MaRole => Rola is not null || !string.IsNullOrWhiteSpace(OpisRoli);

    /// <summary>Nazwa roli do pokazania człowiekowi.</summary>
    public string NazwaRoli => Rola is { } rola
        ? Role.Nazwa(rola)
        : OpisRoli?.Trim() is { Length: > 0 } opis
            ? opis
            : "rola nieokreślona";
}

/// <summary>Nazwy ról podmiotów trzecich.</summary>
/// <remarks>
/// Brzmienie wzięte ze schematu, bo to ono pojawia się w aplikacji
/// Ministerstwa - użytkownik ma zobaczyć na wydruku to samo słowo,
/// które zobaczy po zalogowaniu do KSeF.
/// </remarks>
public static class Role
{
    public static string Nazwa(RolaPodmiotu rola) => rola switch
    {
        RolaPodmiotu.Faktor => "Faktor",
        RolaPodmiotu.Odbiorca => "Odbiorca",
        RolaPodmiotu.PodmiotPierwotny => "Podmiot pierwotny",
        RolaPodmiotu.DodatkowyNabywca => "Dodatkowy nabywca",
        RolaPodmiotu.WystawcaFaktury => "Wystawca faktury",
        RolaPodmiotu.DokonujacyPlatnosci => "Dokonujący płatności",
        RolaPodmiotu.JstWystawca => "Jednostka samorządu terytorialnego — wystawca",
        RolaPodmiotu.JstOdbiorca => "Jednostka samorządu terytorialnego — odbiorca",
        RolaPodmiotu.CzlonekGrupyVatWystawca => "Członek grupy VAT — wystawca",
        RolaPodmiotu.CzlonekGrupyVatOdbiorca => "Członek grupy VAT — odbiorca",
        _ => rola.ToString()
    };

    /// <summary>Wszystkie role w kolejności numerów ze schematu.</summary>
    public static IReadOnlyList<RolaPodmiotu> Wszystkie { get; } =
        Enum.GetValues<RolaPodmiotu>();
}
