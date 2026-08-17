using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Podatkowa księga przychodów i rozchodów.
/// </summary>
/// <remarks>
/// Testy pilnują reguł z rozporządzenia i z ustawy o PIT, a nie samego
/// działania kodu. Księga, która pokazuje liczby niezgodne z przepisem, jest
/// gorsza niż jej brak - bo na jej podstawie ktoś zapłaci zaliczkę.
/// </remarks>
public sealed class TestyKsiegi
{
    private static readonly OkresRozliczeniowy Sierpien = OkresRozliczeniowy.Miesiac(2026, 8);

    private static WpisKsiegi Wpis(KolumnaKpir kolumna, decimal kwota,
                                   int dzien = 12, int miesiac = 8) =>
        new(new DateOnly(2026, miesiac, dzien), $"FV/{miesiac}/{dzien}",
            "Kontrahent sp. z o.o.", "ul. Testowa 1", "Zdarzenie", kolumna, kwota);

    [Fact]
    public void KsiegaBierzeTylkoZapisyOkresu()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien,
        [
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 1000m, miesiac: 7),
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 2000m, miesiac: 8),
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 3000m, miesiac: 9)
        ]);

        Assert.Single(ksiega.Wpisy);
        Assert.Equal(2000m, ksiega.Kolumna(KolumnaKpir.SprzedazTowarowIUslug));
    }

    /// <summary>Kolumna 9 to suma kolumn 7 i 8.</summary>
    [Fact]
    public void RazemPrzychodSumujeSprzedazIPozostale()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien,
        [
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 5000m),
            Wpis(KolumnaKpir.PozostalePrzychody, 120.50m)
        ]);

        Assert.Equal(5120.50m, ksiega.RazemPrzychod);
    }

    /// <summary>
    /// Kolumna 14 sumuje wyłącznie wynagrodzenia i pozostałe wydatki.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Zakup towarów handlowych (kolumna 10)
    /// i koszty uboczne (11) stoją poza tą sumą, bo rozlicza się je przez spis
    /// z natury. Dodanie ich zawyżyłoby koszty o wartość towaru, który leży
    /// jeszcze w magazynie - i zaniżyłoby dochód.
    /// </remarks>
    [Fact]
    public void RazemWydatkiNieObejmujeZakupuTowarow()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien,
        [
            Wpis(KolumnaKpir.ZakupTowarow, 10_000m),
            Wpis(KolumnaKpir.KosztyUboczneZakupu, 500m),
            Wpis(KolumnaKpir.Wynagrodzenia, 4000m),
            Wpis(KolumnaKpir.PozostaleWydatki, 1200m)
        ]);

        Assert.Equal(5200m, ksiega.RazemWydatki);

        // Same kolumny pozostają widoczne - nie chodzi o ich pominięcie,
        // tylko o to, żeby nie wchodziły do sumy z kolumny 14.
        Assert.Equal(10_000m, ksiega.Kolumna(KolumnaKpir.ZakupTowarow));
        Assert.Equal(500m, ksiega.Kolumna(KolumnaKpir.KosztyUboczneZakupu));
    }

    /// <summary>
    /// Sumy narastające liczy się od początku roku.
    /// </summary>
    /// <remarks>
    /// Zaliczkę na podatek dochodowy ustala się od dochodu narastająco
    /// (art. 44 ust. 3 ustawy o PIT), a nie od dochodu samego miesiąca.
    /// </remarks>
    [Fact]
    public void NarastajacoLiczySieOdPoczatkuRoku()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien,
        [
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 1000m, miesiac: 1),
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 2000m, miesiac: 5),
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 3000m, miesiac: 8),
            Wpis(KolumnaKpir.PozostaleWydatki, 600m, miesiac: 3),
            Wpis(KolumnaKpir.PozostaleWydatki, 400m, miesiac: 8)
        ]);

        Assert.Equal(3000m, ksiega.Kolumna(KolumnaKpir.SprzedazTowarowIUslug));
        Assert.Equal(6000m, ksiega.PrzychodNarastajaco);
        Assert.Equal(1000m, ksiega.WydatkiNarastajaco);
        Assert.Equal(5000m, ksiega.DochodNarastajaco);
    }

    /// <summary>Miesiące po okresie nie wchodzą do sum narastających.</summary>
    [Fact]
    public void NarastajacoNieSiegaWPrzyszlosc()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien,
        [
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 1000m, miesiac: 8),
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 9999m, miesiac: 12)
        ]);

        Assert.Equal(1000m, ksiega.PrzychodNarastajaco);
    }

    /// <summary>
    /// Zmniejszenie zapisuje się kwotą ujemną.
    /// </summary>
    /// <remarks>
    /// Księga nie zna zapisów czerwonych, więc minus jest jedynym sposobem
    /// pokazania, że przychód ubył - na przykład po korekcie.
    /// </remarks>
    [Fact]
    public void KorektaZmniejszajacaWchodziNaMinus()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien,
        [
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 5000m),
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, -1200m, dzien: 20)
        ]);

        Assert.Equal(3800m, ksiega.RazemPrzychod);
    }

    [Fact]
    public void ZapisyIdaWKolejnosciDat()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien,
        [
            Wpis(KolumnaKpir.PozostaleWydatki, 100m, dzien: 25),
            Wpis(KolumnaKpir.SprzedazTowarowIUslug, 200m, dzien: 3),
            Wpis(KolumnaKpir.PozostaleWydatki, 300m, dzien: 14)
        ]);

        Assert.Equal([3, 14, 25], ksiega.Wpisy.Select(w => w.Data.Day));
    }

    [Fact]
    public void PustyOkresDajeKsiegeBezZapisow()
    {
        Kpir ksiega = Kpir.Zbuduj(Sierpien, []);

        Assert.Empty(ksiega.Wpisy);
        Assert.Equal(0m, ksiega.RazemPrzychod);
        Assert.Equal(0m, ksiega.RazemWydatki);
        Assert.Empty(ksiega.Sumy);
    }

    /// <summary>Numery kolumn muszą zgadzać się z rozporządzeniem.</summary>
    /// <remarks>
    /// Trafiają na wydruk księgi i po nich księgowa sprawdza, czy zapis siedzi
    /// tam, gdzie powinien. Przesunięcie numeracji byłoby niewidoczne w kodzie,
    /// a widoczne dopiero na papierze.
    /// </remarks>
    [Fact]
    public void NumeryKolumnZgadzajaSieZRozporzadzeniem()
    {
        Assert.Equal(7, Kolumny.Numer(KolumnaKpir.SprzedazTowarowIUslug));
        Assert.Equal(8, Kolumny.Numer(KolumnaKpir.PozostalePrzychody));
        Assert.Equal(10, Kolumny.Numer(KolumnaKpir.ZakupTowarow));
        Assert.Equal(11, Kolumny.Numer(KolumnaKpir.KosztyUboczneZakupu));
        Assert.Equal(12, Kolumny.Numer(KolumnaKpir.Wynagrodzenia));
        Assert.Equal(13, Kolumny.Numer(KolumnaKpir.PozostaleWydatki));
        Assert.Equal(15, Kolumny.Numer(KolumnaKpir.BadaniaIRozwoj));
    }

    [Fact]
    public void PrzychodemSaTylkoKolumnySiedemIOsiem()
    {
        Assert.True(Kolumny.Przychod(KolumnaKpir.SprzedazTowarowIUslug));
        Assert.True(Kolumny.Przychod(KolumnaKpir.PozostalePrzychody));

        foreach (KolumnaKpir kolumna in Kolumny.Kosztowe)
        {
            Assert.False(Kolumny.Przychod(kolumna));
        }
    }
}

/// <summary>
/// Ewidencja przychodów przy ryczałcie.
/// </summary>
/// <remarks>
/// Inny dokument niż księga i inne reguły: nie ma kosztów, ale jest podział
/// na stawki, a każda stawka daje osobny podatek.
/// </remarks>
public sealed class TestyRyczaltu
{
    private static readonly OkresRozliczeniowy Sierpien = OkresRozliczeniowy.Miesiac(2026, 8);

    private static WpisRyczaltu Wpis(decimal stawka, decimal kwota, int miesiac = 8) =>
        new(new DateOnly(2026, miesiac, 12), $"FV/{miesiac}/1", "Usługa", stawka, kwota);

    /// <summary>
    /// Podatek liczy się osobno w każdej stawce.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Suma podatku nie jest podatkiem od sumy
    /// przychodów: 10 000 zł na 12% i 10 000 zł na 8,5% to 1200 + 850 = 2050 zł,
    /// a nie podatek od 20 000 zł jedną stawką. Przy działalności mieszanej
    /// pomyłka tutaj daje kwotę oderwaną od rzeczywistości.
    /// </remarks>
    [Fact]
    public void PodatekLiczySieOsobnoWKazdejStawce()
    {
        EwidencjaRyczaltu ewidencja = EwidencjaRyczaltu.Zbuduj(Sierpien,
        [
            Wpis(12m, 10_000m),
            Wpis(8.5m, 10_000m)
        ]);

        Assert.Equal(2, ewidencja.WedlugStawek.Count);
        Assert.Equal(20_000m, ewidencja.RazemPrzychod);
        Assert.Equal(2050L, ewidencja.RazemPodatek);
    }

    /// <summary>Podatek zaokrągla się do pełnych złotych.</summary>
    /// <remarks>
    /// Art. 63 § 1 Ordynacji podatkowej. Grosze w podatku byłyby odrzucone
    /// przy przelewie do urzędu.
    /// </remarks>
    [Fact]
    public void PodatekZaokraglaSieDoZlotych()
    {
        // 3333,33 zł przy 8,5% to 283,33305 zł - do 283 zł.
        Assert.Equal(283L, StawkiRyczaltu.Podatek(3333.33m, 8.5m));

        // 1000,50 zł przy 15% to 150,075 zł - do 150 zł.
        Assert.Equal(150L, StawkiRyczaltu.Podatek(1000.50m, 15m));
    }

    [Fact]
    public void StawkiZgadzajaSieZUstawa()
    {
        Assert.Equal([2m, 3m, 5.5m, 8.5m, 10m, 12m, 12.5m, 14m, 15m, 17m],
            StawkiRyczaltu.Wszystkie);

        Assert.True(StawkiRyczaltu.Znana(8.5m));
        Assert.False(StawkiRyczaltu.Znana(9m));
    }

    [Fact]
    public void StawkaZapisujeSiePoPolsku()
    {
        Assert.Equal("8,5%", StawkiRyczaltu.NaTekst(8.5m));
        Assert.Equal("12%", StawkiRyczaltu.NaTekst(12m));
    }

    [Fact]
    public void PodatekNarastajacoLiczyOdPoczatkuRoku()
    {
        EwidencjaRyczaltu ewidencja = EwidencjaRyczaltu.Zbuduj(Sierpien,
        [
            Wpis(12m, 10_000m, miesiac: 3),
            Wpis(12m, 10_000m, miesiac: 8),
            Wpis(12m, 99_999m, miesiac: 11)
        ]);

        Assert.Equal(10_000m, ewidencja.RazemPrzychod);
        Assert.Equal(20_000m, ewidencja.PrzychodNarastajaco);
        Assert.Equal(2400L, ewidencja.PodatekNarastajaco);
    }

    [Fact]
    public void StawkiIdaWKolejnosciRosnacej()
    {
        EwidencjaRyczaltu ewidencja = EwidencjaRyczaltu.Zbuduj(Sierpien,
        [
            Wpis(17m, 1000m),
            Wpis(8.5m, 1000m),
            Wpis(12m, 1000m)
        ]);

        Assert.Equal([8.5m, 12m, 17m], ewidencja.WedlugStawek.Select(s => s.Stawka));
    }

    [Fact]
    public void PustyOkresDajeEwidencjeBezPodatku()
    {
        EwidencjaRyczaltu ewidencja = EwidencjaRyczaltu.Zbuduj(Sierpien, []);

        Assert.Empty(ewidencja.Wpisy);
        Assert.Empty(ewidencja.WedlugStawek);
        Assert.Equal(0L, ewidencja.RazemPodatek);
    }
}

/// <summary>
/// Wydruk księgi.
/// </summary>
/// <remarks>
/// Układu strony testem się nie sprawdzi - to trzeba obejrzeć. Testy pilnują
/// tego, co sprawdzalne: że plik jest poprawnym dokumentem PDF, że długa
/// księga rozkłada się na strony i że wydruk powstaje dla obu form księgi.
/// </remarks>
public sealed class TestyWydrukuKsiegi
{
    private static readonly OkresRozliczeniowy Sierpien = OkresRozliczeniowy.Miesiac(2026, 8);

    private static Kpir Ksiega(int ileZapisow)
    {
        var wpisy = new List<WpisKsiegi>();

        for (int i = 1; i <= ileZapisow; i++)
        {
            wpisy.Add(new WpisKsiegi(
                new DateOnly(2026, 8, Math.Min(i, 28)),
                $"FV/2026/08/{i}",
                "Kontrahent sp. z o.o.",
                "ul. Testowa 1, 00-001 Warszawa",
                "Sprzedaż towarów i usług",
                KolumnaKpir.SprzedazTowarowIUslug,
                1000m + i));
        }

        return Kpir.Zbuduj(Sierpien, wpisy);
    }

    [Fact]
    public void WydrukKsiegiJestPoprawnymPdf()
    {
        byte[] pdf = FirmaPro.Wydruk.WydrukKsiegi.Utworz(
            Ksiega(3), "Moja Firma sp. z o.o.", "5252248481");

        Assert.NotEmpty(pdf);
        Assert.StartsWith("%PDF-",
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);
    }

    /// <summary>Strona jest pozioma - inaczej szesnaście kolumn się nie mieści.</summary>
    [Fact]
    public void StronaJestPozioma()
    {
        byte[] pdf = FirmaPro.Wydruk.WydrukKsiegi.Utworz(
            Ksiega(1), "Moja Firma sp. z o.o.", "5252248481");

        using var pamiec = new MemoryStream(pdf);
        using PdfSharp.Pdf.PdfDocument dokument =
            PdfSharp.Pdf.IO.PdfReader.Open(pamiec, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        Assert.True(dokument.Pages[0].Width > dokument.Pages[0].Height,
            "Księga ma się drukować na arkuszu poziomym.");
    }

    [Fact]
    public void DlugaKsiegaRozkladaSieNaStrony()
    {
        byte[] pdf = FirmaPro.Wydruk.WydrukKsiegi.Utworz(
            Ksiega(120), "Moja Firma sp. z o.o.", "5252248481");

        using var pamiec = new MemoryStream(pdf);
        using PdfSharp.Pdf.PdfDocument dokument =
            PdfSharp.Pdf.IO.PdfReader.Open(pamiec, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);

        Assert.True(dokument.PageCount > 1,
            "Księga ze 120 zapisami powinna zająć więcej niż jedną stronę.");
    }

    [Fact]
    public void EwidencjaRyczaltuTezMaWydruk()
    {
        EwidencjaRyczaltu ewidencja = EwidencjaRyczaltu.Zbuduj(Sierpien,
        [
            new WpisRyczaltu(new DateOnly(2026, 8, 12), "FV/1", "Usługa", 12m, 10_000m),
            new WpisRyczaltu(new DateOnly(2026, 8, 20), "FV/2", "Wykład", 17m, 2_000m)
        ]);

        byte[] pdf = FirmaPro.Wydruk.WydrukKsiegi.Utworz(
            ewidencja, "Moja Firma sp. z o.o.", "5252248481");

        Assert.StartsWith("%PDF-",
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);
    }

    [Fact]
    public void PustaKsiegaTezSieDrukuje()
    {
        byte[] pdf = FirmaPro.Wydruk.WydrukKsiegi.Utworz(
            Kpir.Zbuduj(Sierpien, []), "Moja Firma sp. z o.o.", "5252248481");

        Assert.NotEmpty(pdf);
    }
}
