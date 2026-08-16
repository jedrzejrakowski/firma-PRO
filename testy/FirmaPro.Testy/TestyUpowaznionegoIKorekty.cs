using System.Xml.Linq;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Podmiot upoważniony i korekta danych sprzedawcy.
/// </summary>
/// <remarks>
/// Dwie sekcje schematu dla sytuacji, w których faktura mówi coś więcej niż
/// „sprzedawca wystawił nabywcy". Komornik wystawia dokument za dłużnika
/// (art. 106c), a korekta danych sprzedawcy musi pokazać brzmienie sprzed
/// poprawki (art. 106j ust. 2 pkt 3) - inaczej nie widać, co poprawiono.
/// </remarks>
public sealed class TestyUpowaznionegoIKorekty
{
    // ------------------------------------------------- podmiot upoważniony

    [Fact]
    public void UpowaznionyTrafiaDoDokumentuZRola()
    {
        XDocument dokument = Zbuduj(ZKomornikiem());
        XNamespace ns = dokument.Root!.GetDefaultNamespace();

        XElement sekcja = Assert.Single(dokument.Descendants(ns + "PodmiotUpowazniony"));

        Assert.Equal("Komornik Sądowy przy Sądzie Rejonowym",
            sekcja.Descendants(ns + "Nazwa").Single().Value);

        // Rola 2 to komornik sądowy.
        Assert.Equal("2", sekcja.Element(ns + "RolaPU")!.Value);
    }

    /// <summary>
    /// Dane kontaktowe mają w tej sekcji własne nazwy pól.
    /// </summary>
    /// <remarks>
    /// EmailPU i TelefonPU zamiast Email i Telefon. Użycie wspólnych nazw
    /// dawałoby dokument odrzucany przez schemat.
    /// </remarks>
    [Fact]
    public void DaneKontaktoweMajaWlasneNazwyPol()
    {
        Faktura faktura = ZKomornikiem();
        faktura.Upowazniony!.Dane.Email = "kancelaria@example.pl";
        faktura.Upowazniony.Dane.Telefon = "+48221234567";

        XDocument dokument = Zbuduj(faktura);
        XNamespace ns = dokument.Root!.GetDefaultNamespace();

        Assert.Equal("kancelaria@example.pl",
            dokument.Descendants(ns + "EmailPU").Single().Value);
        Assert.Equal("+48221234567",
            dokument.Descendants(ns + "TelefonPU").Single().Value);
    }

    [Fact]
    public void DokumentZUpowaznionymPrzechodziSchemat() =>
        Assert.Empty(Fabryka.BledyWalidacjiXsd(Fa3Generator.ZbudujXml(ZKomornikiem())));

    [Fact]
    public void OdczytOddajeUpowaznionego()
    {
        Faktura odczytana = TamIzPowrotem(ZKomornikiem());

        Assert.NotNull(odczytana.Upowazniony);
        Assert.Equal(RolaUpowaznionego.KomornikSadowy, odczytana.Upowazniony!.Rola);
        Assert.Equal("Komornik Sądowy przy Sądzie Rejonowym",
            odczytana.Upowazniony.Dane.Nazwa);
        Assert.Equal("ul. Sądowa 1", odczytana.Upowazniony.Dane.Adres.Linia1);
        Assert.Equal("Komornik sądowy", odczytana.Upowazniony.NazwaRoli);
    }

    /// <summary>
    /// Adres jest w tej sekcji obowiązkowy.
    /// </summary>
    /// <remarks>
    /// Komornik ani przedstawiciel podatkowy nie może być podmiotem
    /// anonimowym - odpowiada za dokument obok sprzedawcy.
    /// </remarks>
    [Fact]
    public void UpowaznionyBezAdresuJestBledem()
    {
        Faktura faktura = ZKomornikiem();
        faktura.Upowazniony!.Dane.Adres = new Adres();

        Assert.True(Walidator.SprawdzFakture(faktura).SaBledy);
    }

    [Fact]
    public void UpowaznionyBezNipuJestBledem()
    {
        Faktura faktura = ZKomornikiem();
        faktura.Upowazniony!.Dane.Nip = string.Empty;

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.Contains(wynik.Problemy,
            p => p.Pole.Contains("NIP", StringComparison.Ordinal));
    }

    [Fact]
    public void ZwyklaFakturaNieMaSekcjiUpowaznionego()
    {
        XDocument dokument = Zbuduj(Fabryka.PrzykladowaFaktura());
        XNamespace ns = dokument.Root!.GetDefaultNamespace();

        Assert.Empty(dokument.Descendants(ns + "PodmiotUpowazniony"));
    }

    // --------------------------------------------- korekta danych sprzedawcy

    [Fact]
    public void DaneSprzedawcyPrzedKorektaTrafiajaDoDokumentu()
    {
        XDocument dokument = Zbuduj(KorektaDanychSprzedawcy());
        XNamespace ns = dokument.Root!.GetDefaultNamespace();

        XElement sekcja = Assert.Single(dokument.Descendants(ns + "Podmiot1K"));

        Assert.Equal("Moja Firma sp. z o.o. (dawna nazwa)",
            sekcja.Descendants(ns + "Nazwa").Single().Value);
        Assert.Equal("ul. Stara 1", sekcja.Descendants(ns + "AdresL1").Single().Value);
    }

    [Fact]
    public void KorektaDanychSprzedawcyPrzechodziSchemat() =>
        Assert.Empty(Fabryka.BledyWalidacjiXsd(
            Fa3Generator.ZbudujXml(KorektaDanychSprzedawcy())));

    [Fact]
    public void OdczytOddajeDaneSprzedawcyPrzedKorekta()
    {
        Faktura odczytana = TamIzPowrotem(KorektaDanychSprzedawcy());

        Assert.NotNull(odczytana.SprzedawcaPrzedKorekta);
        Assert.Equal("Moja Firma sp. z o.o. (dawna nazwa)",
            odczytana.SprzedawcaPrzedKorekta!.Nazwa);
        Assert.Equal("5252248481", odczytana.SprzedawcaPrzedKorekta.Nip);
    }

    /// <summary>
    /// Na zwykłej fakturze ta sekcja nie ma prawa wystąpić.
    /// </summary>
    /// <remarks>
    /// Nie ma czego korygować, a schemat w ogóle jej tam nie przewiduje -
    /// dokument zostałby odrzucony przy wysyłce.
    /// </remarks>
    [Fact]
    public void SprzedawcaPrzedKorektaPozaKorektaJestBledem()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.SprzedawcaPrzedKorekta = new Podmiot
        {
            Nazwa = "Dawna nazwa",
            Nip = faktura.Sprzedawca.Nip,
            Adres = new Adres { Linia1 = "ul. Stara 1" }
        };

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.True(wynik.SaBledy);
        Assert.Contains(wynik.Problemy,
            p => p.Komunikat.Contains("korygującej", StringComparison.Ordinal));
    }

    /// <summary>
    /// Zmiana numeru NIP tą drogą jest zablokowana.
    /// </summary>
    /// <remarks>
    /// Najważniejsza reguła w tym pliku. Błędnego numeru nie poprawia się
    /// korektą danych - trzeba wystawić korektę do wartości zerowych i nową
    /// fakturę. Dokument z dwoma różnymi numerami urząd powiązałby z kimś
    /// zupełnie innym albo z nikim.
    /// </remarks>
    [Fact]
    public void ZmianaNipuSprzedawcyJestZablokowana()
    {
        Faktura faktura = KorektaDanychSprzedawcy();
        faktura.SprzedawcaPrzedKorekta!.Nip = "7010001453";

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.True(wynik.SaBledy);
        Assert.Contains(wynik.Problemy,
            p => p.Komunikat.Contains("zerowych", StringComparison.Ordinal));
    }

    [Fact]
    public void PoprawnaKorektaDanychNieMaBledow() =>
        Assert.False(Walidator.SprawdzFakture(KorektaDanychSprzedawcy()).SaBledy);

    /// <summary>
    /// Wszystkie sekcje naraz przechodzą schemat.
    /// </summary>
    /// <remarks>
    /// Najtrudniejszy przypadek: Podmiot3, PodmiotUpowazniony i Podmiot1K
    /// stoją w trzech różnych miejscach dokumentu, a schemat pilnuje kolejności
    /// elementów. Osobno każda sekcja przechodzi - dopiero razem widać, czy
    /// wstawiono je we właściwych miejscach.
    /// </remarks>
    [Fact]
    public void WszystkieSekcjeNarazPrzechodzaSchemat()
    {
        Faktura faktura = KorektaDanychSprzedawcy();

        faktura.Upowazniony = ZKomornikiem().Upowazniony;
        faktura.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot
            {
                Nazwa = "Oddział w Krakowie",
                Nip = "1180000001",
                Adres = new Adres { Linia1 = "ul. Długa 5", Linia2 = "31-147 Kraków" }
            },
            Rola = RolaPodmiotu.Odbiorca
        });

        Assert.Empty(Fabryka.BledyWalidacjiXsd(Fa3Generator.ZbudujXml(faktura)));

        // I wracają z pliku wszystkie trzy, nie myląc się ze sobą.
        Faktura odczytana = TamIzPowrotem(faktura);

        Assert.Single(odczytana.PodmiotyInne);
        Assert.NotNull(odczytana.Upowazniony);
        Assert.NotNull(odczytana.SprzedawcaPrzedKorekta);
        Assert.Equal("Oddział w Krakowie", odczytana.PodmiotyInne[0].Dane.Nazwa);
        Assert.Equal(RolaUpowaznionego.KomornikSadowy, odczytana.Upowazniony!.Rola);
        Assert.Equal("Moja Firma sp. z o.o. (dawna nazwa)",
            odczytana.SprzedawcaPrzedKorekta!.Nazwa);
    }

    // ------------------------------------------------------------ pomocnicze

    private static XDocument Zbuduj(Faktura faktura) =>
        XDocument.Parse(System.Text.Encoding.UTF8.GetString(
            Fa3Generator.ZbudujXml(faktura)));

    private static Faktura TamIzPowrotem(Faktura faktura) =>
        Fa3Czytnik.Odczytaj(Fa3Generator.ZbudujXml(faktura));

    private static Faktura ZKomornikiem()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();

        faktura.Upowazniony = new PodmiotUpowazniony
        {
            Rola = RolaUpowaznionego.KomornikSadowy,
            Dane = new Podmiot
            {
                Nazwa = "Komornik Sądowy przy Sądzie Rejonowym",
                Nip = "7010001453",
                Adres = new Adres { Linia1 = "ul. Sądowa 1", Linia2 = "00-100 Warszawa" }
            }
        };

        return faktura;
    }

    private static Faktura KorektaDanychSprzedawcy()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();

        faktura.Rodzaj = RodzajFaktury.Korygujaca;
        faktura.PrzyczynaKorekty = "Zmiana nazwy sprzedawcy";
        faktura.Korygowane.Add(new DaneFakturyKorygowanej(
            "FV/2026/07/9", new DateOnly(2026, 7, 20), null));

        faktura.SprzedawcaPrzedKorekta = new Podmiot
        {
            Nazwa = "Moja Firma sp. z o.o. (dawna nazwa)",
            Nip = faktura.Sprzedawca.Nip,
            Adres = new Adres { Linia1 = "ul. Stara 1", Linia2 = "00-001 Warszawa" }
        };

        return faktura;
    }
}
