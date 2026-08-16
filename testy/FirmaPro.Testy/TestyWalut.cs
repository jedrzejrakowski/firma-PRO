using System.Xml.Linq;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Faktury wystawione w walucie obcej.
/// </summary>
/// <remarks>
/// Podatek państwo pobiera w złotych, więc kurs nie jest ozdobą - decyduje
/// o kwocie zobowiązania. Ustawa nie zostawia tu wyboru: liczy się średni kurs
/// NBP z ostatniego dnia roboczego przed powstaniem obowiązku podatkowego
/// (art. 31a), a gdy faktura powstała wcześniej - przed jej wystawieniem.
/// </remarks>
public sealed class TestyWalut
{
    /// <summary>
    /// Kurs bierze się z dnia poprzedzającego obowiązek podatkowy.
    /// </summary>
    [Fact]
    public void KursZDniaPoprzedzajacegoObowiazek()
    {
        DateOnly dzien = Przeliczenie.DzienKursu(
            dataWystawienia: new DateOnly(2026, 8, 20),
            dataObowiazku: new DateOnly(2026, 8, 20));

        Assert.Equal(new DateOnly(2026, 8, 19), dzien);
    }

    /// <summary>
    /// Faktura wystawiona przed dostawą liczy kurs od dnia wystawienia.
    /// </summary>
    /// <remarks>
    /// Art. 31a ust. 2 - inaczej kurs byłby brany z przyszłości, której
    /// w dniu wystawiania faktury jeszcze nie ma.
    /// </remarks>
    [Fact]
    public void FakturaPrzedDostawaLiczyKursOdWystawienia()
    {
        DateOnly dzien = Przeliczenie.DzienKursu(
            dataWystawienia: new DateOnly(2026, 8, 10),
            dataObowiazku: new DateOnly(2026, 8, 31));

        Assert.Equal(new DateOnly(2026, 8, 9), dzien);
    }

    [Fact]
    public void PrzeliczenieZaokraglaDoGroszy()
    {
        var kurs = new KursWaluty("EUR", 4.2837m, new DateOnly(2026, 8, 19), "160/A/NBP/2026");

        // 1000 EUR po 4,2837 to 4283,70 zł.
        Assert.Equal(4283.70m, Przeliczenie.NaZlote(1000m, kurs));

        // 123,45 EUR po 4,2837 to 528,82 zł (528,82268... w dół do groszy).
        Assert.Equal(528.82m, Przeliczenie.NaZlote(123.45m, kurs));
    }

    [Fact]
    public void ZlotowkiPrzechodzaBezZmiany()
    {
        KursWaluty kurs = KursWaluty.Zlotowy(new DateOnly(2026, 8, 19));

        Assert.True(kurs.Zlotowka);
        Assert.Equal(1234.56m, Przeliczenie.NaZlote(1234.56m, kurs));
    }

    /// <summary>
    /// Faktura w euro niesie kurs przy każdym wierszu i podatek w złotych.
    /// </summary>
    [Fact]
    public void FakturaWEuroMaKursIPodatekWZlotych()
    {
        Faktura faktura = FakturaWalutowa();

        XDocument dokument = XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(Fa3Generator.ZbudujXml(faktura)));

        XNamespace ns = dokument.Root!.GetDefaultNamespace();

        Assert.Equal("EUR", dokument.Descendants(ns + "KodWaluty").Single().Value);

        // Kurs stoi przy wierszu, tak jak chce schemat.
        Assert.Equal("4.2837", dokument.Descendants(ns + "KursWaluty").Single().Value);

        // 1000 EUR w stawce 23% to 230 EUR podatku, czyli 985,25 zł.
        Assert.Equal("230.00", dokument.Descendants(ns + "P_14_1").Single().Value);
        Assert.Equal("985.25", dokument.Descendants(ns + "P_14_1W").Single().Value);
    }

    /// <summary>
    /// Faktura złotowa nie ma ani kursu, ani pól z podatkiem „w złotych".
    /// </summary>
    /// <remarks>
    /// Powtórzenie tej samej kwoty dwa razy myliłoby odbiorcę, a schemat
    /// przewiduje te pola wyłącznie dla walut obcych.
    /// </remarks>
    [Fact]
    public void FakturaZlotowaNieMaPolWalutowych()
    {
        Faktura faktura = FakturaWalutowa();
        faktura.Waluta = "PLN";
        faktura.Kurs = null;

        XDocument dokument = XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(Fa3Generator.ZbudujXml(faktura)));

        XNamespace ns = dokument.Root!.GetDefaultNamespace();

        Assert.Empty(dokument.Descendants(ns + "KursWaluty"));
        Assert.Empty(dokument.Descendants(ns + "P_14_1W"));
    }

    /// <summary>Dokument w walucie obcej musi przejść schemat Ministerstwa.</summary>
    [Fact]
    public void FakturaWalutowaPrzechodziSchemat()
    {
        byte[] xml = Fa3Generator.ZbudujXml(FakturaWalutowa());

        IReadOnlyList<string> bledy = Fabryka.BledyWalidacjiXsd(xml);

        Assert.Empty(bledy);
    }

    private static Faktura FakturaWalutowa() => new()
    {
        Numer = "FV/2026/08/1",
        DataWystawienia = new DateOnly(2026, 8, 20),
        DataSprzedazy = new DateOnly(2026, 8, 20),
        MiejsceWystawienia = "Warszawa",
        Waluta = "EUR",
        Kurs = new KursWaluty("EUR", 4.2837m, new DateOnly(2026, 8, 19), "160/A/NBP/2026"),
        Sprzedawca = new Podmiot
        {
            Nazwa = "Moja Firma sp. z o.o.",
            Nip = "5252248481",
            Adres = new Adres { Linia1 = "ul. Prosta 51", Linia2 = "00-838 Warszawa" }
        },
        Nabywca = new Podmiot
        {
            Nazwa = "Kunde GmbH",
            Nip = "1180000001",
            Adres = new Adres { Linia1 = "Hauptstrasse 1", Linia2 = "10115 Berlin" }
        },
        Pozycje =
        [
            new PozycjaFaktury
            {
                Nazwa = "Usługa programistyczna",
                Jednostka = "usł.",
                Ilosc = 1m,
                CenaNetto = 1000m,
                Stawka = StawkaVat.Vat23
            }
        ]
    };
}
