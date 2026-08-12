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
    /// Rytm rozliczania VAT - miesięczny albo kwartalny.
    /// </summary>
    /// <remarks>
    /// Decyduje o podziale rejestru na okresy oraz o tym, ile okresów zostaje
    /// na odliczenie podatku z faktury zakupu (art. 86 ust. 11).
    /// </remarks>
    public TypOkresu TypOkresuVat { get; set; } = TypOkresu.Miesieczny;

    /// <summary>
    /// Czterocyfrowy kod urzędu skarbowego, do którego trafia JPK_V7.
    /// </summary>
    /// <remarks>
    /// Kody publikuje Ministerstwo Finansów. W tym samym mieście urząd dla
    /// osób fizycznych ma inny kod niż dla pozostałych podatników, więc nie
    /// da się go wyprowadzić z adresu.
    /// </remarks>
    public string? KodUrzeduSkarbowego { get; set; }

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

    /// <summary>
    /// Zmienia się przy każdej zmianie hasła.
    /// </summary>
    /// <remarks>
    /// Wartość trafia do ciasteczka logowania i jest sprawdzana przy każdym
    /// żądaniu. Dzięki temu zmiana hasła wyrzuca wszystkie wcześniejsze
    /// sesje - bez tego ktoś, kto przejął cudze ciasteczko, pracowałby dalej
    /// mimo zmienionego hasła.
    /// </remarks>
    public Guid StempelBezpieczenstwa { get; set; } = Guid.NewGuid();

    public ICollection<CzlonkostwoWFirmie> Czlonkostwa { get; set; } = [];
}

/// <summary>
/// Jednorazowy odnośnik do ustawienia nowego hasła.
/// </summary>
/// <remarks>
/// Nie należy do żadnej firmy - hasło jest sprawą konta, a jedno konto może
/// pracować w wielu firmach.
/// </remarks>
public sealed class ResetHasla : EncjaBazowa
{
    public Guid UzytkownikId { get; set; }
    public Uzytkownik? Uzytkownik { get; set; }

    /// <summary>Losowy ciąg z odnośnika - jedyny dowód uprawnienia.</summary>
    public string Kod { get; set; } = string.Empty;

    public DateTimeOffset WaznoscDoUtc { get; set; }

    public DateTimeOffset? WykorzystanoUtc { get; set; }
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

/// <summary>
/// Zaproszenie współpracownika do firmy.
/// </summary>
/// <remarks>
/// Zaproszenie ma postać jednorazowego odnośnika: idzie pocztą, gdy jest
/// skonfigurowana, a poza tym właściciel przekazuje go, jak mu wygodnie.
/// Odnośnik jest wart tyle, co hasło, dlatego traci ważność i działa raz.
/// </remarks>
public sealed class Zaproszenie : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }
    public Firma? Firma { get; set; }

    /// <summary>Adres, dla którego zaproszenie zostało wystawione.</summary>
    public string Email { get; set; } = string.Empty;

    public RolaWFirmie Rola { get; set; } = RolaWFirmie.Ksiegowy;

    /// <summary>Losowy ciąg z odnośnika - jedyny dowód uprawnienia.</summary>
    public string Kod { get; set; } = string.Empty;

    public DateTimeOffset WaznoscDoUtc { get; set; }

    /// <summary>Kiedy zaproszenie zostało przyjęte; <c>null</c> - jeszcze nie.</summary>
    public DateTimeOffset? PrzyjeteUtc { get; set; }

    public Guid ZapraszajacyId { get; set; }
}

/// <summary>
/// Pozycja zamówienia zapisana przy fakturze zaliczkowej.
/// </summary>
/// <remarks>
/// Wiersze faktury zaliczkowej pokazują samą wpłatę, więc bez zamówienia nie
/// byłoby wiadomo, czego ta wpłata dotyczy (art. 106f ust. 1 pkt 4 ustawy).
/// Zamówienie zapisujemy przy dokumencie, a nie osobno: ma pokazywać stan
/// z dnia wystawienia, tak samo jak dane nabywcy.
/// </remarks>
public sealed class PozycjaZamowieniaFaktury : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid FakturaId { get; set; }
    public FakturaSprzedazy? Faktura { get; set; }

    public int NrWiersza { get; set; }

    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal Ilosc { get; set; } = 1m;
    public decimal CenaNetto { get; set; }
    public string KodStawki { get; set; } = "23";
    public string? Gtu { get; set; }
}

/// <summary>
/// Wskazanie faktury zaliczkowej rozliczanej fakturą końcową.
/// </summary>
/// <remarks>
/// Osobna tabela, bo faktura końcowa rozlicza zwykle kilka zaliczek
/// (art. 106f ust. 3 ustawy).
/// </remarks>
public sealed class RozliczonaZaliczka : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    /// <summary>Faktura końcowa.</summary>
    public Guid FakturaId { get; set; }
    public FakturaSprzedazy? Faktura { get; set; }

    /// <summary>Rozliczana faktura zaliczkowa.</summary>
    public Guid ZaliczkowaId { get; set; }

    /// <summary>Numer zaliczkowej przepisany na chwilę wystawienia.</summary>
    public string Numer { get; set; } = string.Empty;

    public DateOnly DataWystawienia { get; set; }

    public string? NumerKsef { get; set; }

    /// <summary>Kwota brutto rozliczanej zaliczki.</summary>
    public decimal Brutto { get; set; }
}

/// <summary>Wpłata na poczet faktury sprzedaży.</summary>
/// <remarks>
/// Osobne wpłaty, a nie samo pole „zapłacono": należność bywa regulowana
/// w ratach, a przy sporze liczy się, kiedy i ile wpłynęło. Pola
/// <c>Zaplacono</c> i <c>DataZaplaty</c> przy fakturze zostają jako
/// podsumowanie - to one trafiają na wydruk i do pliku FA(3) - i program
/// utrzymuje je sam na podstawie wpłat, żeby nie rozjechały się z prawdą.
/// </remarks>
public sealed class Platnosc : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid FakturaId { get; set; }
    public FakturaSprzedazy? Faktura { get; set; }

    public decimal Kwota { get; set; }

    /// <summary>Dzień, w którym pieniądze wpłynęły.</summary>
    public DateOnly Data { get; set; }

    public string? Uwagi { get; set; }
}

/// <summary>Co zostało wysłane kontrahentowi.</summary>
public enum RodzajWysylki
{
    /// <summary>Sama faktura.</summary>
    Faktura,

    /// <summary>Przypomnienie o niezapłaconej należności.</summary>
    Przypomnienie
}

/// <summary>
/// Ślad wysłania faktury pocztą do kontrahenta.
/// </summary>
/// <remarks>
/// Osobna tabela, a nie pole przy fakturze: faktura bywa wysyłana kilka razy
/// - pod poprawiony adres albo drugi raz, bo pierwsza wiadomość zaginęła.
/// Przy sporze o to, czy kontrahent fakturę dostał, liczy się cała historia,
/// a nie sam ostatni raz.
/// </remarks>
public sealed class WyslanieFaktury : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid FakturaId { get; set; }
    public FakturaSprzedazy? Faktura { get; set; }

    public string Adres { get; set; } = string.Empty;

    /// <summary>Kto wysłał - do odtworzenia przebiegu sprawy.</summary>
    public Guid? UzytkownikId { get; set; }

    public DateTimeOffset WyslanoUtc { get; set; }

    /// <summary>Czy dołączono plik XML obok wizualizacji PDF.</summary>
    public bool ZXmlem { get; set; }

    public RodzajWysylki Rodzaj { get; set; } = RodzajWysylki.Faktura;
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

    /// <summary>
    /// Data wskazująca okres rejestru VAT.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wyliczana przy wystawieniu z reguły ogólnej (art. 19a ust. 1: decyduje
    /// data sprzedaży), ale zapisana w bazie jako osobna kolumna. Rejestr może
    /// dzięki temu wybrać dokumenty jednym warunkiem po indeksie, zamiast
    /// wczytywać szerszy przedział i odsiewać go w pamięci - a takie odsiewanie
    /// po cichu gubiłoby dokumenty leżące poza przyjętym marginesem.
    /// </para>
    /// <para>
    /// Wartość można poprawić ręcznie przy wyjątkach - mediach i najmie, gdzie
    /// obowiązek podatkowy powstaje z chwilą wystawienia faktury. Zmiana nie
    /// dotyka treści dokumentu wysłanego do KSeF, tylko jego przypisania
    /// do okresu.
    /// </para>
    /// </remarks>
    public DateOnly DataUjeciaVat { get; set; }

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

    // --- korekta ---------------------------------------------------------

    /// <summary>Faktura, którą ten dokument koryguje.</summary>
    public Guid? FakturaKorygowanaId { get; set; }
    public FakturaSprzedazy? FakturaKorygowana { get; set; }

    /// <summary>
    /// Dane faktury korygowanej przepisane w chwili wystawienia korekty.
    /// </summary>
    /// <remarks>
    /// Tak samo jak dane nabywcy: dokument ma pozostać kompletny sam z siebie,
    /// nawet gdyby faktura pierwotna została kiedyś usunięta z bazy.
    /// </remarks>
    public string? KorygowanaNumer { get; set; }
    public DateOnly? KorygowanaDataWystawienia { get; set; }
    public string? KorygowanaNumerKsef { get; set; }

    public string? PrzyczynaKorekty { get; set; }

    /// <summary>Typ skutku korekty w ewidencji VAT.</summary>
    public TypKorektyVat? TypKorekty { get; set; }

    /// <summary>Czy dokument jest fakturą korygującą.</summary>
    public bool CzyKorekta =>
        Rodzaj is RodzajFaktury.Korygujaca
               or RodzajFaktury.KorektaZaliczkowej
               or RodzajFaktury.KorektaRozliczeniowej;

    public ICollection<PozycjaFakturySprzedazy> Pozycje { get; set; } = [];

    /// <summary>Pozycje zamówienia - tylko przy fakturze zaliczkowej.</summary>
    public ICollection<PozycjaZamowieniaFaktury> PozycjeZamowienia { get; set; } = [];

    /// <summary>Zaliczki rozliczane tą fakturą - tylko przy fakturze końcowej.</summary>
    public ICollection<RozliczonaZaliczka> RozliczoneZaliczki { get; set; } = [];

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

    /// <summary>
    /// Czy wiersz opisuje stan sprzed korekty.
    /// </summary>
    /// <remarks>
    /// Na fakturze korygującej pozycje wykazywane są dwukrotnie: raz w stanie
    /// sprzed zmiany, raz po niej. Do rejestru VAT wchodzi różnica między
    /// jednymi a drugimi.
    /// </remarks>
    public bool StanPrzed { get; set; }
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

/// <summary>
/// Faktura otrzymana od dostawcy, ujmowana w rejestrze zakupów.
/// </summary>
/// <remarks>
/// <para>
/// Zakup zapisujemy w rozbiciu na stawki, a nie na pozycje towarowe. Rejestr
/// VAT i deklaracja potrzebują wyłącznie kwot w stawkach; przepisywanie
/// wszystkich pozycji z cudzej faktury byłoby pracą, z której nic nie wynika.
/// Gdy dojdzie magazyn, pozycje trafią do niego jako osobne zagadnienie.
/// </para>
/// <para>
/// Osobno trzymamy trzy daty, bo każda znaczy co innego: data wystawienia jest
/// na dokumencie, data wpływu decyduje o najwcześniejszym możliwym odliczeniu,
/// a data ujęcia wskazuje okres, w którym podatek faktycznie odliczamy.
/// </para>
/// </remarks>
public sealed class FakturaZakupu : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }
    public Firma? Firma { get; set; }

    /// <summary>Numer nadany przez sprzedawcę - nie mamy nad nim kontroli.</summary>
    public string Numer { get; set; } = string.Empty;

    public DateOnly DataWystawienia { get; set; }

    /// <summary>Data wpływu faktury do nabywcy.</summary>
    public DateOnly DataWplywu { get; set; }

    /// <summary>
    /// Data powstania obowiązku podatkowego u sprzedawcy - zwykle data
    /// dostawy towaru albo wykonania usługi.
    /// </summary>
    public DateOnly DataObowiazkuPodatkowego { get; set; }

    /// <summary>Data wskazująca okres, w którym odliczany jest podatek.</summary>
    public DateOnly DataUjecia { get; set; }

    public Guid? KontrahentId { get; set; }
    public Kontrahent? Kontrahent { get; set; }

    /// <summary>
    /// Dane sprzedawcy przepisane z dokumentu.
    /// </summary>
    /// <remarks>
    /// Tak samo jak przy sprzedaży: dokument ma pokazywać dane z dnia
    /// wystawienia, a nie te, które akurat są w kartotece.
    /// </remarks>
    public string SprzedawcaNazwa { get; set; } = string.Empty;
    public string? SprzedawcaNip { get; set; }

    public string Waluta { get; set; } = "PLN";

    public RodzajZakupu Rodzaj { get; set; } = RodzajZakupu.TowaryIUslugi;

    /// <summary>
    /// Czy podatek z tej faktury podlega odliczeniu.
    /// </summary>
    /// <remarks>
    /// Zakup służący sprzedaży zwolnionej albo celom prywatnym trafia do
    /// rejestru, ale podatku z niego się nie odlicza.
    /// </remarks>
    public bool Odliczany { get; set; } = true;

    public decimal RazemNetto { get; set; }
    public decimal RazemVat { get; set; }
    public decimal RazemBrutto { get; set; }

    /// <summary>Numer nadany przez KSeF, gdy faktura pochodzi z systemu.</summary>
    public string? NumerKsef { get; set; }

    public string? Uwagi { get; set; }

    public ICollection<KwotaVatZakupu> Kwoty { get; set; } = [];
}

/// <summary>Kwoty faktury zakupu w jednej stawce podatku.</summary>
public sealed class KwotaVatZakupu : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid FakturaZakupuId { get; set; }
    public FakturaZakupu? Faktura { get; set; }

    /// <summary>Kod stawki zgodny ze schematem FA(3).</summary>
    public string KodStawki { get; set; } = "23";

    public decimal Netto { get; set; }
    public decimal Vat { get; set; }
}


/// <summary>
/// Zapamiętane rozliczenie okresu - to, co przechodzi na następny miesiąc.
/// </summary>
/// <remarks>
/// Deklaracja za dany okres potrzebuje nadwyżki podatku z okresu
/// poprzedniego. Program mógłby ją wyliczać w łańcuchu wstecz, ale wtedy
/// poprawka w starej fakturze po cichu zmieniałaby wszystkie późniejsze
/// deklaracje - także te już złożone w urzędzie. Dlatego kwota zostaje
/// zapisana w chwili zamknięcia okresu i od tego momentu się nie zmienia.
/// </remarks>
public sealed class ZamkniecieOkresuVat : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }
    public Firma? Firma { get; set; }

    public int Rok { get; set; }

    /// <summary>Miesiąc (1-12) albo kwartał (1-4), zależnie od rytmu rozliczeń.</summary>
    public int Numer { get; set; }

    public TypOkresu Typ { get; set; } = TypOkresu.Miesieczny;

    /// <summary>Nadwyżka przechodząca na następny okres, w pełnych złotych.</summary>
    public long NadwyzkaDoPrzeniesienia { get; set; }

    /// <summary>Podatek wpłacony do urzędu za ten okres, w pełnych złotych.</summary>
    public long PodatekDoWplaty { get; set; }

    public DateTimeOffset DataZamkniecia { get; set; }
}
