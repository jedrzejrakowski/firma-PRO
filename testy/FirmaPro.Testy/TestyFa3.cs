using System.Text;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Sprawdzenie generatora FA(3) oficjalnym schematem XSD Ministerstwa Finansów.
/// </summary>
/// <remarks>
/// To najważniejszy zestaw testów w całym systemie: chroni przed zmianą,
/// po której KSeF zacząłby odrzucać faktury.
/// </remarks>
public class TestyZgodnosciZeSchematem
{
    private static byte[] Zbuduj(Faktura faktura) =>
        Fa3Generator.ZbudujXml(faktura, Fabryka.DataWytworzenia);

    private static void ZbudujISprawdz(Faktura faktura)
    {
        IReadOnlyList<string> bledy = Fabryka.BledyWalidacjiXsd(Zbuduj(faktura));
        Assert.True(bledy.Count == 0,
            "Dokument niezgodny ze schematem FA(3):" + Environment.NewLine +
            string.Join(Environment.NewLine, bledy));
    }

    [Fact]
    public void FakturaPodstawowa()
    {
        ZbudujISprawdz(Fabryka.PrzykladowaFaktura());
    }

    /// <summary>
    /// Faktura zaliczkowa: wiersze pokazują wpłatę, a zamówienie - czego dotyczy.
    /// </summary>
    /// <remarks>
    /// Sekcja Zamowienie stoi w schemacie na samym końcu Fa, po warunkach
    /// transakcji. Zła kolejność elementów nie psuje niczego widocznego -
    /// dopiero KSeF odrzuca taki dokument.
    /// </remarks>
    [Fact]
    public void FakturaZaliczkowa()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Rodzaj = RodzajFaktury.Zaliczkowa;

        faktura.Zamowienie =
        [
            new PozycjaZamowienia
            {
                Nazwa = "Wykonanie instalacji",
                Jednostka = "usł.",
                Ilosc = 1m,
                CenaNetto = 10000m,
                Stawka = StawkaVat.Vat23
            },
            new PozycjaZamowienia
            {
                Nazwa = "Materiały budowlane",
                Jednostka = "kpl.",
                Ilosc = 2m,
                CenaNetto = 1500m,
                Stawka = StawkaVat.Vat8
            }
        ];

        // Wiersze faktury to samo rozbicie wpłaconej zaliczki na stawki.
        faktura.Pozycje = [.. Zaliczka.Rozbij(faktura.Zamowienie, 6150m)
            .Select(czesc => new PozycjaFaktury
            {
                Nazwa = $"Zaliczka - stawka {czesc.Stawka.Opis}",
                Jednostka = "usł.",
                Ilosc = 1m,
                CenaNetto = czesc.Netto,
                Stawka = czesc.Stawka
            })];

        ZbudujISprawdz(faktura);
    }

    /// <summary>
    /// Faktura końcowa wskazuje zaliczki - i te z KSeF, i te wystawione poza nim.
    /// </summary>
    [Fact]
    public void FakturaKoncowaZeWskazaniemZaliczek()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Rodzaj = RodzajFaktury.Rozliczeniowa;

        faktura.Zaliczkowe =
        [
            new DaneZaliczki("FV/2026/07/9", new DateOnly(2026, 7, 15),
                "5252248481-20260715-0AAAAA-BBBBBB-CC", 1230m),
            new DaneZaliczki("FV/2026/06/3", new DateOnly(2026, 6, 10), null, 615m)
        ];

        ZbudujISprawdz(faktura);
    }

    /// <summary>Korekta faktury zaliczkowej niesie i zamówienie, i dane korekty.</summary>
    [Fact]
    public void KorektaFakturyZaliczkowej()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Rodzaj = RodzajFaktury.KorektaZaliczkowej;
        faktura.PrzyczynaKorekty = "Zmiana wartości zamówienia";
        faktura.TypKorekty = TypKorektyVat.WDacieKorekty;
        faktura.Korygowane =
        [
            new DaneFakturyKorygowanej("FV/2026/07/9", new DateOnly(2026, 7, 15),
                "5252248481-20260715-0AAAAA-BBBBBB-CC")
        ];

        faktura.Zamowienie =
        [
            new PozycjaZamowienia
            {
                Nazwa = "Wykonanie instalacji",
                Jednostka = "usł.",
                Ilosc = 1m,
                CenaNetto = 9000m,
                Stawka = StawkaVat.Vat23
            }
        ];

        ZbudujISprawdz(faktura);
    }

    [Fact]
    public void NabywcaBezNumeruNip()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Nabywca = new Podmiot
        {
            Nazwa = "Jan Kowalski",
            Adres = new Adres { KodKraju = "PL", Linia1 = "ul. Kwiatowa 5" }
        };

        ZbudujISprawdz(faktura);
        // Osoba prywatna - w dokumencie pojawia się znacznik BrakID.
        Assert.Equal("1", Fabryka.Wartosc(Zbuduj(faktura), "BrakID"));
    }

    [Fact]
    public void NabywcaZNumeremVatUe()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Nabywca = new Podmiot
        {
            Nazwa = "Muster GmbH",
            KodUe = "DE",
            NrVatUe = "123456789",
            Adres = new Adres { KodKraju = "DE", Linia1 = "Hauptstrasse 1" }
        };

        ZbudujISprawdz(faktura);
        Assert.Equal("DE", Fabryka.Wartosc(Zbuduj(faktura), "KodUE"));
    }

    [Fact]
    public void SprzedazZwolnionaWymagaPodstawyPrawnej()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje =
        [
            new PozycjaFaktury
            {
                Nazwa = "Usługa medyczna",
                Ilosc = 1m,
                CenaNetto = 300.00m,
                Stawka = StawkaVat.Zwolniona
            }
        ];
        faktura.PodstawaZwolnienia = "art. 43 ust. 1 pkt 19 ustawy o VAT";

        ZbudujISprawdz(faktura);

        byte[] xml = Zbuduj(faktura);
        Assert.Equal("1", Fabryka.Wartosc(xml, "P_19"));
        Assert.Equal("300.00", Fabryka.Wartosc(xml, "P_13_7"));
    }

    [Fact]
    public void OdwrotneObciazenieUstawiaAdnotacje()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje =
        [
            new PozycjaFaktury
            {
                Nazwa = "Usługa budowlana",
                Ilosc = 1m,
                CenaNetto = 5000.00m,
                Stawka = StawkaVat.OdwrotneObciazenie
            }
        ];

        ZbudujISprawdz(faktura);

        byte[] xml = Zbuduj(faktura);
        Assert.Equal("1", Fabryka.Wartosc(xml, "P_18"));
        Assert.Equal("5000.00", Fabryka.Wartosc(xml, "P_13_10"));
    }

    [Fact]
    public void StawkiZeroweTrafiajaDoWlasciwychPol()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje =
        [
            new PozycjaFaktury { Nazwa = "Towar krajowy", Ilosc = 1m, CenaNetto = 100m, Stawka = StawkaVat.ZeroKrajowa },
            new PozycjaFaktury { Nazwa = "Towar WDT", Ilosc = 1m, CenaNetto = 200m, Stawka = StawkaVat.ZeroWdt },
            new PozycjaFaktury { Nazwa = "Towar eksport", Ilosc = 1m, CenaNetto = 300m, Stawka = StawkaVat.ZeroEksport }
        ];

        ZbudujISprawdz(faktura);

        byte[] xml = Zbuduj(faktura);
        Assert.Equal("100.00", Fabryka.Wartosc(xml, "P_13_6_1"));
        Assert.Equal("200.00", Fabryka.Wartosc(xml, "P_13_6_2"));
        Assert.Equal("300.00", Fabryka.Wartosc(xml, "P_13_6_3"));
    }

    [Fact]
    public void FakturaBezPlatnosciDatISopki()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Platnosc = new WarunkiPlatnosci { Forma = null };
        faktura.Stopka = null;
        faktura.DataSprzedazy = null;
        faktura.MiejsceWystawienia = null;

        ZbudujISprawdz(faktura);
    }

    [Fact]
    public void NabywcaJednostkaJstIczlonekGrupyVat()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Nabywca.JednostkaPodrzednaJst = true;
        faktura.Nabywca.CzlonekGrupyVat = true;

        ZbudujISprawdz(faktura);

        byte[] xml = Zbuduj(faktura);
        Assert.Equal("1", Fabryka.Wartosc(xml, "JST"));
        Assert.Equal("1", Fabryka.Wartosc(xml, "GV"));
    }

    [Fact]
    public void DuzaLiczbaPozycji()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje = Enumerable.Range(1, 50)
            .Select(i => new PozycjaFaktury
            {
                Nazwa = $"Pozycja {i}",
                Ilosc = 1m,
                CenaNetto = 9.99m,
                Stawka = StawkaVat.Vat23
            })
            .ToList();

        ZbudujISprawdz(faktura);
    }
}

/// <summary>Sprawdzenie nagłówka i pól, które schemat wymaga zawsze.</summary>
public class TestyNaglowkaIPolObowiazkowych
{
    [Fact]
    public void NaglowekMaWymaganeOznaczeniaWzoru()
    {
        byte[] xml = Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura(),
            Fabryka.DataWytworzenia);
        string tekst = Encoding.UTF8.GetString(xml);

        Assert.Contains("kodSystemowy=\"FA (3)\"", tekst, StringComparison.Ordinal);
        Assert.Contains("wersjaSchemy=\"1-0E\"", tekst, StringComparison.Ordinal);
        Assert.Equal("3", Fabryka.Wartosc(xml, "WariantFormularza"));
        Assert.Contains(Fa3Generator.Ns.NamespaceName, tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void PolaPodsumowaniaWystepujaNawetGdySaZerowe()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje =
        [
            new PozycjaFaktury { Nazwa = "Usługa", Ilosc = 1m, CenaNetto = 100m, Stawka = StawkaVat.Vat23 }
        ];

        byte[] xml = Fa3Generator.ZbudujXml(faktura, Fabryka.DataWytworzenia);

        foreach (string pole in new[] { "P_13_2", "P_14_2", "P_13_3", "P_14_3", "P_13_4", "P_14_4", "P_13_5" })
        {
            Assert.Equal("0.00", Fabryka.Wartosc(xml, pole));
        }
    }

    [Fact]
    public void DataSprzedazyPomijanaGdyRownaDacieWystawienia()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.DataSprzedazy = faktura.DataWystawienia;

        byte[] xml = Fa3Generator.ZbudujXml(faktura, Fabryka.DataWytworzenia);
        Assert.Null(Fabryka.Wartosc(xml, "P_6"));
    }

    [Fact]
    public void ZnacznikCzasuJestWStrefieUtc()
    {
        byte[] xml = Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura(),
            Fabryka.DataWytworzenia);
        Assert.Equal("2026-08-08T10:30:00Z", Fabryka.Wartosc(xml, "DataWytworzeniaFa"));
    }
}

/// <summary>Formatowanie liczb zgodnie ze wzorcami ze schematu.</summary>
public class TestyFormatowaniaLiczb
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(1234.5, "1234.50")]
    // Zaokrąglenie "w górę od połowy", a nie bankierskie - domyślne
    // Math.Round dałoby tu 2.34.
    [InlineData(2.345, "2.35")]
    public void KwotaZawszeZDwomaMiejscami(decimal wejscie, string oczekiwane)
    {
        Assert.Equal(oczekiwane, Kwoty.NaXml(wejscie));
    }

    [Theory]
    // Wzorce schematu odrzucają notację wykładniczą w rodzaju 1E+3.
    [InlineData(1000, 6, "1000")]
    [InlineData(2.500, 6, "2.5")]
    [InlineData(0.000001, 6, "0.000001")]
    public void LiczbaBezNotacjiWykladniczej(decimal wejscie, int miejsca, string oczekiwane)
    {
        Assert.Equal(oczekiwane, Kwoty.LiczbaNaXml(wejscie, miejsca));
    }

    [Fact]
    public void KwotaUzywaKropkiNiezaleznieOdUstawienRegionalnych()
    {
        // W polskich ustawieniach separatorem dziesiętnym jest przecinek,
        // a schemat wymaga kropki - formatowanie musi być niezależne od regionu.
        // Poprzednie ustawienie przywracamy, bo testy biegną równolegle
        // i zmiana kultury zaburzyłaby pozostałe.
        System.Globalization.CultureInfo poprzednia =
            System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("pl-PL");

            Assert.Equal("1234.50", Kwoty.NaXml(1234.5m));
            Assert.Equal("2.5", Kwoty.LiczbaNaXml(2.5m, 6));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = poprzednia;
        }
    }
}
