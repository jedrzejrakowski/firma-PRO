using FirmaPro.Domena;

namespace FirmaPro.Dane.Encje;

/// <summary>
/// Encja należąca do konkretnej firmy (najemcy).
/// </summary>
/// <remarks>
/// Każda encja z tym interfejsem jest automatycznie odfiltrowana do firmy,
/// w kontekście której pracuje użytkownik. Dodanie nowej tabeli z danymi
/// klienta bez tego interfejsu byłoby luką - dlatego test sprawdza, czy
/// wszystkie encje poza słownikowymi go implementują.
/// </remarks>
public interface INalezyDoFirmy
{
    Guid FirmaId { get; set; }
}

/// <summary>Wspólne pola śledzenia zmian.</summary>
public abstract class EncjaBazowa
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Kiedy rekord powstał (czas UTC).</summary>
    public DateTimeOffset UtworzonoUtc { get; set; }

    /// <summary>Kiedy rekord był ostatnio zmieniany (czas UTC).</summary>
    public DateTimeOffset? ZmienionoUtc { get; set; }
}

/// <summary>
/// Firma korzystająca z systemu - jednocześnie najemca w rozumieniu
/// wielofirmowości i wystawca faktur.
/// </summary>
public sealed class Firma : EncjaBazowa
{
    public string Nazwa { get; set; } = string.Empty;
    public string Nip { get; set; } = string.Empty;

    public string KodKraju { get; set; } = "PL";
    public string AdresLinia1 { get; set; } = string.Empty;
    public string? AdresLinia2 { get; set; }

    public string? Email { get; set; }
    public string? Telefon { get; set; }

    public string? RachunekBankowy { get; set; }
    public string? NazwaBanku { get; set; }

    public string? MiejsceWystawienia { get; set; }
    public string? StopkaFaktury { get; set; }

    /// <summary>Domyślny termin płatności liczony w dniach od wystawienia.</summary>
    public int DomyslnyTerminPlatnosciDni { get; set; } = 14;

    /// <summary>Środowisko KSeF: test, demo albo produkcja.</summary>
    public SrodowiskoKsef Srodowisko { get; set; } = SrodowiskoKsef.Test;

    /// <summary>
    /// Token KSeF zaszyfrowany kluczem aplikacji.
    /// </summary>
    /// <remarks>
    /// Token pozwala wystawiać faktury w imieniu firmy, więc nigdy nie jest
    /// zapisywany otwartym tekstem. Szyfrowaniem zajmuje się warstwa wyżej -
    /// tutaj trzymamy wyłącznie szyfrogram.
    /// </remarks>
    public byte[]? TokenKsefZaszyfrowany { get; set; }

    public bool Aktywna { get; set; } = true;

    public ICollection<CzlonkostwoWFirmie> Czlonkowie { get; set; } = [];
}

/// <summary>Konto użytkownika systemu.</summary>
public sealed class Uzytkownik : EncjaBazowa
{
    public string Email { get; set; } = string.Empty;

    /// <summary>Skrót hasła - nigdy samo hasło.</summary>
    public string HaszHasla { get; set; } = string.Empty;

    public string? ImieINazwisko { get; set; }
    public bool Aktywny { get; set; } = true;

    public ICollection<CzlonkostwoWFirmie> Czlonkostwa { get; set; } = [];
}

/// <summary>Rola użytkownika w firmie.</summary>
public enum RolaWFirmie
{
    /// <summary>Podgląd bez prawa zmiany.</summary>
    Podglad,
    /// <summary>Wystawianie faktur i prowadzenie kartotek.</summary>
    Ksiegowy,
    /// <summary>Pełne prawa, w tym ustawienia firmy i KSeF.</summary>
    Wlasciciel
}

/// <summary>
/// Powiązanie użytkownika z firmą. Jeden użytkownik może obsługiwać wiele
/// firm - tak pracują biura rachunkowe.
/// </summary>
public sealed class CzlonkostwoWFirmie : EncjaBazowa
{
    public Guid UzytkownikId { get; set; }
    public Uzytkownik? Uzytkownik { get; set; }

    public Guid FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public RolaWFirmie Rola { get; set; } = RolaWFirmie.Ksiegowy;
}

/// <summary>Kontrahent - nabywca na fakturze.</summary>
public sealed class Kontrahent : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public string Nazwa { get; set; } = string.Empty;
    public string Nip { get; set; } = string.Empty;

    public string KodKraju { get; set; } = "PL";
    public string AdresLinia1 { get; set; } = string.Empty;
    public string? AdresLinia2 { get; set; }

    public string? Email { get; set; }
    public string? Telefon { get; set; }

    public string? KodUe { get; set; }
    public string? NrVatUe { get; set; }

    public bool JednostkaPodrzednaJst { get; set; }
    public bool CzlonekGrupyVat { get; set; }

    public bool Aktywny { get; set; } = true;

    /// <summary>Buduje podmiot dziedzinowy na potrzeby wystawienia faktury.</summary>
    public Podmiot NaPodmiot() => new()
    {
        Nazwa = Nazwa,
        Nip = Nip,
        Adres = new Adres
        {
            KodKraju = KodKraju,
            Linia1 = AdresLinia1,
            Linia2 = AdresLinia2
        },
        Email = Email,
        Telefon = Telefon,
        KodUe = KodUe,
        NrVatUe = NrVatUe,
        JednostkaPodrzednaJst = JednostkaPodrzednaJst,
        CzlonekGrupyVat = CzlonekGrupyVat
    };
}

/// <summary>Stan faktury w obiegu z KSeF.</summary>
public enum StatusKsef
{
    /// <summary>Wystawiona w systemie, jeszcze nie wysłana.</summary>
    Robocza,
    /// <summary>Wysłana, czeka na wynik weryfikacji.</summary>
    Wyslana,
    /// <summary>Przyjęta - nadany numer KSeF.</summary>
    Przyjeta,
    /// <summary>Odrzucona przez system.</summary>
    Odrzucona
}

/// <summary>Faktura sprzedaży wystawiona przez firmę.</summary>
public sealed class FakturaSprzedazy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public string Numer { get; set; } = string.Empty;

    public DateOnly DataWystawienia { get; set; }
    public DateOnly? DataSprzedazy { get; set; }
    public string? MiejsceWystawienia { get; set; }

    public string Waluta { get; set; } = "PLN";
    public RodzajFaktury Rodzaj { get; set; } = RodzajFaktury.Vat;

    public Guid KontrahentId { get; set; }
    public Kontrahent? Kontrahent { get; set; }

    /// <summary>
    /// Dane nabywcy przepisane w chwili wystawienia.
    /// </summary>
    /// <remarks>
    /// Faktura musi pokazywać dane aktualne w dniu wystawienia. Gdyby
    /// odczytywać je z kartoteki kontrahenta, zmiana adresu wstecznie
    /// zmieniłaby treść wystawionych już dokumentów.
    /// </remarks>
    public string NabywcaNazwa { get; set; } = string.Empty;
    public string NabywcaNip { get; set; } = string.Empty;
    public string NabywcaKodKraju { get; set; } = "PL";
    public string NabywcaAdresLinia1 { get; set; } = string.Empty;
    public string? NabywcaAdresLinia2 { get; set; }
    public string? NabywcaKodUe { get; set; }
    public string? NabywcaNrVatUe { get; set; }
    public bool NabywcaJst { get; set; }
    public bool NabywcaGrupaVat { get; set; }

    public FormaPlatnosci? FormaPlatnosci { get; set; } = Domena.FormaPlatnosci.Przelew;
    public DateOnly? TerminPlatnosci { get; set; }
    public string? RachunekBankowy { get; set; }
    public string? NazwaBanku { get; set; }
    public bool Zaplacono { get; set; }
    public DateOnly? DataZaplaty { get; set; }

    public string? Stopka { get; set; }
    public string? PodstawaZwolnienia { get; set; }

    /// <summary>Sumy zapisane w chwili wystawienia - do zestawień i rejestru VAT.</summary>
    public decimal RazemNetto { get; set; }
    public decimal RazemVat { get; set; }
    public decimal RazemBrutto { get; set; }

    public StatusKsef Status { get; set; } = StatusKsef.Robocza;
    public string? NumerKsef { get; set; }
    public DateTimeOffset? DataPrzyjeciaKsef { get; set; }
    public string? UwagiKsef { get; set; }

    /// <summary>Skrót SHA-256 wysłanego pliku - potrzebny do kodu QR.</summary>
    public string? SkrotXml { get; set; }

    public ICollection<PozycjaFakturySprzedazy> Pozycje { get; set; } = [];

    /// <summary>
    /// Czy dokument jest już zamknięty na zmiany.
    /// </summary>
    /// <remarks>
    /// Faktura przyjęta przez KSeF nie może być zmieniona - błąd koryguje się
    /// fakturą korygującą. Pilnuje tego kontekst bazy przy zapisie.
    /// </remarks>
    public bool CzyZamknieta => Status is StatusKsef.Przyjeta or StatusKsef.Wyslana;
}

/// <summary>Pozycja faktury sprzedaży.</summary>
public sealed class PozycjaFakturySprzedazy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid FakturaId { get; set; }
    public FakturaSprzedazy? Faktura { get; set; }

    /// <summary>Numer porządkowy na fakturze, liczony od 1.</summary>
    public int NrWiersza { get; set; }

    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal Ilosc { get; set; } = 1m;
    public decimal CenaNetto { get; set; }

    /// <summary>Kod stawki zgodny ze schematem FA(3), np. "23" albo "0 WDT".</summary>
    public string KodStawki { get; set; } = StawkaVat.Vat23.Kod;

    public string? Gtu { get; set; }
    public string? Pkwiu { get; set; }
    public string? Cn { get; set; }
    public string? Indeks { get; set; }

    /// <summary>Wartości wyliczone i zapisane w chwili wystawienia.</summary>
    public decimal WartoscNetto { get; set; }
    public decimal KwotaVat { get; set; }
}

/// <summary>
/// Seria numeracji faktur w obrębie firmy.
/// </summary>
/// <remarks>
/// Numery faktur muszą tworzyć ciąg bez luk i powtórzeń. Ostatni nadany numer
/// trzymamy w bazie, a niepowtarzalność wymusza dodatkowo indeks unikalny na
/// parze (firma, numer) - to on jest ostateczną gwarancją, nawet gdyby dwie
/// osoby wystawiały fakturę w tej samej chwili.
/// </remarks>
public sealed class SeriaNumeracji : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public string Nazwa { get; set; } = "Sprzedaż";

    /// <summary>
    /// Wzór numeru. Obsługiwane pola: {NR}, {ROK}, {MC}.
    /// Przykład: "FV/{ROK}/{MC}/{NR}".
    /// </summary>
    public string Wzor { get; set; } = "FV/{ROK}/{MC}/{NR}";

    /// <summary>Rok, którego dotyczy licznik - numeracja zeruje się co roku.</summary>
    public int Rok { get; set; }

    /// <summary>Miesiąc licznika; 0 gdy numeracja jest roczna.</summary>
    public int Miesiac { get; set; }

    public int OstatniNumer { get; set; }

    public bool Domyslna { get; set; } = true;
}
