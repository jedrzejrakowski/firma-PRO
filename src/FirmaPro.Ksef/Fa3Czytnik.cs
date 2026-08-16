using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FirmaPro.Domena;

namespace FirmaPro.Ksef;

/// <summary>Pliku nie da się odczytać jako faktury FA(3).</summary>
public sealed class BladOdczytuFakturyException(string komunikat, Exception? przyczyna = null)
    : Exception(komunikat, przyczyna);

/// <summary>
/// Odczytuje fakturę zapisaną w strukturze FA(3).
/// </summary>
/// <remarks>
/// <para>
/// Odwrotność <see cref="Fa3Generator"/>. Potrzebna do wizualizacji: wydruk
/// faktury wysłanej do KSeF ma pokazywać <b>dokument, który tam trafił</b>,
/// a nie złożony na nowo z wierszy bazy danych. Te dwa źródła powinny mówić
/// to samo - ale „powinny" to za mało przy dokumencie, który rozstrzyga
/// o podatku, i tylko odczyt pozwala to sprawdzić.
/// </para>
/// <para>
/// Czytnik bierze z pliku to, co plik niesie. Kilku rzeczy widocznych na
/// wydruku struktura FA(3) nie przewiduje - numeru tabeli NBP i kwot faktur
/// zaliczkowych - i te trzeba dołożyć z zewnątrz. Czytnik ich nie zmyśla.
/// </para>
/// </remarks>
public static class Fa3Czytnik
{
    private static readonly XNamespace Ns = Fa3Generator.Ns;

    private static readonly Dictionary<string, RodzajFaktury> RodzajePoKodzie = new(
        StringComparer.Ordinal)
    {
        ["VAT"] = RodzajFaktury.Vat,
        ["KOR"] = RodzajFaktury.Korygujaca,
        ["ZAL"] = RodzajFaktury.Zaliczkowa,
        ["ROZ"] = RodzajFaktury.Rozliczeniowa,
        ["UPR"] = RodzajFaktury.Uproszczona,
        ["KOR_ZAL"] = RodzajFaktury.KorektaZaliczkowej,
        ["KOR_ROZ"] = RodzajFaktury.KorektaRozliczeniowej
    };

    /// <summary>Odczytuje fakturę z bajtów pliku XML.</summary>
    /// <exception cref="BladOdczytuFakturyException">
    /// Plik nie jest dokumentem FA(3) albo brakuje w nim pól, bez których
    /// nie ma faktury.
    /// </exception>
    public static Faktura Odczytaj(byte[] xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument dokument;

        try
        {
            using var strumien = new MemoryStream(xml);
            dokument = XDocument.Load(strumien);
        }
        catch (XmlException blad)
        {
            throw new BladOdczytuFakturyException(
                "Plik nie jest poprawnym dokumentem XML.", blad);
        }

        return Odczytaj(dokument);
    }

    /// <summary>Odczytuje fakturę z wczytanego dokumentu.</summary>
    public static Faktura Odczytaj(XDocument dokument)
    {
        ArgumentNullException.ThrowIfNull(dokument);

        XElement korzen = dokument.Root
            ?? throw new BladOdczytuFakturyException("Dokument jest pusty.");

        if (korzen.Name != Ns + "Faktura")
        {
            throw new BladOdczytuFakturyException(
                "Dokument nie jest fakturą w strukturze FA(3).");
        }

        XElement fa = Wymagany(korzen, "Fa");

        var faktura = new Faktura
        {
            Numer = TekstWymagany(fa, "P_2"),
            DataWystawienia = DataWymagana(fa, "P_1"),
            DataSprzedazy = Data(fa, "P_6"),
            MiejsceWystawienia = Tekst(fa, "P_1M"),
            Waluta = Tekst(fa, "KodWaluty") ?? "PLN",
            Rodzaj = Rodzaj(fa),
            Sprzedawca = Podmiot(Wymagany(korzen, "Podmiot1")),
            Nabywca = Podmiot(Wymagany(korzen, "Podmiot2")),
            Platnosc = Platnosc(fa.Element(Ns + "Platnosc")),
            Stopka = Stopka(korzen),
            PodstawaZwolnienia = PodstawaZwolnienia(fa),
            PrzyczynaKorekty = Tekst(fa, "PrzyczynaKorekty"),
            TypKorekty = TypKorekty(fa)
        };

        WczytajPodmiotyInne(korzen, faktura);
        WczytajUpowaznionego(korzen, faktura);
        WczytajSprzedawcePrzedKorekta(fa, faktura);
        WczytajWiersze(fa, faktura);
        WczytajKorygowane(fa, faktura);
        WczytajZaliczkowe(fa, faktura);
        WczytajZamowienie(fa, faktura);
        WczytajKurs(fa, faktura);

        return faktura;
    }

    // --------------------------------------------------------------- podmioty

    private static Podmiot Podmiot(XElement element)
    {
        XElement? dane = element.Element(Ns + "DaneIdentyfikacyjne");

        var podmiot = new Podmiot
        {
            Nazwa = dane is null ? string.Empty : Tekst(dane, "Nazwa") ?? string.Empty,
            Nip = dane is null ? string.Empty : Tekst(dane, "NIP") ?? string.Empty,
            KodUe = dane is null ? null : Tekst(dane, "KodUE"),
            NrVatUe = dane is null ? null : Tekst(dane, "NrVatUE"),
            JednostkaPodrzednaJst = Tekst(element, "JST") == "1",
            CzlonekGrupyVat = Tekst(element, "GV") == "1"
        };

        if (element.Element(Ns + "Adres") is { } adres)
        {
            podmiot.Adres = new Adres
            {
                KodKraju = Tekst(adres, "KodKraju") ?? "PL",
                Linia1 = Tekst(adres, "AdresL1") ?? string.Empty,
                Linia2 = Tekst(adres, "AdresL2")
            };
        }

        if (element.Element(Ns + "DaneKontaktowe") is { } kontakt)
        {
            podmiot.Email = Tekst(kontakt, "Email");
            podmiot.Telefon = Tekst(kontakt, "Telefon");
        }

        return podmiot;
    }

    /// <summary>
    /// Wczytuje podmioty trzecie związane z fakturą.
    /// </summary>
    /// <remarks>
    /// Rola jest w schemacie wyborem rozłącznym: albo numer z listy, albo
    /// znacznik roli własnej wraz z jej opisem. Nieznany numer zostawiamy jako
    /// brak roli - to lepsze niż podstawienie pierwszej z brzegu, bo wydruk
    /// przypisałby wtedy podmiotowi rolę, której nie ma w dokumencie.
    /// </remarks>
    private static void WczytajPodmiotyInne(XElement korzen, Faktura faktura)
    {
        foreach (XElement element in korzen.Elements(Ns + "Podmiot3"))
        {
            var podmiot = new PodmiotInny
            {
                Dane = Podmiot(element),
                OpisRoli = Tekst(element, "OpisRoli"),
                Udzial = Liczba(element, "Udzial"),
                NrKlienta = Tekst(element, "NrKlienta")
            };

            if (Tekst(element, "Rola") is { } kod
                && int.TryParse(kod, CultureInfo.InvariantCulture, out int numer)
                && Enum.IsDefined(typeof(RolaPodmiotu), numer))
            {
                podmiot.Rola = (RolaPodmiotu)numer;
            }

            faktura.PodmiotyInne.Add(podmiot);
        }
    }

    /// <summary>
    /// Wczytuje podmiot upoważniony.
    /// </summary>
    /// <remarks>
    /// Rola jest w tej sekcji obowiązkowa i nie ma wariantu opisowego. Gdyby
    /// przyszedł numer spoza listy, całą sekcję pomijamy - wypisanie na
    /// wydruku „komornik" przy nieznanym kodzie byłoby zgadywaniem.
    /// </remarks>
    private static void WczytajUpowaznionego(XElement korzen, Faktura faktura)
    {
        if (korzen.Element(Ns + "PodmiotUpowazniony") is not { } element)
        {
            return;
        }

        if (Tekst(element, "RolaPU") is not { } kod
            || !int.TryParse(kod, CultureInfo.InvariantCulture, out int numer)
            || !Enum.IsDefined(typeof(RolaUpowaznionego), numer))
        {
            return;
        }

        Podmiot dane = Podmiot(element);

        // Ta sekcja ma własne nazwy pól kontaktowych.
        if (element.Element(Ns + "DaneKontaktowe") is { } kontakt)
        {
            dane.Email = Tekst(kontakt, "EmailPU");
            dane.Telefon = Tekst(kontakt, "TelefonPU");
        }

        faktura.Upowazniony = new PodmiotUpowazniony
        {
            Dane = dane,
            Rola = (RolaUpowaznionego)numer
        };
    }

    /// <summary>Wczytuje dane sprzedawcy sprzed korekty.</summary>
    private static void WczytajSprzedawcePrzedKorekta(XElement fa, Faktura faktura)
    {
        if (fa.Element(Ns + "Podmiot1K") is { } element)
        {
            faktura.SprzedawcaPrzedKorekta = Podmiot(element);
        }
    }

    // ---------------------------------------------------------------- wiersze

    /// <summary>
    /// Wczytuje pozycje faktury.
    /// </summary>
    /// <remarks>
    /// Wiersze ze znacznikiem StanPrzed opisują stan sprzed korekty i trafiają
    /// do osobnej listy - zsumowane razem z pozostałymi dałyby podwojoną
    /// wartość dokumentu.
    /// </remarks>
    private static void WczytajWiersze(XElement fa, Faktura faktura)
    {
        foreach (XElement wiersz in fa.Elements(Ns + "FaWiersz"))
        {
            var pozycja = new PozycjaFaktury
            {
                Nazwa = Tekst(wiersz, "P_7") ?? string.Empty,
                Jednostka = Tekst(wiersz, "P_8A") ?? string.Empty,
                Ilosc = Liczba(wiersz, "P_8B") ?? 1m,
                CenaNetto = Liczba(wiersz, "P_9A") ?? 0m,
                Stawka = Stawka(wiersz),
                Gtu = Tekst(wiersz, "GTU"),
                Pkwiu = Tekst(wiersz, "PKWiU"),
                Cn = Tekst(wiersz, "CN"),
                Indeks = Tekst(wiersz, "Indeks")
            };

            if (Tekst(wiersz, "StanPrzed") == "1")
            {
                faktura.PozycjePrzedKorekta.Add(pozycja);
            }
            else
            {
                faktura.Pozycje.Add(pozycja);
            }
        }
    }

    /// <summary>
    /// Stawka podatku z pola P_12.
    /// </summary>
    /// <remarks>
    /// Nieznany kod nie może przejść po cichu jako 23% - wydruk pokazywałby
    /// wtedy podatek, którego na fakturze nie ma.
    /// </remarks>
    private static StawkaVat Stawka(XElement wiersz)
    {
        string? kod = Tekst(wiersz, "P_12");

        if (StawkaVat.TryZKodu(kod, out StawkaVat? stawka))
        {
            return stawka;
        }

        throw new BladOdczytuFakturyException(
            $"Wiersz faktury ma nieznaną stawkę podatku „{kod}\".");
    }

    /// <summary>
    /// Kurs waluty - w strukturze FA(3) powtórzony przy każdym wierszu.
    /// </summary>
    /// <remarks>
    /// Bierzemy pierwszy napotkany: wszystkie wiersze jednej faktury przelicza
    /// ten sam kurs. Dnia tabeli ani jej numeru struktura nie niesie, więc
    /// zostają puste - uzupełnia je warstwa, która ma dostęp do zapisanych
    /// danych faktury.
    /// </remarks>
    private static void WczytajKurs(XElement fa, Faktura faktura)
    {
        if (!faktura.Walutowa)
        {
            return;
        }

        XElement? wiersz = fa.Elements(Ns + "FaWiersz")
            .FirstOrDefault(w => w.Element(Ns + "KursWaluty") is not null);

        if (wiersz is not null && Liczba(wiersz, "KursWaluty") is decimal wartosc)
        {
            faktura.Kurs = new KursWaluty(faktura.Waluta, wartosc,
                faktura.DataWystawienia.AddDays(-1), null);
        }
    }

    // ------------------------------------------------------- korekta i zaliczki

    private static void WczytajKorygowane(XElement fa, Faktura faktura)
    {
        foreach (XElement dane in fa.Elements(Ns + "DaneFaKorygowanej"))
        {
            faktura.Korygowane.Add(new DaneFakturyKorygowanej(
                Tekst(dane, "NrFaKorygowanej") ?? string.Empty,
                Data(dane, "DataWystFaKorygowanej") ?? faktura.DataWystawienia,
                Tekst(dane, "NrKSeFFaKorygowanej")));
        }
    }

    /// <summary>
    /// Faktury zaliczkowe rozliczane tym dokumentem.
    /// </summary>
    /// <remarks>
    /// Kwoty zaliczek struktura nie niesie - wskazuje same faktury. Zostaje
    /// zero, które uzupełnia warstwa wyższa; wypisanie zmyślonej kwoty na
    /// wydruku byłoby gorsze niż jej brak.
    /// </remarks>
    private static void WczytajZaliczkowe(XElement fa, Faktura faktura)
    {
        foreach (XElement dane in fa.Elements(Ns + "FakturaZaliczkowa"))
        {
            faktura.Zaliczkowe.Add(new DaneZaliczki(
                Tekst(dane, "NrFaZaliczkowej") ?? string.Empty,
                faktura.DataWystawienia,
                Tekst(dane, "NrKSeFFaZaliczkowej"),
                0m));
        }
    }

    private static void WczytajZamowienie(XElement fa, Faktura faktura)
    {
        if (fa.Element(Ns + "Zamowienie") is not { } zamowienie)
        {
            return;
        }

        foreach (XElement wiersz in zamowienie.Elements(Ns + "ZamowienieWiersz"))
        {
            faktura.Zamowienie.Add(new PozycjaZamowienia
            {
                Nazwa = Tekst(wiersz, "P_7Z") ?? string.Empty,
                Jednostka = Tekst(wiersz, "P_8AZ") ?? string.Empty,
                Ilosc = Liczba(wiersz, "P_8BZ") ?? 1m,
                CenaNetto = Liczba(wiersz, "P_9AZ") ?? 0m,
                Stawka = StawkaZamowienia(wiersz),
                Gtu = Tekst(wiersz, "GTUZ")
            });
        }
    }

    private static StawkaVat StawkaZamowienia(XElement wiersz)
    {
        string? kod = Tekst(wiersz, "P_12Z");

        return StawkaVat.TryZKodu(kod, out StawkaVat? stawka)
            ? stawka
            : throw new BladOdczytuFakturyException(
                $"Wiersz zamówienia ma nieznaną stawkę podatku „{kod}\".");
    }

    // --------------------------------------------------------------- pozostałe

    private static WarunkiPlatnosci Platnosc(XElement? sekcja)
    {
        if (sekcja is null)
        {
            // Brak sekcji to brak warunków, a nie przelew z domyślnym terminem.
            return new WarunkiPlatnosci { Forma = null };
        }

        var warunki = new WarunkiPlatnosci
        {
            Forma = Forma(sekcja),
            DataZaplaty = Data(sekcja, "DataZaplaty"),
            Zaplacono = Tekst(sekcja, "Zaplacono") == "1"
        };

        // Zapłata z datą jest zapłatą także wtedy, gdy znacznika nie ma -
        // schemat pozwala podać tylko jedno z dwojga.
        if (warunki.DataZaplaty is not null)
        {
            warunki.Zaplacono = true;
        }

        if (sekcja.Element(Ns + "TerminPlatnosci") is { } termin)
        {
            warunki.Termin = Data(termin, "Termin");
        }

        if (sekcja.Element(Ns + "RachunekBankowy") is { } rachunek)
        {
            warunki.Rachunek = Tekst(rachunek, "NrRB");
            warunki.NazwaBanku = Tekst(rachunek, "NazwaBanku");
        }

        return warunki;
    }

    private static FormaPlatnosci? Forma(XElement sekcja) =>
        Tekst(sekcja, "FormaPlatnosci") is { } kod
        && int.TryParse(kod, CultureInfo.InvariantCulture, out int numer)
        && Enum.IsDefined(typeof(FormaPlatnosci), numer)
            ? (FormaPlatnosci)numer
            : null;

    private static RodzajFaktury Rodzaj(XElement fa) =>
        Tekst(fa, "RodzajFaktury") is { } kod && RodzajePoKodzie.TryGetValue(kod,
            out RodzajFaktury rodzaj)
            ? rodzaj
            : RodzajFaktury.Vat;

    private static TypKorektyVat? TypKorekty(XElement fa) =>
        Tekst(fa, "TypKorekty") is { } kod
        && int.TryParse(kod, CultureInfo.InvariantCulture, out int numer)
        && Enum.IsDefined(typeof(TypKorektyVat), numer)
            ? (TypKorektyVat)numer
            : null;

    private static string? PodstawaZwolnienia(XElement fa) =>
        fa.Element(Ns + "Adnotacje")?.Element(Ns + "Zwolnienie") is { } zwolnienie
            ? Tekst(zwolnienie, "P_19A")
            : null;

    private static string? Stopka(XElement korzen) =>
        korzen.Element(Ns + "Stopka")?.Element(Ns + "Informacje") is { } informacje
            ? Tekst(informacje, "StopkaFaktury")
            : null;

    // -------------------------------------------------------------- pomocnicze

    private static XElement Wymagany(XElement rodzic, string nazwa) =>
        rodzic.Element(Ns + nazwa)
        ?? throw new BladOdczytuFakturyException(
            $"W dokumencie brakuje sekcji „{nazwa}\".");

    private static string? Tekst(XElement rodzic, string nazwa) =>
        rodzic.Element(Ns + nazwa)?.Value is { Length: > 0 } wartosc ? wartosc : null;

    private static string TekstWymagany(XElement rodzic, string nazwa) =>
        Tekst(rodzic, nazwa)
        ?? throw new BladOdczytuFakturyException(
            $"W dokumencie brakuje pola „{nazwa}\".");

    private static DateOnly? Data(XElement rodzic, string nazwa) =>
        Tekst(rodzic, nazwa) is { } wartosc
        && DateOnly.TryParse(wartosc, CultureInfo.InvariantCulture, out DateOnly data)
            ? data
            : null;

    private static DateOnly DataWymagana(XElement rodzic, string nazwa) =>
        Data(rodzic, nazwa)
        ?? throw new BladOdczytuFakturyException(
            $"Pole „{nazwa}\" nie zawiera poprawnej daty.");

    private static decimal? Liczba(XElement rodzic, string nazwa) =>
        Tekst(rodzic, nazwa) is { } wartosc
        && decimal.TryParse(wartosc, NumberStyles.Number, CultureInfo.InvariantCulture,
               out decimal liczba)
            ? liczba
            : null;
}
