using System.Xml.Linq;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Podmioty trzecie na fakturze.
/// </summary>
/// <remarks>
/// Nie każda faktura ma dwie strony. Gmina wystawia dokument, ale towar
/// odbiera podległa jej jednostka; wierzytelność bywa sprzedana faktorowi;
/// obok nabywcy stoi drugi kupujący z własnym udziałem. Bez sekcji Podmiot3
/// jednostka podrzędna samorządu w ogóle nie zobaczy faktury w swoim KSeF -
/// system udostępnia dokument właśnie po numerze NIP podanym w tej sekcji.
/// </remarks>
public sealed class TestyPodmiotowInnych
{
    [Fact]
    public void PodmiotTrafiaDoDokumentuZRola()
    {
        Faktura faktura = ZOdbiorca();

        XDocument dokument = XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(Fa3Generator.ZbudujXml(faktura)));

        XNamespace ns = dokument.Root!.GetDefaultNamespace();
        XElement podmiot = Assert.Single(dokument.Descendants(ns + "Podmiot3"));

        Assert.Equal("1180000001", podmiot.Descendants(ns + "NIP").Single().Value);
        Assert.Equal("Oddział w Krakowie",
            podmiot.Descendants(ns + "Nazwa").Single().Value);

        // Rola 2 to odbiorca - jednostka wewnętrzna nabywcy.
        Assert.Equal("2", podmiot.Element(ns + "Rola")!.Value);
    }

    /// <summary>
    /// Rola własna zapisuje się znacznikiem i opisem, nigdy numerem.
    /// </summary>
    /// <remarks>
    /// Schemat traktuje to jako wybór rozłączny. Podanie obu naraz albo
    /// żadnego unieważnia dokument.
    /// </remarks>
    [Fact]
    public void RolaWlasnaIdzieJakoOpis()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Zarządca nieruchomości", Nip = "1180000001" },
            OpisRoli = "Zarządca budynku"
        });

        XDocument dokument = XDocument.Parse(
            System.Text.Encoding.UTF8.GetString(Fa3Generator.ZbudujXml(faktura)));

        XNamespace ns = dokument.Root!.GetDefaultNamespace();
        XElement podmiot = dokument.Descendants(ns + "Podmiot3").Single();

        Assert.Equal("1", podmiot.Element(ns + "RolaInna")!.Value);
        Assert.Equal("Zarządca budynku", podmiot.Element(ns + "OpisRoli")!.Value);
        Assert.Null(podmiot.Element(ns + "Rola"));
    }

    /// <summary>Dokument z podmiotem trzecim musi przejść schemat Ministerstwa.</summary>
    [Fact]
    public void DokumentZPodmiotamiPrzechodziSchemat()
    {
        Faktura faktura = ZOdbiorca();

        faktura.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot
            {
                Nazwa = "Druga Spółka sp. z o.o.",
                Nip = "7010001453",
                Adres = new Adres { Linia1 = "ul. Boczna 3", Linia2 = "00-003 Warszawa" }
            },
            Rola = RolaPodmiotu.DodatkowyNabywca,
            Udzial = 40m,
            NrKlienta = "K-2026-114"
        });

        IReadOnlyList<string> bledy =
            Fabryka.BledyWalidacjiXsd(Fa3Generator.ZbudujXml(faktura));

        Assert.Empty(bledy);
    }

    [Fact]
    public void RolaWlasnaPrzechodziSchemat()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Zarządca nieruchomości", Nip = "1180000001" },
            OpisRoli = "Zarządca budynku"
        });

        Assert.Empty(Fabryka.BledyWalidacjiXsd(Fa3Generator.ZbudujXml(faktura)));
    }

    /// <summary>Podmioty wracają z pliku razem z rolą i udziałem.</summary>
    [Fact]
    public void OdczytOddajePodmiotyZRolami()
    {
        Faktura zrodlo = ZOdbiorca();
        zrodlo.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Faktoring S.A.", Nip = "7010001453" },
            Rola = RolaPodmiotu.Faktor
        });
        zrodlo.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Drugi nabywca sp. j.", Nip = "5252248481" },
            Rola = RolaPodmiotu.DodatkowyNabywca,
            Udzial = 25m,
            NrKlienta = "K-77"
        });

        Faktura odczytana =
            Fa3Czytnik.Odczytaj(Fa3Generator.ZbudujXml(zrodlo));

        Assert.Equal(3, odczytana.PodmiotyInne.Count);

        Assert.Equal(RolaPodmiotu.Odbiorca, odczytana.PodmiotyInne[0].Rola);
        Assert.Equal("Oddział w Krakowie", odczytana.PodmiotyInne[0].Dane.Nazwa);

        Assert.Equal(RolaPodmiotu.Faktor, odczytana.PodmiotyInne[1].Rola);

        Assert.Equal(RolaPodmiotu.DodatkowyNabywca, odczytana.PodmiotyInne[2].Rola);
        Assert.Equal(25m, odczytana.PodmiotyInne[2].Udzial);
        Assert.Equal("K-77", odczytana.PodmiotyInne[2].NrKlienta);
    }

    [Fact]
    public void OdczytOddajeRoleWlasna()
    {
        Faktura zrodlo = Fabryka.PrzykladowaFaktura();
        zrodlo.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Zarządca", Nip = "1180000001" },
            OpisRoli = "Zarządca budynku"
        });

        Faktura odczytana = Fa3Czytnik.Odczytaj(Fa3Generator.ZbudujXml(zrodlo));

        PodmiotInny podmiot = Assert.Single(odczytana.PodmiotyInne);

        Assert.Null(podmiot.Rola);
        Assert.Equal("Zarządca budynku", podmiot.OpisRoli);
        Assert.Equal("Zarządca budynku", podmiot.NazwaRoli);
    }

    // ----------------------------------------------------------- walidacja

    /// <summary>
    /// Podmiot bez roli nie przechodzi.
    /// </summary>
    /// <remarks>
    /// Najważniejsza reguła w tym pliku. To rola rozstrzyga, po co ten podmiot
    /// jest na fakturze - i to według niej KSeF udostępnia dokument. Bez niej
    /// schemat odrzuci plik, a użytkownik zobaczy błąd dopiero po wysyłce.
    /// </remarks>
    [Fact]
    public void PodmiotBezRoliJestBledem()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Ktoś", Nip = "1180000001" }
        });

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.True(wynik.SaBledy);
        Assert.Contains(wynik.Problemy, p => p.Pole.Contains("Rola", StringComparison.Ordinal));
    }

    [Fact]
    public void RolaZListyIOpisWlasnyWykluczajaSie()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Ktoś", Nip = "1180000001" },
            Rola = RolaPodmiotu.Faktor,
            OpisRoli = "i jeszcze coś"
        });

        Assert.True(Walidator.SprawdzFakture(faktura).SaBledy);
    }

    [Fact]
    public void BlednyNipPodmiotuJestWychwytywany()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.PodmiotyInne.Add(new PodmiotInny
        {
            Dane = new Podmiot { Nazwa = "Ktoś", Nip = "1234567890" },
            Rola = RolaPodmiotu.Odbiorca
        });

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.Contains(wynik.Problemy, p => p.Pole.Contains("NIP", StringComparison.Ordinal));
    }

    /// <summary>
    /// Udziały dodatkowych nabywców nie mogą przekroczyć całości.
    /// </summary>
    /// <remarks>
    /// Reszta ponad sumę udziałów przypada nabywcy z faktury - przy sumie
    /// powyżej stu procent wychodziłby udział ujemny.
    /// </remarks>
    [Fact]
    public void UdzialyPonadStoProcentSaBledem()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();

        foreach (decimal udzial in new[] { 60m, 55m })
        {
            faktura.PodmiotyInne.Add(new PodmiotInny
            {
                Dane = new Podmiot { Nazwa = "Nabywca", Nip = "1180000001" },
                Rola = RolaPodmiotu.DodatkowyNabywca,
                Udzial = udzial
            });
        }

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.True(wynik.SaBledy);
        Assert.Contains(wynik.Problemy,
            p => p.Komunikat.Contains("100%", StringComparison.Ordinal));
    }

    [Fact]
    public void PoprawnyPodmiotNiePsujeFaktury()
    {
        Assert.False(Walidator.SprawdzFakture(ZOdbiorca()).SaBledy);
    }

    /// <summary>Zwykła faktura nie ma sekcji podmiotów trzecich.</summary>
    [Fact]
    public void ZwyklaFakturaNieMaPodmiotowTrzecich()
    {
        XDocument dokument = XDocument.Parse(System.Text.Encoding.UTF8.GetString(
            Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura())));

        XNamespace ns = dokument.Root!.GetDefaultNamespace();

        Assert.Empty(dokument.Descendants(ns + "Podmiot3"));
    }

    private static Faktura ZOdbiorca()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();

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

        return faktura;
    }
}
