using System.Text;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using FirmaPro.Wydruk;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FirmaPro.Testy;

/// <summary>
/// Wizualizacja faktury w PDF.
/// </summary>
/// <remarks>
/// Wyglądu wydruku nie da się sprawdzić testem - to trzeba obejrzeć. Testy
/// pilnują więc rzeczy sprawdzalnych: czy plik jest poprawnym dokumentem PDF,
/// czy długa faktura rozkłada się na strony, czy zakończenie nie zostaje
/// oderwane od reszty i czy kod QR trafia na wydruk wyłącznie wtedy, gdy
/// naprawdę prowadzi do dokumentu w KSeF.
/// </remarks>
public sealed class TestyWydruku
{
    [Fact]
    public void WydrukJestPoprawnymDokumentemPdf()
    {
        byte[] pdf = WydrukFaktury.Utworz(Fabryka.PrzykladowaFaktura());

        Assert.NotEmpty(pdf);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);

        using PdfDocument dokument = Otworz(pdf);
        Assert.Equal(1, dokument.PageCount);
    }

    /// <summary>
    /// Faktura z wieloma pozycjami rozkłada się na kolejne strony.
    /// </summary>
    [Fact]
    public void DlugaFakturaZajmujeWiecejNizJednaStrone()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        DodajPozycje(faktura, 60);

        using PdfDocument dokument = Otworz(WydrukFaktury.Utworz(faktura));

        Assert.True(dokument.PageCount > 1,
            "Faktura z 60 pozycjami powinna zająć więcej niż jedną stronę.");
    }

    /// <summary>
    /// Liczba stron rośnie wraz z liczbą pozycji, a nie skacze bez powodu.
    /// </summary>
    /// <remarks>
    /// Test wyłapuje przypadek, w którym zakończenie faktury zostawało samo
    /// na ostatniej stronie: przy 45 pozycjach powstawała pusta kartka
    /// z jedną linijką stopki.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(46)]
    [InlineData(80)]
    public void OstatniaStronaNieJestPrawiePusta(int pozycji)
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        DodajPozycje(faktura, pozycji);

        byte[] pdf = WydrukFaktury.Utworz(faktura, OpcjeWydruku.DlaPrzyjetejZeSkrotu(
            "5252248481-20260808-01A2B3-C4D5E6-70", "5252248481",
            faktura.DataWystawienia, PrzykladowySkrot, SrodowiskoKsef.Test));

        using PdfDocument dokument = Otworz(pdf);

        // Zakończenie (podsumowanie, kwota do zapłaty, kod QR) zajmuje około
        // 90 mm. Gdyby trafiło na osobną stronę razem z czymkolwiek jeszcze,
        // strumień treści ostatniej strony byłby wyraźnie krótszy.
        int dlugoscOstatniej = DlugoscTresci(dokument.Pages[dokument.PageCount - 1]);

        Assert.True(dlugoscOstatniej > 1500,
            $"Ostatnia strona przy {pozycji} pozycjach ma tylko {dlugoscOstatniej} " +
            "bajtów treści - wygląda na osieroconą.");
    }

    /// <summary>
    /// Projekt nie dostaje kodu QR.
    /// </summary>
    /// <remarks>
    /// Kod prowadzący do dokumentu, którego KSeF nie zna, byłby gorszy niż
    /// jego brak - odbiorca zeskanowałby go i zobaczył pustkę, nie wiedząc,
    /// czy to wina faktury, czy systemu.
    /// </remarks>
    [Fact]
    public void ProjektNieMaKoduWeryfikacyjnego()
    {
        OpcjeWydruku opcje = OpcjeWydruku.DlaProjektu();

        Assert.Null(opcje.LinkWeryfikacyjny);
        Assert.Null(opcje.NumerKsef);
        Assert.True(opcje.Projekt);
    }

    /// <summary>
    /// Wydruk projektu jest wyraźnie krótszy - brakuje mu bloku z kodem QR.
    /// </summary>
    [Fact]
    public void WydrukPrzyjetejMaWiecejTresciNizProjekt()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();

        int projekt = DlugoscPierwszejStrony(
            WydrukFaktury.Utworz(faktura, OpcjeWydruku.DlaProjektu()));

        int przyjeta = DlugoscPierwszejStrony(
            WydrukFaktury.Utworz(faktura, OpcjeWydruku.DlaPrzyjetejZeSkrotu(
                "5252248481-20260808-01A2B3-C4D5E6-70", "5252248481",
                faktura.DataWystawienia, PrzykladowySkrot, SrodowiskoKsef.Test)));

        // Sam kod QR to setki prostokątów, więc różnica jest znaczna.
        Assert.True(przyjeta > projekt + 2000,
            $"Wydruk z kodem QR ({przyjeta} B) powinien być wyraźnie dłuższy " +
            $"niż projekt ({projekt} B).");
    }

    /// <summary>
    /// Link z zapisanego skrótu jest taki sam jak liczony z pliku XML.
    /// </summary>
    /// <remarks>
    /// To dowód, że wizualizacja wystawiona po czasie prowadzi dokładnie do
    /// tego dokumentu, który trafił do KSeF - mimo że pliku XML już nie mamy.
    /// </remarks>
    [Fact]
    public void LinkZeSkrotuZgadzaSieZLinkiemZPliku()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        byte[] xml = Fa3Generator.ZbudujXml(faktura);

        string zPliku = KodyQr.LinkWeryfikacyjny(
            "5252248481", faktura.DataWystawienia, xml, SrodowiskoKsef.Test);

        string zeSkrotu = KodyQr.LinkWeryfikacyjnyZeSkrotu(
            "5252248481", faktura.DataWystawienia,
            Kryptografia.SkrotBase64(xml), SrodowiskoKsef.Test);

        Assert.Equal(zPliku, zeSkrotu);
    }

    [Fact]
    public void SkrotOZlejDlugosciJestOdrzucany()
    {
        ArgumentException blad = Assert.Throws<ArgumentException>(() =>
            KodyQr.LinkWeryfikacyjnyZeSkrotu("5252248481", new DateOnly(2026, 8, 8),
                Convert.ToBase64String([1, 2, 3]), SrodowiskoKsef.Test));

        Assert.Contains("32 bajty", blad.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Faktura ze wszystkimi wariantami danych też się drukuje.
    /// </summary>
    /// <remarks>
    /// Nabywca bez NIP-u, stawka zwolniona z podstawą prawną, oznaczenia
    /// GTU i PKWiU - każdy z tych przypadków dokłada coś do układu strony.
    /// </remarks>
    [Fact]
    public void NietypowaFakturaTakzeSieDrukuje()
    {
        var faktura = new Faktura
        {
            Numer = "FV/2026/08/7",
            DataWystawienia = new DateOnly(2026, 8, 8),
            Sprzedawca = Fabryka.PrzykladowaFaktura().Sprzedawca,
            Nabywca = new Podmiot
            {
                Nazwa = "Jan Kowalski",
                Adres = new Adres { Linia1 = "ul. Kwiatowa 5", Linia2 = "00-001 Warszawa" }
            },
            PodstawaZwolnienia = "art. 113 ust. 1 ustawy o VAT",
            Platnosc = new WarunkiPlatnosci
            {
                Forma = FormaPlatnosci.Gotowka,
                Zaplacono = true,
                DataZaplaty = new DateOnly(2026, 8, 8)
            },
            Pozycje =
            [
                new PozycjaFaktury
                {
                    Nazwa = "Usługa zwolniona z podatku",
                    Ilosc = 1m,
                    CenaNetto = 500m,
                    Stawka = StawkaVat.Zwolniona,
                    Pkwiu = "62.01.11.0",
                    Gtu = "GTU_12"
                }
            ]
        };

        using PdfDocument dokument = Otworz(WydrukFaktury.Utworz(faktura));
        Assert.Equal(1, dokument.PageCount);
    }

    /// <summary>Faktura końcowa ze wskazanymi zaliczkami też się drukuje.</summary>
    /// <remarks>
    /// Każda zaliczka dokłada wiersz w bloku płatności, a ten blok musi się
    /// zmieścić razem z podsumowaniem - inaczej zakończenie faktury zostałoby
    /// oderwane od reszty.
    /// </remarks>
    [Fact]
    public void FakturaKoncowaZZaliczkamiSieDrukuje()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Rodzaj = RodzajFaktury.Rozliczeniowa;
        faktura.Zaliczkowe =
        [
            new DaneZaliczki("FV/2026/07/9", new DateOnly(2026, 7, 15), null, 1230m),
            new DaneZaliczki("FV/2026/06/3", new DateOnly(2026, 6, 10), null, 615m)
        ];

        using PdfDocument dokument = Otworz(WydrukFaktury.Utworz(faktura));
        Assert.Equal(1, dokument.PageCount);
    }

    /// <summary>
    /// Kod QR jest zarazem odnośnikiem, który da się kliknąć.
    /// </summary>
    /// <remarks>
    /// Na papierze działa sam kod, ale fakturę częściej ogląda się na ekranie
    /// - a wtedy skanowanie własnego monitora telefonem jest drogą naokoło.
    /// </remarks>
    [Fact]
    public void KodWeryfikacyjnyJestKlikalnymOdnosnikiem()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        byte[] xml = Fa3Generator.ZbudujXml(faktura);

        OpcjeWydruku opcje = OpcjeWydruku.DlaPrzyjetej(
            "5252248481-20260808-010080DD2B5E-26", "5252248481",
            faktura.DataWystawienia, xml, SrodowiskoKsef.Test);

        using PdfDocument dokument = Otworz(WydrukFaktury.Utworz(faktura, opcje));

        List<string> adresy = [.. Odnosniki(dokument.Pages[^1])];

        // Dwa obszary prowadzą pod ten sam adres: sam kod i podpis obok niego.
        Assert.Equal(2, adresy.Count);
        Assert.All(adresy, a => Assert.Equal(opcje.LinkWeryfikacyjny, a));
    }

    [Fact]
    public void ProjektNieMaZadnegoOdnosnika()
    {
        using PdfDocument dokument =
            Otworz(WydrukFaktury.Utworz(Fabryka.PrzykladowaFaktura()));

        Assert.Empty(Odnosniki(dokument.Pages[^1]));
    }

    /// <summary>
    /// Faktura walutowa drukuje więcej niż złotowa - o kurs i podatek w złotych.
    /// </summary>
    /// <remarks>
    /// Treści wydruku nie da się odczytać z gotowego pliku: kroje pisma są
    /// osadzone w podzbiorze, a strumień poleceń skompresowany. Sprawdzamy
    /// więc to, co widać z zewnątrz - że dokument walutowy niesie dodatkowe
    /// polecenia rysowania, i że nie pojawiają się one na fakturze złotowej,
    /// gdzie powtarzałyby tę samą kwotę dwa razy.
    /// </remarks>
    [Fact]
    public void FakturaWalutowaDrukujeKursIPodatekWZlotych()
    {
        Faktura zlotowa = Fabryka.PrzykladowaFaktura();

        Faktura walutowa = Fabryka.PrzykladowaFaktura();
        walutowa.Waluta = "EUR";
        walutowa.Kurs = new KursWaluty("EUR", 4.2837m, new DateOnly(2026, 8, 19),
            "160/A/NBP/2026");

        int bezKursu = DlugoscPierwszejStrony(WydrukFaktury.Utworz(zlotowa));
        int zKursem = DlugoscPierwszejStrony(WydrukFaktury.Utworz(walutowa));

        Assert.True(zKursem > bezKursu,
            "Wydruk faktury w euro powinien nieść kurs i kwotę podatku w złotych.");
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>Adresy, pod które prowadzą odnośniki umieszczone na stronie.</summary>
    private static IEnumerable<string> Odnosniki(PdfPage strona)
    {
        PdfArray? adnotacje = strona.Elements.GetArray("/Annots");

        for (int i = 0; i < (adnotacje?.Elements.Count ?? 0); i++)
        {
            if (adnotacje!.Elements.GetDictionary(i) is not { } adnotacja)
            {
                continue;
            }

            if (adnotacja.Elements.GetDictionary("/A") is { } akcja
                && akcja.Elements.GetString("/URI") is { Length: > 0 } adres)
            {
                yield return adres;
            }
        }
    }

    /// <summary>Skrót o poprawnej długości - treść nie ma tu znaczenia.</summary>
    private static string PrzykladowySkrot { get; } =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private static PdfDocument Otworz(byte[] pdf)
    {
        using var pamiec = new MemoryStream(pdf);
        return PdfReader.Open(pamiec, PdfDocumentOpenMode.Import);
    }

    /// <summary>Długość strumienia poleceń rysowania danej strony.</summary>
    private static int DlugoscTresci(PdfPage strona) =>
        strona.Contents.Elements.Sum(element =>
            element is PdfReference odwolanie && odwolanie.Value is PdfDictionary slownik
                ? slownik.Stream?.Length ?? 0
                : 0);

    private static int DlugoscPierwszejStrony(byte[] pdf)
    {
        using PdfDocument dokument = Otworz(pdf);
        return DlugoscTresci(dokument.Pages[0]);
    }

    private static void DodajPozycje(Faktura faktura, int ile)
    {
        faktura.Pozycje.Clear();

        for (int i = 1; i <= ile; i++)
        {
            faktura.Pozycje.Add(new PozycjaFaktury
            {
                Nazwa = $"Pozycja numer {i}",
                Jednostka = "szt.",
                Ilosc = i,
                CenaNetto = 100m + i,
                Stawka = i % 4 == 0 ? StawkaVat.Vat8 : StawkaVat.Vat23
            });
        }
    }
}
