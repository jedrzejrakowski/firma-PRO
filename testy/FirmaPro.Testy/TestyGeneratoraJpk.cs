using System.Globalization;
using System.Xml.Linq;
using System.Xml.Schema;
using FirmaPro.Domena;
using FirmaPro.Jpk;

namespace FirmaPro.Testy;

/// <summary>
/// Plik JPK_V7M.
/// </summary>
/// <remarks>
/// Testy sprawdzają to, co da się sprawdzić bez oficjalnego schematu:
/// obecność wymaganych elementów, zgodność sum kontrolnych z zawartością
/// ewidencji i to, że kwoty z rejestru trafiają do właściwych pól. Sam
/// kształt dokumentu potwierdzi dopiero walidacja schematem - patrz
/// <see cref="PlikPrzechodziWalidacjeOficjalnymSchematem"/>.
/// </remarks>
public sealed class TestyGeneratoraJpk
{
    private static readonly OkresRozliczeniowy Sierpien = OkresRozliczeniowy.Miesiac(2026, 8);
    private static readonly XNamespace Tns = GeneratorJpk.PrzestrzenNazw;

    [Fact]
    public void NaglowekNiesieOkresIDanePodatnika()
    {
        XDocument dokument = Zbuduj();
        XElement naglowek = dokument.Root!.Element(Tns + "Naglowek")!;

        Assert.Equal("JPK_V7M", naglowek.Element(Tns + "KodFormularza")!.Value);
        Assert.Equal("3", naglowek.Element(Tns + "WariantFormularza")!.Value);
        Assert.Equal("2026", naglowek.Element(Tns + "Rok")!.Value);
        Assert.Equal("8", naglowek.Element(Tns + "Miesiac")!.Value);
        Assert.Equal("1", naglowek.Element(Tns + "CelZlozenia")!.Value);
        Assert.Equal("1471", naglowek.Element(Tns + "KodUrzedu")!.Value);

        XElement podmiot = dokument.Root!.Element(Tns + "Podmiot1")!;
        Assert.Equal("Podatnik", podmiot.Attribute("rola")!.Value);
        Assert.Contains("5252248481", podmiot.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void KorektaMaInnyCelZlozenia()
    {
        XDocument dokument = Zbuduj(cel: CelZlozenia.Korekta);

        Assert.Equal("2",
            dokument.Root!.Element(Tns + "Naglowek")!.Element(Tns + "CelZlozenia")!.Value);
    }

    /// <summary>
    /// Deklaracja niesie pola w pełnych złotych, ewidencja - w groszach.
    /// </summary>
    [Fact]
    public void DeklaracjaWZlotychEwidencjaWGroszach()
    {
        XDocument dokument = Zbuduj();

        XElement pozycje = dokument.Root!
            .Element(Tns + "Deklaracja")!
            .Element(Tns + "PozycjeSzczegolowe")!;

        Assert.Equal("10000", pozycje.Element(Tns + "P_19")!.Value);
        Assert.Equal("2300", pozycje.Element(Tns + "P_20")!.Value);
        Assert.DoesNotContain(".", pozycje.Element(Tns + "P_19")!.Value,
            StringComparison.Ordinal);

        XElement wiersz = dokument.Root!
            .Element(Tns + "Ewidencja")!
            .Element(Tns + "SprzedazWiersz")!;

        Assert.Equal("10000.00", wiersz.Element(Tns + "K_19")!.Value);
        Assert.Equal("2300.00", wiersz.Element(Tns + "K_20")!.Value);
    }

    /// <summary>
    /// Suma kontrolna sprzedaży musi zgadzać się z zawartością ewidencji.
    /// </summary>
    /// <remarks>
    /// To pierwsza rzecz, którą sprawdza urząd po wczytaniu pliku.
    /// Rozjazd oznacza odrzucenie całego dokumentu.
    /// </remarks>
    [Fact]
    public void SumaKontrolnaSprzedazyZgadzaSieZWierszami()
    {
        XDocument dokument = Zbuduj();
        XElement ewidencja = dokument.Root!.Element(Tns + "Ewidencja")!;

        List<XElement> wiersze = ewidencja.Elements(Tns + "SprzedazWiersz").ToList();
        XElement kontrolka = ewidencja.Element(Tns + "SprzedazCtrl")!;

        Assert.Equal(wiersze.Count.ToString(CultureInfo.InvariantCulture),
            kontrolka.Element(Tns + "LiczbaWierszySprzedazy")!.Value);

        decimal podatekZWierszy = wiersze
            .SelectMany(w => w.Elements())
            .Where(e => e.Name.LocalName is "K_16" or "K_18" or "K_20")
            .Sum(e => decimal.Parse(e.Value, CultureInfo.InvariantCulture));

        Assert.Equal(podatekZWierszy,
            decimal.Parse(kontrolka.Element(Tns + "PodatekNalezny")!.Value,
                CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SumaKontrolnaZakupowZgadzaSieZWierszami()
    {
        XDocument dokument = Zbuduj();
        XElement ewidencja = dokument.Root!.Element(Tns + "Ewidencja")!;

        List<XElement> wiersze = ewidencja.Elements(Tns + "ZakupWiersz").ToList();
        XElement kontrolka = ewidencja.Element(Tns + "ZakupCtrl")!;

        Assert.Equal(wiersze.Count.ToString(CultureInfo.InvariantCulture),
            kontrolka.Element(Tns + "LiczbaWierszyZakupow")!.Value);

        decimal podatekZWierszy = wiersze
            .SelectMany(w => w.Elements())
            .Where(e => e.Name.LocalName is "K_41" or "K_43")
            .Sum(e => decimal.Parse(e.Value, CultureInfo.InvariantCulture));

        Assert.Equal(podatekZWierszy,
            decimal.Parse(kontrolka.Element(Tns + "PodatekNaliczony")!.Value,
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Zakup bez prawa do odliczenia nie trafia do pliku dla urzędu.
    /// </summary>
    /// <remarks>
    /// W ewidencji zakupów wykazuje się nabycia dające prawo do odliczenia.
    /// Zakup na cele prywatne zostaje w rejestrze firmy jako dokument, ale
    /// w pliku przekazywanym urzędowi go nie ma - i nie może wchodzić do sumy
    /// kontrolnej, bo ta musi opisywać zawartość pliku, a nie rejestru.
    /// </remarks>
    [Fact]
    public void ZakupBezOdliczeniaNieTrafiaDoPliku()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien, [],
        [
            Zakup("FZ/1", 1000m, RodzajZakupu.TowaryIUslugi, odliczany: true),
            Zakup("FZ/2", 300m, RodzajZakupu.TowaryIUslugi, odliczany: false)
        ]);

        XDocument dokument = GeneratorJpk.ZbudujDokument(
            DeklaracjaVat.Zbuduj(rejestr), rejestr, Dane());

        XElement ewidencja = dokument.Root!.Element(Tns + "Ewidencja")!;
        List<XElement> wiersze = ewidencja.Elements(Tns + "ZakupWiersz").ToList();

        Assert.Single(wiersze);
        Assert.Equal("FZ/1", wiersze[0].Element(Tns + "DowodZakupu")!.Value);

        Assert.Equal("1", ewidencja.Element(Tns + "ZakupCtrl")!
            .Element(Tns + "LiczbaWierszyZakupow")!.Value);

        Assert.DoesNotContain("FZ/2", dokument.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NumerKsefTrafiaDoEwidencjiGdyJestZnany()
    {
        XDocument zNumerem = Zbuduj(numerKsef: "5252248481-20260810-01A2B3-C4D5E6-70");
        XDocument bezNumeru = Zbuduj();

        XElement wiersz = zNumerem.Root!
            .Element(Tns + "Ewidencja")!.Element(Tns + "SprzedazWiersz")!;

        Assert.Equal("5252248481-20260810-01A2B3-C4D5E6-70",
            wiersz.Element(Tns + "NrKSeF")!.Value);

        Assert.Null(bezNumeru.Root!
            .Element(Tns + "Ewidencja")!.Element(Tns + "SprzedazWiersz")!
            .Element(Tns + "NrKSeF"));
    }

    [Fact]
    public void NabywcaBezNipMaOznaczenieBrak()
    {
        var dzien = new DateOnly(2026, 8, 10);

        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien,
        [
            new WpisSprzedazy("FV/1", dzien, dzien, "Jan Kowalski", null, null,
                [new KwotyWStawce(StawkaVat.Vat23, 100m, 23m)])
        ], []);

        XDocument dokument = GeneratorJpk.ZbudujDokument(
            DeklaracjaVat.Zbuduj(rejestr), rejestr, Dane());

        Assert.Equal("BRAK", dokument.Root!
            .Element(Tns + "Ewidencja")!.Element(Tns + "SprzedazWiersz")!
            .Element(Tns + "NrKontrahenta")!.Value);
    }

    [Fact]
    public void RejestrZInnegoOkresuJestOdrzucany()
    {
        RejestrVat sierpien = RejestrVat.Zbuduj(Sierpien, [], []);
        RejestrVat wrzesien = RejestrVat.Zbuduj(OkresRozliczeniowy.Miesiac(2026, 9), [], []);

        Assert.Throws<ArgumentException>(() =>
            GeneratorJpk.ZbudujDokument(DeklaracjaVat.Zbuduj(sierpien), wrzesien, Dane()));
    }

    [Fact]
    public void PlikJestPoprawnymDokumentemXml()
    {
        byte[] plik = GeneratorJpk.ZbudujPlik(
            DeklaracjaVat.Zbuduj(PrzykladowyRejestr()), PrzykladowyRejestr(), Dane());

        Assert.NotEmpty(plik);

        // Odczytanie z powrotem dowodzi, że dokument jest dobrze uformowany.
        using var pamiec = new MemoryStream(plik);
        XDocument odczytany = XDocument.Load(pamiec);

        Assert.Equal(Tns + "JPK", odczytany.Root!.Name);
    }

    /// <summary>
    /// Walidacja pliku oficjalnym schematem Ministerstwa Finansów.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Test uruchamia się dopiero po umieszczeniu schematu w katalogu
    /// <c>schematy/</c>. Bez niego zgłasza się jako <b>pominięty</b>,
    /// a nie zaliczony - brak sprawdzenia nie może wyglądać jak sprawdzenie.
    /// </para>
    /// <para>
    /// Schemat pobiera się ze strony Ministerstwa Finansów (struktury JPK)
    /// i zapisuje pod nazwą zaczynającą się od <c>Schemat_JPK_V7M</c>.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void PlikPrzechodziWalidacjeOficjalnymSchematem()
    {
        string? schemat = ZnajdzSchematJpk();

        Skip.If(schemat is null,
            "Brak schematu JPK_V7M w katalogu schematy/ - pobierz go ze strony " +
            "Ministerstwa Finansów (Struktury JPK) i zapisz pod nazwą " +
            "zaczynającą się od Schemat_JPK_V7M, żeby ten test zaczął działać.");

        var zbior = new XmlSchemaSet();
        zbior.Add(GeneratorJpk.PrzestrzenNazw, schemat);
        zbior.Compile();

        RejestrVat rejestr = PrzykladowyRejestr();
        XDocument dokument = GeneratorJpk.ZbudujDokument(
            DeklaracjaVat.Zbuduj(rejestr), rejestr, Dane());

        var bledy = new List<string>();
        dokument.Validate(zbior, (_, argumenty) => bledy.Add(argumenty.Message));

        Assert.Empty(bledy);
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>Szuka schematu JPK, idąc w górę drzewa katalogów.</summary>
    private static string? ZnajdzSchematJpk()
    {
        var katalog = new DirectoryInfo(AppContext.BaseDirectory);

        while (katalog is not null)
        {
            string sciezka = Path.Combine(katalog.FullName, "schematy");
            if (Directory.Exists(sciezka))
            {
                return Directory.EnumerateFiles(sciezka, "Schemat_JPK_V7M*.xsd")
                                .OrderByDescending(p => p, StringComparer.Ordinal)
                                .FirstOrDefault();
            }

            katalog = katalog.Parent;
        }

        return null;
    }

    private static DanePliku Dane(CelZlozenia cel = CelZlozenia.Pierwotny) => new()
    {
        Nip = "5252248481",
        Nazwa = "Moja Firma sp. z o.o.",
        KodUrzedu = "1471",
        Email = "biuro@example.pl",
        Cel = cel,
        DataWytworzenia = new DateTimeOffset(2026, 9, 10, 8, 30, 0, TimeSpan.Zero)
    };

    private static XDocument Zbuduj(CelZlozenia cel = CelZlozenia.Pierwotny,
                                    string? numerKsef = null)
    {
        RejestrVat rejestr = PrzykladowyRejestr(numerKsef);
        return GeneratorJpk.ZbudujDokument(
            DeklaracjaVat.Zbuduj(rejestr), rejestr, Dane(cel));
    }

    private static RejestrVat PrzykladowyRejestr(string? numerKsef = null)
    {
        var dzien = new DateOnly(2026, 8, 10);

        return RejestrVat.Zbuduj(Sierpien,
        [
            new WpisSprzedazy("FV/2026/08/1", dzien, dzien,
                "Auto-Serwis Nowak sp. j.", "1180000001", numerKsef,
                [
                    new KwotyWStawce(StawkaVat.Vat23, 10000m, 2300m),
                    new KwotyWStawce(StawkaVat.Vat8, 1000m, 80m)
                ])
        ],
        [
            Zakup("FZ/2026/08/17", 2400m, RodzajZakupu.TowaryIUslugi),
            Zakup("FZ/LAPTOP/9", 6000m, RodzajZakupu.SrodkiTrwale)
        ]);
    }

    private static WpisZakupu Zakup(string numer, decimal netto, RodzajZakupu rodzaj,
                                    bool odliczany = true)
    {
        var dzien = new DateOnly(2026, 8, 6);

        return new WpisZakupu(numer, dzien, dzien, dzien, "Dostawca sp. z o.o.",
            "1180000001", rodzaj, odliczany, netto, StawkaVat.Vat23.PodatekOd(netto));
    }
}
