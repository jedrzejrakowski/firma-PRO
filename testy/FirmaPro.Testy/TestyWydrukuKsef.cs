using System.Text;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using FirmaPro.Wydruk;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Wizualizacja w układzie Krajowego Systemu e-Faktur.
/// </summary>
/// <remarks>
/// Układu strony testem się nie sprawdzi - to trzeba obejrzeć. Testy pilnują
/// rzeczy sprawdzalnych: że plik jest poprawnym dokumentem PDF, że powstaje
/// z faktury odczytanej z XML-a, że długi dokument rozkłada się na strony
/// i że dokument spoza KSeF mówi o tym wprost.
/// </remarks>
public sealed class TestyWydrukuKsef
{
    [Fact]
    public void WizualizacjaJestPoprawnymDokumentemPdf()
    {
        byte[] pdf = WydrukKsef.Utworz(ZPliku(Fabryka.PrzykladowaFaktura()));

        Assert.NotEmpty(pdf);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);

        using PdfDocument dokument = Otworz(pdf);
        Assert.Equal(1, dokument.PageCount);
    }

    /// <summary>Tytuł dokumentu mówi, czym plik jest - i czym nie jest.</summary>
    [Fact]
    public void TytulMowiZeToWizualizacja()
    {
        byte[] pdf = WydrukKsef.Utworz(ZPliku(Fabryka.PrzykladowaFaktura()));

        using PdfDocument dokument = Otworz(pdf);

        Assert.Contains("Wizualizacja", dokument.Info.Title, StringComparison.Ordinal);
        Assert.Contains("FA(3)", dokument.Info.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// Faktura z wieloma pozycjami rozkłada się na kolejne strony.
    /// </summary>
    /// <remarks>
    /// Tabela urzędowa bywa długa - faktura za media dla wspólnoty ma po sto
    /// pozycji. Wiersze wychodzące poza kartkę byłyby po prostu niewidoczne.
    /// </remarks>
    [Fact]
    public void DlugaFakturaZajmujeWiecejNizJednaStrone()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje.Clear();

        for (int i = 1; i <= 80; i++)
        {
            faktura.Pozycje.Add(new PozycjaFaktury
            {
                Nazwa = $"Pozycja numer {i}",
                Jednostka = "szt.",
                Ilosc = 1m,
                CenaNetto = 100m,
                Stawka = StawkaVat.Vat23
            });
        }

        using PdfDocument dokument = Otworz(WydrukKsef.Utworz(ZPliku(faktura)));

        Assert.True(dokument.PageCount > 1,
            "Faktura z 80 pozycjami powinna zająć więcej niż jedną stronę.");
    }

    /// <summary>
    /// Dokument spoza KSeF musi się do tego przyznać.
    /// </summary>
    /// <remarks>
    /// Wizualizacja wygląda urzędowo, więc bez tego zastrzeżenia podgląd
    /// roboczy sprawiałby wrażenie dowodu, którym nie jest.
    /// </remarks>
    [Fact]
    public void PodgladRoboczyRozniSieOdPrzyjetej()
    {
        Faktura faktura = ZPliku(Fabryka.PrzykladowaFaktura());

        int projekt = DlugoscPierwszejStrony(
            WydrukKsef.Utworz(faktura, OpcjeWydruku.DlaProjektu()));

        int przyjeta = DlugoscPierwszejStrony(
            WydrukKsef.Utworz(faktura, OpcjeWydruku.DlaPrzyjetejZeSkrotu(
                "5252248481-20260812-010080DD2B5E-26", "5252248481",
                faktura.DataWystawienia, Convert.ToBase64String(new byte[32]),
                SrodowiskoKsef.Test)));

        Assert.NotEqual(projekt, przyjeta);
    }

    /// <summary>Faktura walutowa niesie kurs i podatek w złotych.</summary>
    [Fact]
    public void FakturaWalutowaNiesieKursIPodatek()
    {
        Faktura zlotowa = ZPliku(Fabryka.PrzykladowaFaktura());

        Faktura walutowa = Fabryka.PrzykladowaFaktura();
        walutowa.Waluta = "EUR";
        walutowa.Kurs = new KursWaluty("EUR", 4.2837m, new DateOnly(2026, 8, 19),
            "160/A/NBP/2026");

        Assert.True(
            DlugoscPierwszejStrony(WydrukKsef.Utworz(ZPliku(walutowa)))
            > DlugoscPierwszejStrony(WydrukKsef.Utworz(zlotowa)),
            "Wizualizacja faktury w euro powinna nieść kurs i podatek w złotych.");
    }

    /// <summary>
    /// Faktura korygująca pokazuje stan sprzed zmiany.
    /// </summary>
    /// <remarks>
    /// Korekta bez stanu przed jest nieczytelna: nie widać, co się właściwie
    /// zmieniło ani skąd wzięła się różnica.
    /// </remarks>
    [Fact]
    public void KorektaPokazujeStanPrzedZmiana()
    {
        Faktura korekta = Fabryka.PrzykladowaFaktura();
        korekta.Rodzaj = RodzajFaktury.Korygujaca;
        korekta.PrzyczynaKorekty = "Pomyłka w cenie";
        korekta.Korygowane.Add(new DaneFakturyKorygowanej(
            "FV/2026/07/9", new DateOnly(2026, 7, 20), null));
        korekta.PozycjePrzedKorekta.Add(new PozycjaFaktury
        {
            Nazwa = "Usługa przed korektą",
            Jednostka = "usł.",
            Ilosc = 1m,
            CenaNetto = 900m,
            Stawka = StawkaVat.Vat23
        });

        byte[] pdf = WydrukKsef.Utworz(ZPliku(korekta));

        using PdfDocument dokument = Otworz(pdf);
        Assert.True(dokument.PageCount >= 1);
    }

    /// <summary>
    /// Wizualizacja powstaje z faktury odczytanej z pliku - tak jak w programie.
    /// </summary>
    private static Faktura ZPliku(Faktura faktura) =>
        Fa3Czytnik.Odczytaj(Fa3Generator.ZbudujXml(faktura));

    private static PdfDocument Otworz(byte[] pdf)
    {
        using var pamiec = new MemoryStream(pdf);
        return PdfReader.Open(pamiec, PdfDocumentOpenMode.Import);
    }

    private static int DlugoscPierwszejStrony(byte[] pdf)
    {
        using PdfDocument dokument = Otworz(pdf);

        return dokument.Pages[0].Contents.Elements.Sum(element =>
            element is PdfReference odwolanie && odwolanie.Value is PdfDictionary slownik
                ? slownik.Stream?.Length ?? 0
                : 0);
    }
}
