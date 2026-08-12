using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FirmaPro.Domena;

namespace FirmaPro.Ksef;

/// <summary>
/// Buduje plik XML faktury w strukturze FA(3) - wzorze obowiązującym
/// w Krajowym Systemie e-Faktur od 1 lutego 2026 r.
/// </summary>
/// <remarks>
/// Schemat XSD sprawdza nie tylko obecność pól, ale i ich kolejność, dlatego
/// dokument budowany jest dokładnie w porządku wymaganym przez sekwencję.
/// Pułapki, o które najłatwiej się potknąć i które są tu obsłużone:
/// <list type="bullet">
/// <item>pola podsumowania P_13_1..P_13_5 oraz P_14_1..P_14_4 są obowiązkowe
/// nawet wtedy, gdy wynoszą zero - trzeba je wypisać jako "0.00",</item>
/// <item>sekcja Adnotacje jest obowiązkowa i wymaga jawnego zaprzeczenia
/// okoliczności, które nie wystąpiły (P_19N, P_22N, P_PMarzyN),</item>
/// <item>znaczniki JST i GV po stronie nabywcy są w FA(3) obowiązkowe,</item>
/// <item>P_1 to sama data, mimo że typ w schemacie nazywa się TDataT.</item>
/// </list>
/// </remarks>
public static class Fa3Generator
{
    /// <summary>Przestrzeń nazw wzoru FA(3).</summary>
    public static readonly XNamespace Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    /// <summary>Wartości identyfikujące wzór - w schemacie zadeklarowane jako stałe.</summary>
    public const string KodFormularza = "FA";
    public const string KodSystemowy = "FA (3)";
    public const string WersjaSchemy = "1-0E";
    public const string WariantFormularza = "3";

    /// <summary>Nazwa programu zapisywana w nagłówku faktury (pole SystemInfo).</summary>
    public const string NazwaSystemu = "Firma PRO";

    /// <summary>
    /// Pola podsumowania, które schemat wymaga zawsze - także z wartością zero.
    /// Kolejność jest narzucona przez sekwencję i przeplata sumy netto
    /// z kwotami podatku.
    /// </summary>
    private static readonly string[] PolaObowiazkowe =
    [
        "P_13_1", "P_14_1", "P_13_2", "P_14_2", "P_13_3", "P_14_3",
        "P_13_4", "P_14_4", "P_13_5"
    ];

    /// <summary>Pola podsumowania wypisywane tylko wtedy, gdy wystąpiły.</summary>
    private static readonly string[] PolaOpcjonalne =
    [
        "P_14_5", "P_13_6_1", "P_13_6_2", "P_13_6_3", "P_13_7", "P_13_8",
        "P_13_9", "P_13_10", "P_13_11"
    ];

    private static readonly Dictionary<RodzajFaktury, string> KodyRodzajow = new()
    {
        [RodzajFaktury.Vat] = "VAT",
        [RodzajFaktury.Korygujaca] = "KOR",
        [RodzajFaktury.Zaliczkowa] = "ZAL",
        [RodzajFaktury.Rozliczeniowa] = "ROZ",
        [RodzajFaktury.Uproszczona] = "UPR",
        [RodzajFaktury.KorektaZaliczkowej] = "KOR_ZAL",
        [RodzajFaktury.KorektaRozliczeniowej] = "KOR_ROZ"
    };

    /// <summary>
    /// Buduje dokument faktury. <paramref name="dataWytworzenia"/> pozwala
    /// ustalić znacznik czasu w testach.
    /// </summary>
    public static XDocument ZbudujDokument(Faktura faktura,
                                           DateTimeOffset? dataWytworzenia = null)
    {
        ArgumentNullException.ThrowIfNull(faktura);

        var korzen = new XElement(Ns + "Faktura",
            Naglowek(dataWytworzenia ?? DateTimeOffset.UtcNow),
            Podmiot1(faktura.Sprzedawca),
            Podmiot2(faktura.Nabywca),
            SekcjaFa(faktura));

        if (!string.IsNullOrWhiteSpace(faktura.Stopka))
        {
            korzen.Add(new XElement(Ns + "Stopka",
                new XElement(Ns + "Informacje",
                    new XElement(Ns + "StopkaFaktury", faktura.Stopka))));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), korzen);
    }

    /// <summary>
    /// Zwraca gotowy plik XML jako bajty w kodowaniu UTF-8.
    /// </summary>
    /// <remarks>
    /// To właśnie te bajty wysyłane są do KSeF i z nich liczony jest skrót
    /// SHA-256 używany w kodzie QR - nie wolno ich później przeformatowywać.
    /// </remarks>
    public static byte[] ZbudujXml(Faktura faktura, DateTimeOffset? dataWytworzenia = null)
    {
        XDocument dokument = ZbudujDokument(faktura, dataWytworzenia);

        var ustawienia = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false
        };

        using var strumien = new MemoryStream();
        using (XmlWriter pisarz = XmlWriter.Create(strumien, ustawienia))
        {
            dokument.Save(pisarz);
        }

        return strumien.ToArray();
    }

    private static XElement Naglowek(DateTimeOffset dataWytworzenia)
    {
        var kod = new XElement(Ns + "KodFormularza", KodFormularza);
        kod.SetAttributeValue("kodSystemowy", KodSystemowy);
        kod.SetAttributeValue("wersjaSchemy", WersjaSchemy);

        return new XElement(Ns + "Naglowek",
            kod,
            new XElement(Ns + "WariantFormularza", WariantFormularza),
            // Znacznik czasu w UTC, z sekundami - format wymagany przez TDataCzas.
            new XElement(Ns + "DataWytworzeniaFa",
                dataWytworzenia.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture)),
            new XElement(Ns + "SystemInfo", NazwaSystemu));
    }

    private static XElement Podmiot1(Podmiot sprzedawca)
    {
        var element = new XElement(Ns + "Podmiot1",
            new XElement(Ns + "DaneIdentyfikacyjne",
                new XElement(Ns + "NIP", sprzedawca.Nip),
                new XElement(Ns + "Nazwa", sprzedawca.Nazwa)),
            Adres(sprzedawca));

        DodajDaneKontaktowe(element, sprzedawca);
        return element;
    }

    private static XElement Podmiot2(Podmiot nabywca)
    {
        // Schemat pozwala wybrać jeden ze sposobów identyfikacji. Kolejność
        // prób: NIP, numer VAT UE, a gdy nabywca nie ma żadnego numeru
        // (np. osoba prywatna) - znacznik BrakID.
        var dane = new XElement(Ns + "DaneIdentyfikacyjne");
        if (!string.IsNullOrWhiteSpace(nabywca.Nip))
        {
            dane.Add(new XElement(Ns + "NIP", nabywca.Nip));
        }
        else if (!string.IsNullOrWhiteSpace(nabywca.KodUe)
                 && !string.IsNullOrWhiteSpace(nabywca.NrVatUe))
        {
            dane.Add(new XElement(Ns + "KodUE", nabywca.KodUe));
            dane.Add(new XElement(Ns + "NrVatUE", nabywca.NrVatUe));
        }
        else
        {
            dane.Add(new XElement(Ns + "BrakID", "1"));
        }

        dane.Add(new XElement(Ns + "Nazwa", nabywca.Nazwa));

        var element = new XElement(Ns + "Podmiot2", dane);

        if (!string.IsNullOrWhiteSpace(nabywca.Adres.Linia1))
        {
            element.Add(Adres(nabywca));
        }

        DodajDaneKontaktowe(element, nabywca);

        // Znaczniki obowiązkowe w FA(3) - muszą wystąpić na końcu sekcji
        // i przyjmują wartość "2" (nie), gdy przypadek nie zachodzi.
        element.Add(new XElement(Ns + "JST", nabywca.JednostkaPodrzednaJst ? "1" : "2"));
        element.Add(new XElement(Ns + "GV", nabywca.CzlonekGrupyVat ? "1" : "2"));

        return element;
    }

    private static XElement Adres(Podmiot podmiot)
    {
        var adres = new XElement(Ns + "Adres",
            new XElement(Ns + "KodKraju", podmiot.Adres.KodKraju),
            new XElement(Ns + "AdresL1", podmiot.Adres.Linia1));

        if (!string.IsNullOrWhiteSpace(podmiot.Adres.Linia2))
        {
            adres.Add(new XElement(Ns + "AdresL2", podmiot.Adres.Linia2));
        }

        return adres;
    }

    private static void DodajDaneKontaktowe(XElement rodzic, Podmiot podmiot)
    {
        if (string.IsNullOrWhiteSpace(podmiot.Email)
            && string.IsNullOrWhiteSpace(podmiot.Telefon))
        {
            return;
        }

        var kontakt = new XElement(Ns + "DaneKontaktowe");
        if (!string.IsNullOrWhiteSpace(podmiot.Email))
        {
            kontakt.Add(new XElement(Ns + "Email", podmiot.Email));
        }

        if (!string.IsNullOrWhiteSpace(podmiot.Telefon))
        {
            kontakt.Add(new XElement(Ns + "Telefon", podmiot.Telefon));
        }

        rodzic.Add(kontakt);
    }

    private static XElement SekcjaFa(Faktura faktura)
    {
        var fa = new XElement(Ns + "Fa",
            new XElement(Ns + "KodWaluty", faktura.Waluta),
            new XElement(Ns + "P_1", faktura.DataWystawienia.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(faktura.MiejsceWystawienia))
        {
            fa.Add(new XElement(Ns + "P_1M", faktura.MiejsceWystawienia));
        }

        fa.Add(new XElement(Ns + "P_2", faktura.Numer));

        // P_6 wolno pominąć, gdy data sprzedaży pokrywa się z datą wystawienia.
        if (faktura.DataSprzedazy is { } sprzedaz && sprzedaz != faktura.DataWystawienia)
        {
            fa.Add(new XElement(Ns + "P_6",
                sprzedaz.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        DodajPodsumowanie(fa, faktura);
        fa.Add(Adnotacje(faktura));
        fa.Add(new XElement(Ns + "RodzajFaktury", KodyRodzajow[faktura.Rodzaj]));
        DodajDaneKorekty(fa, faktura);
        DodajFakturyZaliczkowe(fa, faktura);
        DodajWiersze(fa, faktura);
        DodajPlatnosc(fa, faktura);
        DodajZamowienie(fa, faktura);

        return fa;
    }

    private static void DodajPodsumowanie(XElement fa, Faktura faktura)
    {
        PodsumowanieFaktury podsumowanie = faktura.Podsumowanie();

        foreach (string pole in PolaObowiazkowe)
        {
            fa.Add(new XElement(Ns + pole, Kwoty.NaXml(podsumowanie.Pole(pole))));
        }

        foreach (string pole in PolaOpcjonalne)
        {
            if (podsumowanie.MaPole(pole))
            {
                fa.Add(new XElement(Ns + pole, Kwoty.NaXml(podsumowanie.Pole(pole))));
            }
        }

        fa.Add(new XElement(Ns + "P_15", Kwoty.NaXml(podsumowanie.RazemBrutto)));
    }

    /// <summary>
    /// Obowiązkowa sekcja Adnotacje.
    /// </summary>
    /// <remarks>
    /// Wszystkie znaczniki trzeba wypełnić także wtedy, gdy dana okoliczność
    /// nie zachodzi: "2" oznacza "nie", a znaczniki z literą N (P_19N, P_22N,
    /// P_PMarzyN) oznaczają brak wystąpienia całej grupy przypadków.
    /// </remarks>
    private static XElement Adnotacje(Faktura faktura)
    {
        var zwolnienie = new XElement(Ns + "Zwolnienie");
        if (faktura.MaSprzedazZwolniona)
        {
            // Przy sprzedaży zwolnionej trzeba wskazać podstawę prawną.
            zwolnienie.Add(new XElement(Ns + "P_19", "1"));
            zwolnienie.Add(new XElement(Ns + "P_19A", faktura.PodstawaZwolnienia));
        }
        else
        {
            zwolnienie.Add(new XElement(Ns + "P_19N", "1"));
        }

        return new XElement(Ns + "Adnotacje",
            // P_16 - metoda kasowa, P_17 - samofakturowanie,
            // P_18 - odwrotne obciążenie, P_18A - podzielona płatność.
            new XElement(Ns + "P_16", "2"),
            new XElement(Ns + "P_17", "2"),
            new XElement(Ns + "P_18", faktura.MaOdwrotneObciazenie ? "1" : "2"),
            new XElement(Ns + "P_18A", "2"),
            zwolnienie,
            new XElement(Ns + "NoweSrodkiTransportu",
                new XElement(Ns + "P_22N", "1")),
            new XElement(Ns + "P_23", "2"),
            new XElement(Ns + "PMarzy",
                new XElement(Ns + "P_PMarzyN", "1")));
    }

    /// <summary>
    /// Sekcja danych korekty - obowiązkowa przy fakturze korygującej.
    /// </summary>
    /// <remarks>
    /// Schemat wymaga wskazania faktury korygowanej wraz z informacją, czy ma
    /// ona numer KSeF. Wybór jest rozłączny: albo numer KSeF, albo znacznik
    /// faktury wystawionej poza systemem - pominięcie obu unieważnia dokument.
    /// </remarks>
    private static void DodajDaneKorekty(XElement fa, Faktura faktura)
    {
        if (!faktura.CzyKorekta)
        {
            return;
        }

        DodajGdyJest(fa, "PrzyczynaKorekty", faktura.PrzyczynaKorekty);

        if (faktura.TypKorekty is TypKorektyVat typ)
        {
            fa.Add(new XElement(Ns + "TypKorekty",
                ((int)typ).ToString(CultureInfo.InvariantCulture)));
        }

        foreach (DaneFakturyKorygowanej korygowana in faktura.Korygowane)
        {
            var dane = new XElement(Ns + "DaneFaKorygowanej",
                new XElement(Ns + "DataWystFaKorygowanej",
                    korygowana.DataWystawienia.ToString("yyyy-MM-dd",
                        CultureInfo.InvariantCulture)),
                new XElement(Ns + "NrFaKorygowanej", korygowana.Numer));

            if (string.IsNullOrWhiteSpace(korygowana.NumerKsef))
            {
                dane.Add(new XElement(Ns + "NrKSeFN", "1"));
            }
            else
            {
                dane.Add(new XElement(Ns + "NrKSeF", "1"));
                dane.Add(new XElement(Ns + "NrKSeFFaKorygowanej", korygowana.NumerKsef));
            }

            fa.Add(dane);
        }
    }

    /// <summary>
    /// Wskazuje faktury zaliczkowe rozliczane fakturą końcową.
    /// </summary>
    /// <remarks>
    /// Kolejność w schemacie jest ustalona: sekcja stoi po DodatkowyOpis,
    /// a przed wierszami faktury. Faktura wystawiona w KSeF wskazywana jest
    /// numerem KSeF; wystawiona poza nim - własnym numerem ze znacznikiem.
    /// </remarks>
    private static void DodajFakturyZaliczkowe(XElement fa, Faktura faktura)
    {
        foreach (DaneZaliczki zaliczkowa in faktura.Zaliczkowe)
        {
            fa.Add(string.IsNullOrWhiteSpace(zaliczkowa.NumerKsef)
                ? new XElement(Ns + "FakturaZaliczkowa",
                    new XElement(Ns + "NrKSeFZN", "1"),
                    new XElement(Ns + "NrFaZaliczkowej", zaliczkowa.Numer))
                : new XElement(Ns + "FakturaZaliczkowa",
                    new XElement(Ns + "NrKSeFFaZaliczkowej", zaliczkowa.NumerKsef)));
        }
    }

    /// <summary>
    /// Zamówienie lub umowa, na poczet których wpłacono zaliczkę.
    /// </summary>
    /// <remarks>
    /// W schemacie sekcja stoi na końcu Fa, po warunkach transakcji. Wartość
    /// zamówienia podaje się z podatkiem - to kwota, do której zmierzają
    /// kolejne zaliczki.
    /// </remarks>
    private static void DodajZamowienie(XElement fa, Faktura faktura)
    {
        if (faktura.Zamowienie.Count == 0)
        {
            return;
        }

        var sekcja = new XElement(Ns + "Zamowienie",
            new XElement(Ns + "WartoscZamowienia",
                Kwoty.NaXml(Zaliczka.WartoscZamowienia(faktura.Zamowienie))));

        int numer = 1;

        foreach (PozycjaZamowienia pozycja in faktura.Zamowienie)
        {
            var wiersz = new XElement(Ns + "ZamowienieWiersz",
                new XElement(Ns + "NrWierszaZam", numer++));

            DodajGdyJest(wiersz, "P_7Z", pozycja.Nazwa);
            DodajGdyJest(wiersz, "P_8AZ", pozycja.Jednostka);

            wiersz.Add(new XElement(Ns + "P_8BZ",
                Kwoty.LiczbaNaXml(pozycja.Ilosc, 6)));
            wiersz.Add(new XElement(Ns + "P_9AZ",
                Kwoty.LiczbaNaXml(pozycja.CenaNetto, 2)));
            wiersz.Add(new XElement(Ns + "P_11NettoZ",
                Kwoty.NaXml(pozycja.WartoscNetto)));

            if (pozycja.Stawka.NaliczaPodatek)
            {
                wiersz.Add(new XElement(Ns + "P_11VatZ", Kwoty.NaXml(pozycja.KwotaVat)));
            }

            wiersz.Add(new XElement(Ns + "P_12Z", pozycja.Stawka.Kod));

            DodajGdyJest(wiersz, "GTUZ", pozycja.Gtu);

            sekcja.Add(wiersz);
        }

        fa.Add(sekcja);
    }

    private static void DodajWiersze(XElement fa, Faktura faktura)
    {
        int numerWiersza = 1;

        // Przy korekcie najpierw idą pozycje sprzed zmiany, oznaczone
        // znacznikiem StanPrzed, a dopiero po nich stan po korekcie.
        // Numeracja jest wspólna i ciągła dla obu grup.
        foreach (PozycjaFaktury pozycja in faktura.PozycjePrzedKorekta)
        {
            fa.Add(Wiersz(pozycja, numerWiersza++, stanPrzed: true));
        }

        foreach (PozycjaFaktury pozycja in faktura.Pozycje)
        {
            fa.Add(Wiersz(pozycja, numerWiersza++, stanPrzed: false));
        }
    }

    private static XElement Wiersz(PozycjaFaktury pozycja, int numerWiersza, bool stanPrzed)
    {
        var wiersz = new XElement(Ns + "FaWiersz",
            new XElement(Ns + "NrWierszaFa", numerWiersza.ToString(CultureInfo.InvariantCulture)));

        DodajGdyJest(wiersz, "P_7", pozycja.Nazwa);
        DodajGdyJest(wiersz, "Indeks", pozycja.Indeks);
        DodajGdyJest(wiersz, "PKWiU", pozycja.Pkwiu);
        DodajGdyJest(wiersz, "CN", pozycja.Cn);
        DodajGdyJest(wiersz, "P_8A", pozycja.Jednostka);

        wiersz.Add(new XElement(Ns + "P_8B", Kwoty.LiczbaNaXml(pozycja.Ilosc, 6)));
        wiersz.Add(new XElement(Ns + "P_9A", Kwoty.LiczbaNaXml(pozycja.CenaNetto, 8)));
        wiersz.Add(new XElement(Ns + "P_11", Kwoty.NaXml(pozycja.WartoscNetto)));
        wiersz.Add(new XElement(Ns + "P_12", pozycja.Stawka.Kod));

        DodajGdyJest(wiersz, "GTU", pozycja.Gtu);

        if (stanPrzed)
        {
            wiersz.Add(new XElement(Ns + "StanPrzed", "1"));
        }

        return wiersz;
    }

    private static void DodajPlatnosc(XElement fa, Faktura faktura)
    {
        WarunkiPlatnosci platnosc = faktura.Platnosc;
        if (platnosc.CzyPusta)
        {
            return;
        }

        var sekcja = new XElement(Ns + "Platnosc");

        // Znacznik zapłaty i data zapłaty są dla schematu wariantami tej samej
        // decyzji - wolno podać tylko jeden z nich.
        if (platnosc.DataZaplaty is { } dataZaplaty)
        {
            sekcja.Add(new XElement(Ns + "DataZaplaty",
                dataZaplaty.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        else if (platnosc.Zaplacono)
        {
            sekcja.Add(new XElement(Ns + "Zaplacono", "1"));
        }

        if (platnosc.Termin is { } termin)
        {
            sekcja.Add(new XElement(Ns + "TerminPlatnosci",
                new XElement(Ns + "Termin",
                    termin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        }

        if (platnosc.Forma is { } forma)
        {
            sekcja.Add(new XElement(Ns + "FormaPlatnosci",
                ((int)forma).ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(platnosc.Rachunek))
        {
            var rachunek = new XElement(Ns + "RachunekBankowy",
                new XElement(Ns + "NrRB", platnosc.Rachunek));
            DodajGdyJest(rachunek, "NazwaBanku", platnosc.NazwaBanku);
            sekcja.Add(rachunek);
        }

        fa.Add(sekcja);
    }

    private static void DodajGdyJest(XElement rodzic, string nazwa, string? wartosc)
    {
        if (!string.IsNullOrWhiteSpace(wartosc))
        {
            rodzic.Add(new XElement(Ns + nazwa, wartosc.Trim()));
        }
    }
}
