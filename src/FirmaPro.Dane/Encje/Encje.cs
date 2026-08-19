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
    /// <summary>Sposób uwierzytelnienia w KSeF.</summary>
    public MetodaUwierzytelnieniaKsef MetodaUwierzytelnienia { get; set; }
        = MetodaUwierzytelnieniaKsef.Token;

    /// <summary>
    /// Certyfikat wraz z kluczem prywatnym, w postaci zaszyfrowanej.
    /// </summary>
    /// <remarks>
    /// Przechowywany tak samo jak token - kluczem aplikacji, nigdy otwartym
    /// tekstem. Klucz prywatny certyfikatu pozwala wystawiać faktury w imieniu
    /// firmy, więc jest równie wrażliwy.
    /// </remarks>
    public byte[]? CertyfikatKsefZaszyfrowany { get; set; }

    /// <summary>Odcisk certyfikatu - do rozpoznania go na ekranie.</summary>
    public string? CertyfikatOdcisk { get; set; }

    /// <summary>Nazwa wyróżniona posiadacza certyfikatu.</summary>
    public string? CertyfikatPodmiot { get; set; }

    /// <summary>
    /// Koniec ważności certyfikatu.
    /// </summary>
    /// <remarks>
    /// Token po prostu jest albo go nie ma; certyfikat przestaje działać
    /// po cichu w środku miesiąca. Data trzymana jest osobno, żeby dało się
    /// ostrzec zawczasu bez odszyfrowywania certyfikatu przy każdym ekranie.
    /// </remarks>
    public DateTimeOffset? CertyfikatWaznyDo { get; set; }

    public TypOkresu TypOkresuVat { get; set; } = TypOkresu.Miesieczny;

    /// <summary>
    /// Forma opodatkowania - rozstrzyga, jaką księgę firma prowadzi.
    /// </summary>
    /// <remarks>
    /// Przy skali i podatku liniowym księga przychodów i rozchodów, przy
    /// ryczałcie ewidencja przychodów. To dwa różne dokumenty, nie dwa widoki
    /// tego samego - dlatego program pokazuje ten, który firmie przysługuje.
    /// </remarks>
    public FormaOpodatkowania FormaOpodatkowania { get; set; } = FormaOpodatkowania.Skala;

    /// <summary>
    /// Domyślna stawka ryczałtu podpowiadana przy fakturze.
    /// </summary>
    /// <remarks>
    /// Podpowiedź, nie reguła: jedna działalność bywa opodatkowana kilkoma
    /// stawkami naraz, więc przy fakturze można ją zmienić.
    /// </remarks>
    public decimal StawkaRyczaltu { get; set; } = 8.5m;

    /// <summary>
    /// Czy zaliczki na podatek dochodowy płacone są kwartalnie.
    /// </summary>
    /// <remarks>
    /// Prawo do kwartalnych zaliczek mają mali podatnicy i rozpoczynający
    /// działalność (art. 44 ust. 3g ustawy o PIT). Wybór zgłasza się w zeznaniu
    /// rocznym, więc program go nie sprawdza - zapisuje to, co poda właściciel.
    /// </remarks>
    public bool ZaliczkiKwartalne { get; set; }

    /// <summary>
    /// Strata z lat ubiegłych pozostała do odliczenia.
    /// </summary>
    /// <remarks>
    /// Odlicza się ją od dochodu w ciągu pięciu kolejnych lat, przy czym
    /// w jednym roku nie więcej niż połowę straty - albo jednorazowo do
    /// 5 000 000 zł (art. 9 ust. 3 ustawy o PIT). Ile wolno odliczyć w tym
    /// roku, rozstrzyga podatnik; program przyjmuje podaną kwotę.
    /// </remarks>
    public decimal StrataDoOdliczenia { get; set; }

    /// <summary>Czy firma prowadzi księgę przychodów i rozchodów.</summary>
    public bool ProwadziKpir =>
        FormaOpodatkowania is FormaOpodatkowania.Skala or FormaOpodatkowania.Liniowy;

    /// <summary>Czy firma prowadzi ewidencję przychodów.</summary>
    public bool ProwadziRyczalt => FormaOpodatkowania == FormaOpodatkowania.Ryczalt;

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

/// <summary>
/// Powtarzalna pozycja faktury - towar albo usługa z cennika.
/// </summary>
/// <remarks>
/// <para>
/// Kartoteka istnieje po to, żeby nie przepisywać przy każdej fakturze tego
/// samego: nazwy, jednostki, ceny i stawki. Przy jednej fakturze to drobiazg,
/// przy dwudziestu miesięcznie decyduje o tym, czy programu chce się używać.
/// </para>
/// <para>
/// Pozycja z kartoteki jest wzorcem, a nie źródłem prawdy o wystawionym
/// dokumencie. Wiersz faktury dostaje kopię wartości w chwili wystawienia -
/// późniejsza podwyżka ceny w cenniku nie może zmienić faktury sprzed roku.
/// </para>
/// </remarks>
public sealed class PozycjaCennika : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal CenaNetto { get; set; }

    /// <summary>Kod stawki zgodny ze schematem FA(3), np. "23" albo "zw".</summary>
    public string KodStawki { get; set; } = "23";

    /// <summary>Oznaczenie GTU, gdy towar albo usługa go wymaga.</summary>
    public string? Gtu { get; set; }

    public string? Pkwiu { get; set; }
    public string? Cn { get; set; }

    /// <summary>Własny symbol pozycji - katalogowy albo magazynowy.</summary>
    public string? Indeks { get; set; }

    /// <summary>
    /// Czy pozycja ma się pokazywać przy wystawianiu faktury.
    /// </summary>
    /// <remarks>
    /// Wycofanego towaru nie kasujemy, tylko chowamy - inaczej zniknąłby
    /// z podpowiedzi razem z historią tego, co i po ile sprzedawano.
    /// </remarks>
    public bool Aktywna { get; set; } = true;
}

/// <summary>
/// Wzorzec faktury wystawianej cyklicznie.
/// </summary>
/// <remarks>
/// <para>
/// Abonament, stała obsługa, najem - dokumenty, które co miesiąc różnią się
/// wyłącznie datą i numerem. Wzorzec pamięta ich treść, żeby nie przepisywać
/// jej dwanaście razy w roku.
/// </para>
/// <para>
/// Wzorzec nie wystawia faktur sam z siebie. Program pokazuje, co czeka na
/// wystawienie, ale dokument powstaje dopiero po kliknięciu - faktura jest
/// dokumentem prawnym, którego po wysłaniu do KSeF nie da się wycofać,
/// a jedynie skorygować.
/// </para>
/// </remarks>
public sealed class WzorzecCykliczny : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    /// <summary>Nazwa własna wzorca - widoczna tylko w programie.</summary>
    public string Nazwa { get; set; } = string.Empty;

    public Guid KontrahentId { get; set; }
    public Kontrahent? Kontrahent { get; set; }

    public RytmFaktury Rytm { get; set; } = RytmFaktury.Miesiecznie;

    /// <summary>
    /// Dzień miesiąca, w którym wypada wystawienie.
    /// </summary>
    /// <remarks>
    /// Zero oznacza ostatni dzień miesiąca (<see cref="Cyklicznosc.OstatniDzien"/>).
    /// Dzień dłuższy od miesiąca jest przycinany - 31 w lutym to 28 albo 29.
    /// </remarks>
    public int DzienMiesiaca { get; set; } = 1;

    public int TerminPlatnosciDni { get; set; } = 14;

    public FormaPlatnosci? FormaPlatnosci { get; set; } = Domena.FormaPlatnosci.Przelew;

    /// <summary>Od kiedy wzorzec obowiązuje.</summary>
    public DateOnly Od { get; set; }

    /// <summary>Do kiedy obowiązuje; puste oznacza „bezterminowo”.</summary>
    public DateOnly? Do { get; set; }

    /// <summary>Data najbliższej faktury z tego wzorca.</summary>
    public DateOnly NastepneWystawienie { get; set; }

    /// <summary>Kiedy wystawiono z niego ostatnią fakturę.</summary>
    public DateOnly? OstatnieWystawienie { get; set; }

    public bool Aktywny { get; set; } = true;

    public List<PozycjaWzorca> Pozycje { get; } = [];
}

/// <summary>Wiersz wzorca faktury cyklicznej.</summary>
public sealed class PozycjaWzorca : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid WzorzecId { get; set; }
    public WzorzecCykliczny? Wzorzec { get; set; }

    public int NrWiersza { get; set; }

    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal Ilosc { get; set; } = 1m;
    public decimal CenaNetto { get; set; }
    public string KodStawki { get; set; } = "23";
    public string? Gtu { get; set; }
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

    /// <summary>
    /// Kurs, którym przeliczono fakturę na złote; puste przy fakturze w PLN.
    /// </summary>
    /// <remarks>
    /// Zapisany przy wystawieniu i nigdy później nie przeliczany. Kurs bierze
    /// się z konkretnego dnia (art. 31a ustawy) - odczytanie go ponownie
    /// tydzień później dałoby inną kwotę podatku niż ta, którą pokazuje
    /// wystawiony już dokument.
    /// </remarks>
    public decimal? KursWaluty { get; set; }

    /// <summary>Dzień tabeli, z której pochodzi kurs.</summary>
    public DateOnly? KursZDnia { get; set; }

    /// <summary>Numer tabeli NBP - dowód, skąd wzięto kurs.</summary>
    public string? KursTabela { get; set; }

    /// <summary>Czy faktura jest wystawiona w walucie innej niż złoty.</summary>
    public bool Walutowa =>
        !string.Equals(Waluta, "PLN", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Kurs do przeliczeń - jeden do jednego dla faktur złotowych.
    /// </summary>
    /// <remarks>
    /// Dzięki temu rejestr VAT i deklaracja mnożą zawsze, bez rozgałęziania
    /// kodu na „a jeśli to złotówki". Pominięty warunek w jednym z takich
    /// miejsc oznaczałby podatek policzony od kwoty w euro.
    /// </remarks>
    public decimal KursDoPrzeliczen => KursWaluty ?? 1m;

    /// <summary>
    /// Kwota podatku w złotych.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Faktura w obcej walucie musi wykazywać podatek także w złotych
    /// (art. 106e ust. 11 ustawy) - i to ta kwota trafia do rejestru VAT.
    /// </para>
    /// <para>
    /// Przeliczamy stawka po stawce, tak samo jak dokument wysyłany do KSeF
    /// (pola P_14_xW). Przeliczenie samej sumy potrafi dać wynik różniący się
    /// o grosz, a ekran, wydruk i plik muszą mówić dokładnie to samo -
    /// rozbieżność w takim miejscu kończy się telefonem od księgowej.
    /// </para>
    /// </remarks>
    public decimal PodatekWZlotych =>
        WedlugStawekWZlotych(RazemVat, p => p.KwotaVat);

    /// <summary>
    /// Wartość netto w złotych.
    /// </summary>
    /// <remarks>
    /// Przepisy każą wykazać w złotych sam podatek, ale to netto trafia
    /// do rejestru VAT jako podstawa opodatkowania i do przychodów firmy.
    /// Liczone tą samą drogą co podatek, żeby obie kwoty pochodziły
    /// z jednego rachunku.
    /// </remarks>
    public decimal NettoWZlotych =>
        WedlugStawekWZlotych(RazemNetto, p => p.WartoscNetto);

    /// <summary>
    /// Wartość brutto w złotych.
    /// </summary>
    /// <remarks>
    /// Suma dwóch przeliczonych kwot, a nie osobne przeliczenie brutto.
    /// Inaczej wiersz netto i VAT potrafiłby nie sumować się do brutto,
    /// i to dokładnie na oczach człowieka, który to sprawdza.
    /// </remarks>
    public decimal BruttoWZlotych => NettoWZlotych + PodatekWZlotych;

    /// <summary>
    /// Przelicza kwotę na złote stawka po stawce.
    /// </summary>
    /// <remarks>
    /// Tak samo jak dokument wysyłany do KSeF (pola P_14_xW). Przeliczenie
    /// samej sumy potrafi dać wynik różniący się o grosz, a ekran, wydruk
    /// i plik muszą mówić dokładnie to samo - rozbieżność w takim miejscu
    /// kończy się telefonem od księgowej.
    /// </remarks>
    /// <param name="suma">Kwota zapisana przy fakturze - używana, gdy pozycje
    /// nie zostały wczytane z bazy.</param>
    /// <param name="skladnik">Które pole pozycji sumować.</param>
    private decimal WedlugStawekWZlotych(decimal suma,
                                         Func<PozycjaFakturySprzedazy, decimal> skladnik) =>
        Pozycje.Count == 0
            ? Przeliczenie.NaZlote(suma, KursDoPrzeliczen)
            : Kwoty.Zaokraglij(Pozycje
                .GroupBy(p => p.KodStawki, StringComparer.Ordinal)
                .Sum(stawka => Przeliczenie.NaZlote(
                    stawka.Where(p => !p.StanPrzed).Sum(skladnik)
                    - stawka.Where(p => p.StanPrzed).Sum(skladnik),
                    KursDoPrzeliczen)));

    public RodzajFaktury Rodzaj { get; set; } = RodzajFaktury.Vat;

    /// <summary>
    /// Stawka ryczałtu przypisana do tej sprzedaży.
    /// </summary>
    /// <remarks>
    /// Puste u firm rozliczających się skalą albo liniowo - tam stawka nie ma
    /// znaczenia. Przy ryczałcie decyduje o tym, do której rubryki ewidencji
    /// trafi przychód, a więc i o kwocie podatku.
    /// </remarks>
    public decimal? StawkaRyczaltu { get; set; }

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

    /// <summary>
    /// Plik XML, który poszedł do KSeF - co do bajtu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bez niego program umiałby złożyć dokument o tej samej treści, ale nie
    /// ten sam dokument: XML niesie znacznik czasu wytworzenia, więc złożony
    /// ponownie ma inny skrót niż ten zapisany w KSeF. Pobrany plik nie
    /// zgadzałby się z systemem, a wizualizacja powstawałaby z wierszy bazy,
    /// nie z faktury, która naprawdę istnieje w obrocie.
    /// </para>
    /// <para>
    /// Faktury sprzed wprowadzenia tej kolumny mają tu wartość pustą -
    /// program wraca wtedy do składania dokumentu na nowo.
    /// </para>
    /// </remarks>
    public byte[]? XmlWyslany { get; set; }

    /// <summary>Czy zachowano plik przesłany do KSeF.</summary>
    public bool MaXmlWyslany => XmlWyslany is { Length: > 0 };

    // --- urzędowe poświadczenie odbioru -----------------------------------

    /// <summary>
    /// Numer sesji, w której faktura poszła do KSeF.
    /// </summary>
    /// <remarks>
    /// Zapisany wyłącznie po to, żeby dało się później pobrać UPO: adres
    /// poświadczenia zawiera numer sesji, a ta jest już wtedy zamknięta.
    /// Bez tej kolumny dowód doręczenia byłby nie do odzyskania.
    /// </remarks>
    public string? NumerSesjiKsef { get; set; }

    /// <summary>
    /// Urzędowe poświadczenie odbioru w postaci, w jakiej wydał je KSeF.
    /// </summary>
    /// <remarks>
    /// To jedyny dowód, że faktura weszła do obiegu prawnego - przy sporze
    /// albo kontroli liczy się ono, a nie wpis w naszej bazie. Trzymamy je
    /// nieprzerobione, bo poświadczenie jest podpisane i każda zmiana treści
    /// unieważniłaby podpis.
    /// </remarks>
    public string? UpoXml { get; set; }

    /// <summary>Kiedy program pobrał poświadczenie.</summary>
    public DateTimeOffset? DataUpoUtc { get; set; }

    /// <summary>Czy poświadczenie zostało już pobrane.</summary>
    public bool MaUpo => UpoXml is { Length: > 0 };

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

    /// <summary>Podmioty trzecie wskazane na fakturze.</summary>
    public ICollection<PodmiotInnyFaktury> PodmiotyInne { get; set; } = [];

    // --- podmiot upoważniony ----------------------------------------------

    /// <summary>
    /// Podmiot, który wystawił fakturę w imieniu podatnika.
    /// </summary>
    /// <remarks>
    /// Komornik, organ egzekucyjny albo przedstawiciel podatkowy. Trzymamy to
    /// kolumnami przy fakturze, a nie osobną tabelą, bo taki podmiot może być
    /// tylko jeden - tak stanowi schemat.
    /// </remarks>
    public RolaUpowaznionego? UpowaznionyRola { get; set; }

    public string? UpowaznionyNazwa { get; set; }
    public string? UpowaznionyNip { get; set; }
    public string? UpowaznionyKodKraju { get; set; }
    public string? UpowaznionyAdresLinia1 { get; set; }
    public string? UpowaznionyAdresLinia2 { get; set; }
    public string? UpowaznionyEmail { get; set; }
    public string? UpowaznionyTelefon { get; set; }

    /// <summary>Czy fakturę wystawił ktoś w imieniu podatnika.</summary>
    public bool MaUpowaznionego => UpowaznionyRola is not null;

    // --- dane sprzedawcy sprzed korekty ------------------------------------

    /// <summary>
    /// Nazwa i adres sprzedawcy w brzmieniu z faktury korygowanej.
    /// </summary>
    /// <remarks>
    /// Wypełniane tylko wtedy, gdy korekta poprawia dane samego sprzedawcy
    /// (art. 106j ust. 2 pkt 3 ustawy) - inaczej nie widać, co się zmieniło.
    /// </remarks>
    public string? SprzedawcaPrzedNazwa { get; set; }
    public string? SprzedawcaPrzedNip { get; set; }
    public string? SprzedawcaPrzedKodKraju { get; set; }
    public string? SprzedawcaPrzedAdresLinia1 { get; set; }
    public string? SprzedawcaPrzedAdresLinia2 { get; set; }

    /// <summary>Czy korekta poprawia dane sprzedawcy.</summary>
    public bool KorygujeDaneSprzedawcy => SprzedawcaPrzedNazwa is { Length: > 0 };

    /// <summary>
    /// Nazwa i adres nabywcy w brzmieniu z faktury korygowanej.
    /// </summary>
    /// <remarks>
    /// Ta sama sprawa co przy sprzedawcy, po drugiej stronie transakcji.
    /// Kolumnami, a nie tabelą: korekta danych dotyczy nabywcy z faktury,
    /// a ten jest jeden. Korygowanie danych dodatkowych nabywców schemat
    /// dopuszcza, ale to przypadek na tyle rzadki, że nie warto pod niego
    /// budować osobnej tabeli - obsługa jest w modelu i w pliku.
    /// </remarks>
    public string? NabywcaPrzedNazwa { get; set; }
    public string? NabywcaPrzedNip { get; set; }
    public string? NabywcaPrzedKodKraju { get; set; }
    public string? NabywcaPrzedAdresLinia1 { get; set; }
    public string? NabywcaPrzedAdresLinia2 { get; set; }

    /// <summary>Czy korekta poprawia dane nabywcy.</summary>
    public bool KorygujeDaneNabywcy => NabywcaPrzedNazwa is { Length: > 0 };

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
/// Podmiot trzeci wskazany na fakturze sprzedaży.
/// </summary>
/// <remarks>
/// Dane przepisane w chwili wystawienia, tak samo jak dane nabywcy - dokument
/// ma pozostać kompletny sam z siebie, nawet gdyby kartoteka się zmieniła.
/// </remarks>
public sealed class PodmiotInnyFaktury : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid FakturaId { get; set; }
    public FakturaSprzedazy? Faktura { get; set; }

    /// <summary>Kolejność na dokumencie, liczona od 1.</summary>
    public int NrKolejny { get; set; }

    public string Nazwa { get; set; } = string.Empty;
    public string? Nip { get; set; }
    public string KodKraju { get; set; } = "PL";
    public string? AdresLinia1 { get; set; }
    public string? AdresLinia2 { get; set; }
    public string? Email { get; set; }
    public string? Telefon { get; set; }

    /// <summary>Rola z listy przewidzianej strukturą; puste przy roli własnej.</summary>
    public RolaPodmiotu? Rola { get; set; }

    /// <summary>Opis roli, gdy nie ma jej na liście.</summary>
    public string? OpisRoli { get; set; }

    /// <summary>Procentowy udział dodatkowego nabywcy.</summary>
    public decimal? Udzial { get; set; }

    public string? NrKlienta { get; set; }
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

    /// <summary>
    /// Kolumna księgi, do której trafia ten koszt.
    /// </summary>
    /// <remarks>
    /// Rejestrowi VAT wystarczy podział na towary i środki trwałe, ale księga
    /// przychodów i rozchodów ma osobne kolumny na towary handlowe, koszty
    /// uboczne zakupu, wynagrodzenia i pozostałe wydatki. Bez tego wyboru
    /// wszystko lądowałoby w jednym worku i księgę trzeba by poprawiać ręcznie.
    /// </remarks>
    public KolumnaKpir KolumnaKpir { get; set; } = KolumnaKpir.PozostaleWydatki;

    /// <summary>
    /// Czy wydatek jest kosztem podatkowym.
    /// </summary>
    /// <remarks>
    /// Odliczenie VAT i koszt w podatku dochodowym to dwie różne sprawy.
    /// Reprezentacja nie jest kosztem, choć VAT bywa od niej do odliczenia;
    /// zakup dla celów prywatnych nie jest ani jednym, ani drugim.
    /// </remarks>
    public bool KosztPodatkowy { get; set; } = true;

    public decimal RazemNetto { get; set; }
    public decimal RazemVat { get; set; }
    public decimal RazemBrutto { get; set; }

    /// <summary>Numer nadany przez KSeF, gdy faktura pochodzi z systemu.</summary>
    public string? NumerKsef { get; set; }

    /// <summary>
    /// Plik XML faktury pobrany z KSeF.
    /// </summary>
    /// <remarks>
    /// Przy zakupach to jedyny dokument, jaki mamy - dostawca nie przysyła
    /// nam niczego poza tym, co włożył do systemu. Metadane z KSeF niosą same
    /// sumy, więc bez pliku nie da się pokazać ani pozycji, ani stawek, ani
    /// tego, za co właściwie zapłacono.
    /// </remarks>
    public byte[]? XmlKsef { get; set; }

    /// <summary>Czy zachowano plik faktury pobrany z KSeF.</summary>
    public bool MaXmlKsef => XmlKsef is { Length: > 0 };

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

/// <summary>
/// Zapis w księdze wprowadzony ręcznie.
/// </summary>
/// <remarks>
/// <para>
/// Nie wszystko, co trafia do księgi, jest fakturą. Amortyzacja, odsetki
/// od rachunku firmowego, sprzedaż wyposażenia, opłata skarbowa, ryczałt
/// za używanie samochodu - to zdarzenia bez faktury, a księga musi je znać,
/// bo bez nich dochód jest nieprawdziwy.
/// </para>
/// <para>
/// Zapisy ręczne stoją w osobnej tabeli, a nie wśród faktur, bo nie są
/// dokumentami sprzedaży ani zakupu: nie mają numeru KSeF, nie idą do JPK
/// i nie podlegają niezmienności dokumentu wysłanego do urzędu.
/// </para>
/// </remarks>
public sealed class ZapisKsiegi : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    /// <summary>Data zdarzenia gospodarczego - kolumna 2 księgi.</summary>
    public DateOnly Data { get; set; }

    /// <summary>Numer dowodu księgowego - kolumna 3.</summary>
    public string NumerDowodu { get; set; } = string.Empty;

    /// <summary>Nazwa kontrahenta - kolumna 4; puste przy zapisach własnych.</summary>
    public string? Kontrahent { get; set; }

    /// <summary>Adres kontrahenta - kolumna 5.</summary>
    public string? Adres { get; set; }

    /// <summary>Opis zdarzenia - kolumna 6.</summary>
    public string Opis { get; set; } = string.Empty;

    /// <summary>Kolumna, do której trafia kwota.</summary>
    public KolumnaKpir Kolumna { get; set; } = KolumnaKpir.PozostaleWydatki;

    /// <summary>
    /// Kwota zapisu.
    /// </summary>
    /// <remarks>
    /// Ujemna przy zmniejszeniu. Księga nie zna zapisów czerwonych, więc minus
    /// jest jedynym sposobem pokazania, że coś ubyło.
    /// </remarks>
    public decimal Kwota { get; set; }

    /// <summary>
    /// Stawka ryczałtu - tylko przy przychodach firmy na ryczałcie.
    /// </summary>
    /// <remarks>
    /// Przychód bez stawki nie da się opodatkować, więc przy ewidencji
    /// przychodów jest obowiązkowa. Przy kosztach nie ma znaczenia - ryczałt
    /// kosztów nie zna.
    /// </remarks>
    public decimal? StawkaRyczaltu { get; set; }

    /// <summary>Uwagi - kolumna 16.</summary>
    public string? Uwagi { get; set; }
}

/// <summary>
/// Środek trwały w ewidencji firmy.
/// </summary>
/// <remarks>
/// <para>
/// Zakup środka trwałego nie jest kosztem miesiąca zakupu - kosztem są odpisy
/// amortyzacyjne (art. 22 ust. 8 ustawy o PIT). Faktura za samochód nie wchodzi
/// więc do księgi, a wchodzą odpisy liczone z tej ewidencji.
/// </para>
/// <para>
/// Samych odpisów nie zapisujemy w bazie. Plan wynika w całości z danych środka
/// i przelicza się go za każdym razem: gdyby odpisy leżały osobno, poprawienie
/// wartości początkowej zostawiłoby stare kwoty i księga przestałaby zgadzać
/// się z ewidencją.
/// </para>
/// </remarks>
public sealed class SrodekTrwalyFirmy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public string Nazwa { get; set; } = string.Empty;

    /// <summary>Numer inwentarzowy - trafia do księgi jako numer dowodu.</summary>
    public string NumerInwentarzowy { get; set; } = string.Empty;

    /// <summary>
    /// Dzień przyjęcia do używania.
    /// </summary>
    /// <remarks>
    /// Nie dzień zakupu - amortyzacja zaczyna się od miesiąca następującego
    /// po miesiącu przyjęcia (art. 22h ust. 1 pkt 1).
    /// </remarks>
    public DateOnly DataPrzyjecia { get; set; }

    public decimal WartoscPoczatkowa { get; set; }

    public MetodaAmortyzacji Metoda { get; set; } = MetodaAmortyzacji.Liniowa;

    /// <summary>Roczna stawka z Wykazu stawek amortyzacyjnych, w procentach.</summary>
    public decimal StawkaRoczna { get; set; } = 20m;

    /// <summary>Współczynnik podwyższający - tylko przy metodzie degresywnej.</summary>
    public decimal Wspolczynnik { get; set; } = 2.0m;

    /// <summary>
    /// Górna granica wartości, od której odpis jest kosztem.
    /// </summary>
    /// <remarks>
    /// 150 000 zł dla samochodu osobowego, 225 000 zł dla elektrycznego
    /// (art. 23 ust. 1 pkt 4). Puste przy pozostałych środkach.
    /// </remarks>
    public decimal? LimitKosztu { get; set; }

    /// <summary>Dzień likwidacji albo sprzedaży - po nim odpisów już nie ma.</summary>
    public DateOnly? DataLikwidacji { get; set; }

    public string? Uwagi { get; set; }

    /// <summary>Model dziedziny, z którego liczy się plan odpisów.</summary>
    public SrodekTrwaly NaModel() => new()
    {
        Nazwa = Nazwa,
        NumerInwentarzowy = NumerInwentarzowy,
        DataPrzyjecia = DataPrzyjecia,
        WartoscPoczatkowa = WartoscPoczatkowa,
        Metoda = Metoda,
        StawkaRoczna = StawkaRoczna,
        Wspolczynnik = Wspolczynnik,
        LimitKosztu = LimitKosztu,
        DataLikwidacji = DataLikwidacji
    };
}

/// <summary>
/// Spis z natury zapisany w programie.
/// </summary>
/// <remarks>
/// Sporządzany obowiązkowo na koniec i na początek roku (§ 24 rozporządzenia
/// w sprawie prowadzenia księgi). Nie jest ani przychodem, ani kosztem - jest
/// stanem magazynu, który wchodzi do rozliczenia dopiero różnicą między
/// remanentem początkowym a końcowym.
/// </remarks>
public sealed class SpisZNaturyFirmy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    /// <summary>Dzień, na który sporządzono spis.</summary>
    public DateOnly Data { get; set; }

    /// <summary>Opis okoliczności - koniec roku, likwidacja, zmiana wspólnika.</summary>
    public string? Uwagi { get; set; }

    /// <summary>Czy spis jest zamknięty i nie podlega już zmianom.</summary>
    /// <remarks>
    /// Spis podpisuje się i przechowuje razem z księgą, więc po zamknięciu nie
    /// dopisuje się do niego pozycji. Poprawka to nowy spis, nie zmiana starego.
    /// </remarks>
    public bool Zamkniety { get; set; }

    public ICollection<PozycjaSpisuFirmy> Pozycje { get; set; } = [];

    /// <summary>Model dziedziny - do wyceny i rozliczenia rocznego.</summary>
    public SpisZNatury NaModel() => new(Data,
        [.. Pozycje
            .OrderBy(p => p.NrPozycji)
            .Select(p => new PozycjaSpisu(p.Nazwa, p.Jednostka, p.Ilosc,
                                          p.CenaJednostkowa, p.Wycena))]);
}

/// <summary>Pozycja spisu z natury.</summary>
public sealed class PozycjaSpisuFirmy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    public Guid SpisId { get; set; }
    public SpisZNaturyFirmy? Spis { get; set; }

    /// <summary>Numer porządkowy w arkuszu spisu.</summary>
    public int NrPozycji { get; set; }

    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal Ilosc { get; set; }
    public decimal CenaJednostkowa { get; set; }

    /// <summary>Sposób ustalenia ceny - przy kontroli trzeba go umieć podać.</summary>
    public SposobWyceny Wycena { get; set; } = SposobWyceny.CenaZakupu;
}

/// <summary>
/// Jak firma opłaca składki ZUS.
/// </summary>
/// <remarks>
/// Te ustawienia stoją osobno od danych firmy, bo zmieniają się w czasie -
/// ulga na start przechodzi w preferencyjne, preferencyjne w Mały ZUS Plus.
/// Data rozpoczęcia działalności pozwala programowi powiedzieć, kiedy jeden
/// tytuł się kończy, zamiast czekać, aż użytkownik sam zauważy.
/// </remarks>
public sealed class UstawieniaZusFirmy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    /// <summary>Tytuł, z jakiego opłacane są składki.</summary>
    public TytulUbezpieczenia Tytul { get; set; } = TytulUbezpieczenia.Pelny;

    /// <summary>Czy przedsiębiorca przystąpił do dobrowolnego chorobowego.</summary>
    public bool Chorobowe { get; set; } = true;

    /// <summary>Stopa składki wypadkowej - różna dla różnych płatników.</summary>
    public decimal StopaWypadkowa { get; set; } = StopyZus.WypadkowaMalyPlatnik;

    /// <summary>
    /// Zwolnienie z Funduszu Pracy ze względu na wiek.
    /// </summary>
    /// <remarks>
    /// Kobiety po 55. i mężczyźni po 60. roku życia (art. 104b ustawy
    /// o promocji zatrudnienia). Program nie zna daty urodzenia, więc pyta.
    /// </remarks>
    public bool BezFunduszuPracy { get; set; }

    /// <summary>Dzień rozpoczęcia działalności - od niego liczą się ulgi.</summary>
    public DateOnly? DataRozpoczecia { get; set; }

    /// <summary>
    /// Czy składki społeczne trafiają do kosztów zamiast odliczenia od dochodu.
    /// </summary>
    /// <remarks>
    /// Wybór należy do podatnika i <b>musi być konsekwentny</b> - ta sama
    /// składka nie może być jednocześnie kosztem i odliczeniem. Dlatego stoi
    /// przy zasadach opłacania składek, a nie przy pojedynczym miesiącu.
    /// </remarks>
    public bool SpoleczneWKosztach { get; set; }

    /// <summary>Dochód roku poprzedniego - podstawa Małego ZUS Plus.</summary>
    public decimal? DochodPoprzedniegoRoku { get; set; }

    /// <summary>Przychód roku poprzedniego - decyduje o prawie do Małego ZUS Plus.</summary>
    public decimal? PrzychodPoprzedniegoRoku { get; set; }

    /// <summary>Dni prowadzenia działalności w roku poprzednim.</summary>
    /// <remarks>
    /// Rok zaczęty albo zawieszony w trakcie wymaga przeliczenia dochodu
    /// na pełny miesiąc, inaczej podstawa wyszłaby zaniżona (art. 18c ust. 3).
    /// </remarks>
    public int DniProwadzeniaPoprzedniegoRoku { get; set; } = 365;

    /// <summary>Model dziedziny do rachunku składek.</summary>
    /// <param name="stawki">Kwoty roku, za który liczone są składki.</param>
    /// <param name="miesiac">Miesiąc - przy zmianie minimalnego w połowie roku.</param>
    public UstawieniaZus NaModel(StawkiZus stawki, int miesiac)
    {
        ArgumentNullException.ThrowIfNull(stawki);

        decimal? podstawaMzp = Tytul == TytulUbezpieczenia.MalyZusPlus
                               && DochodPoprzedniegoRoku is decimal dochod
            ? Zus.PodstawaMalegoZusPlus(dochod,
                                        Math.Max(DniProwadzeniaPoprzedniegoRoku, 1),
                                        stawki, miesiac)
            : null;

        return new UstawieniaZus(Tytul, Chorobowe, StopaWypadkowa,
                                 BezFunduszuPracy, podstawaMzp);
    }
}

/// <summary>
/// Składki za jeden miesiąc - naliczone przez program i zapłacone.
/// </summary>
/// <remarks>
/// Program liczy składki sam, ale zapisuje je dopiero wtedy, gdy użytkownik
/// potwierdzi zapłatę. Dopiero zapłacona składka jest kosztem albo odliczeniem
/// (art. 26 ust. 1 pkt 2 ustawy o PIT mówi o składkach <b>zapłaconych</b>),
/// więc naliczenie samo w sobie do księgi nie wchodzi.
/// </remarks>
public sealed class SkladkaZusFirmy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    /// <summary>Miesiąc, za który należne są składki.</summary>
    public int Rok { get; set; }

    /// <summary>Numer miesiąca od 1 do 12.</summary>
    public int Miesiac { get; set; }

    public decimal Emerytalna { get; set; }
    public decimal Rentowa { get; set; }
    public decimal Chorobowe { get; set; }
    public decimal Wypadkowa { get; set; }
    public decimal FunduszPracy { get; set; }
    public decimal Zdrowotna { get; set; }

    /// <summary>Podstawa wymiaru składek społecznych.</summary>
    public decimal PodstawaSpolecznych { get; set; }

    /// <summary>Podstawa wymiaru składki zdrowotnej.</summary>
    public decimal PodstawaZdrowotnej { get; set; }

    /// <summary>Dzień zapłaty; puste, dopóki składka nie została zapłacona.</summary>
    public DateOnly? DataZaplaty { get; set; }

    /// <summary>
    /// Czy składki społeczne ujęto w kosztach zamiast odliczyć od dochodu.
    /// </summary>
    /// <remarks>
    /// Wybór należy do podatnika i musi być konsekwentny - ta sama składka
    /// nie może być jednocześnie kosztem i odliczeniem.
    /// </remarks>
    public bool SpoleczneWKosztach { get; set; }

    public string? Uwagi { get; set; }

    /// <summary>Same ubezpieczenia społeczne, bez Funduszu Pracy.</summary>
    public decimal Ubezpieczenia =>
        Kwoty.Zaokraglij(Emerytalna + Rentowa + Chorobowe + Wypadkowa);

    /// <summary>Cała kwota jednego przelewu do ZUS.</summary>
    public decimal Razem => Kwoty.Zaokraglij(Ubezpieczenia + FunduszPracy + Zdrowotna);

    /// <summary>Czy składka została zapłacona.</summary>
    public bool Zaplacona => DataZaplaty is not null;

    /// <summary>Okres, za który należna jest składka.</summary>
    public OkresRozliczeniowy Okres() => OkresRozliczeniowy.Miesiac(Rok, Miesiac);
}

/// <summary>
/// Zaliczka na podatek dochodowy za okres.
/// </summary>
/// <remarks>
/// Program zapisuje zaliczkę dopiero po potwierdzeniu zapłaty. Kwota
/// naliczona zmienia się przy każdej dopisanej fakturze, a zapłacona już nie -
/// i to ona odejmuje się od podatku w okresach następnych. Gdyby program
/// odejmował kwoty naliczone, korekta faktury sprzed pół roku po cichu
/// zmieniłaby wszystkie późniejsze zaliczki.
/// </remarks>
public sealed class ZaliczkaPitFirmy : EncjaBazowa, INalezyDoFirmy
{
    public Guid FirmaId { get; set; }

    /// <summary>Rok podatkowy.</summary>
    public int Rok { get; set; }

    /// <summary>Numer okresu - miesiąc 1-12 albo kwartał 1-4.</summary>
    public int Numer { get; set; }

    /// <summary>Czy okres jest kwartałem.</summary>
    /// <remarks>
    /// Zapisane przy zaliczce, a nie odczytywane z ustawień firmy: zmiana
    /// sposobu rozliczania w kolejnym roku nie może przemianować zaliczek
    /// zapłaconych wcześniej.
    /// </remarks>
    public bool Kwartalna { get; set; }

    /// <summary>Kwota zapłacona, w pełnych złotych.</summary>
    public long Kwota { get; set; }

    /// <summary>Dzień zapłaty.</summary>
    public DateOnly DataZaplaty { get; set; }

    public string? Uwagi { get; set; }

    /// <summary>Okres, za który zapłacono zaliczkę.</summary>
    public OkresRozliczeniowy Okres() => Kwartalna
        ? OkresRozliczeniowy.Kwartal(Rok, Numer)
        : OkresRozliczeniowy.Miesiac(Rok, Numer);
}
