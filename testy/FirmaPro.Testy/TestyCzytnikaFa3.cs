using FirmaPro.Domena;
using FirmaPro.Ksef;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Odczyt faktury zapisanej w strukturze FA(3).
/// </summary>
/// <remarks>
/// Czytnik istnieje po to, żeby wizualizacja powstawała z dokumentu, który
/// trafił do KSeF, a nie z wierszy bazy danych. Testy sprawdzają go tam, gdzie
/// da się to zrobić uczciwie: składamy plik generatorem, rozkładamy czytnikiem
/// i porównujemy. Jeśli obie strony rozjadą się w którymkolwiek polu, wydruk
/// zacząłby kłamać - i właśnie to ma tu wyjść.
/// </remarks>
public sealed class TestyCzytnikaFa3
{
    [Fact]
    public void OdczytOddajePolaNaglowka()
    {
        Faktura odczytana = TamIzPowrotem(Fabryka.PrzykladowaFaktura());
        Faktura zrodlo = Fabryka.PrzykladowaFaktura();

        Assert.Equal(zrodlo.Numer, odczytana.Numer);
        Assert.Equal(zrodlo.DataWystawienia, odczytana.DataWystawienia);
        Assert.Equal(zrodlo.MiejsceWystawienia, odczytana.MiejsceWystawienia);
        Assert.Equal(zrodlo.Waluta, odczytana.Waluta);
        Assert.Equal(zrodlo.Rodzaj, odczytana.Rodzaj);
    }

    [Fact]
    public void OdczytOddajeStronyTransakcji()
    {
        Faktura zrodlo = Fabryka.PrzykladowaFaktura();
        Faktura odczytana = TamIzPowrotem(zrodlo);

        Assert.Equal(zrodlo.Sprzedawca.Nazwa, odczytana.Sprzedawca.Nazwa);
        Assert.Equal(zrodlo.Sprzedawca.Nip, odczytana.Sprzedawca.Nip);
        Assert.Equal(zrodlo.Sprzedawca.Adres.Linia1, odczytana.Sprzedawca.Adres.Linia1);

        Assert.Equal(zrodlo.Nabywca.Nazwa, odczytana.Nabywca.Nazwa);
        Assert.Equal(zrodlo.Nabywca.Nip, odczytana.Nabywca.Nip);
    }

    /// <summary>
    /// Kwoty odtworzone z pliku zgadzają się co do grosza.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Wizualizacja pokazująca inną kwotę niż
    /// dokument w KSeF jest gorsza niż jej brak - nabywca zapłaciłby wtedy
    /// sumę, której nikt nie zafakturował.
    /// </remarks>
    [Fact]
    public void KwotyPoOdczycieSaTeSame()
    {
        Faktura zrodlo = Fabryka.PrzykladowaFaktura();
        Faktura odczytana = TamIzPowrotem(zrodlo);

        PodsumowanieFaktury przed = zrodlo.Podsumowanie();
        PodsumowanieFaktury po = odczytana.Podsumowanie();

        Assert.Equal(przed.RazemNetto, po.RazemNetto);
        Assert.Equal(przed.RazemVat, po.RazemVat);
        Assert.Equal(przed.RazemBrutto, po.RazemBrutto);

        Assert.Equal(zrodlo.Pozycje.Count, odczytana.Pozycje.Count);

        for (int i = 0; i < zrodlo.Pozycje.Count; i++)
        {
            Assert.Equal(zrodlo.Pozycje[i].Nazwa, odczytana.Pozycje[i].Nazwa);
            Assert.Equal(zrodlo.Pozycje[i].Ilosc, odczytana.Pozycje[i].Ilosc);
            Assert.Equal(zrodlo.Pozycje[i].CenaNetto, odczytana.Pozycje[i].CenaNetto);
            Assert.Equal(zrodlo.Pozycje[i].Stawka.Kod, odczytana.Pozycje[i].Stawka.Kod);
        }
    }

    [Fact]
    public void OdczytOddajeWarunkiPlatnosci()
    {
        var zrodlo = Fabryka.PrzykladowaFaktura();
        zrodlo.Platnosc = new WarunkiPlatnosci
        {
            Forma = FormaPlatnosci.Przelew,
            Termin = new DateOnly(2026, 9, 3),
            Rachunek = "PL61109010140000071219812874",
            NazwaBanku = "Bank Testowy S.A."
        };

        Faktura odczytana = TamIzPowrotem(zrodlo);

        Assert.Equal(FormaPlatnosci.Przelew, odczytana.Platnosc.Forma);
        Assert.Equal(new DateOnly(2026, 9, 3), odczytana.Platnosc.Termin);
        Assert.Equal("PL61109010140000071219812874", odczytana.Platnosc.Rachunek);
        Assert.Equal("Bank Testowy S.A.", odczytana.Platnosc.NazwaBanku);
    }

    /// <summary>Kurs waluty wraca z wiersza, bo tam go stawia schemat.</summary>
    [Fact]
    public void OdczytOddajeKursWaluty()
    {
        var zrodlo = Fabryka.PrzykladowaFaktura();
        zrodlo.Waluta = "EUR";
        zrodlo.Kurs = new KursWaluty("EUR", 4.2837m, new DateOnly(2026, 8, 19),
            "160/A/NBP/2026");

        Faktura odczytana = TamIzPowrotem(zrodlo);

        Assert.Equal("EUR", odczytana.Waluta);
        Assert.NotNull(odczytana.Kurs);
        Assert.Equal(4.2837m, odczytana.Kurs!.Wartosc);

        // Numeru tabeli struktura FA(3) nie niesie - czytnik go nie zmyśla.
        Assert.Null(odczytana.Kurs.Tabela);
    }

    /// <summary>
    /// Pozycje sprzed korekty trafiają do osobnej listy.
    /// </summary>
    /// <remarks>
    /// Wrzucone razem z pozostałymi podwoiłyby wartość dokumentu, a korekta
    /// pokazywałaby kwotę, której nikt nigdy nie zafakturował.
    /// </remarks>
    [Fact]
    public void PozycjeSprzedKorektyIdaOsobno()
    {
        var zrodlo = Fabryka.PrzykladowaFaktura();
        zrodlo.Rodzaj = RodzajFaktury.Korygujaca;
        zrodlo.PrzyczynaKorekty = "Pomyłka w cenie";
        zrodlo.Korygowane.Add(new DaneFakturyKorygowanej(
            "FV/2026/07/9", new DateOnly(2026, 7, 20), "1180000001-20260720-0AAAAA-BB"));
        zrodlo.PozycjePrzedKorekta.Add(new PozycjaFaktury
        {
            Nazwa = "Usługa przed korektą",
            Jednostka = "usł.",
            Ilosc = 1m,
            CenaNetto = 900m,
            Stawka = StawkaVat.Vat23
        });

        Faktura odczytana = TamIzPowrotem(zrodlo);

        Assert.Single(odczytana.PozycjePrzedKorekta);
        Assert.Equal("Usługa przed korektą", odczytana.PozycjePrzedKorekta[0].Nazwa);
        Assert.Equal(zrodlo.Pozycje.Count, odczytana.Pozycje.Count);

        Assert.Equal("Pomyłka w cenie", odczytana.PrzyczynaKorekty);
        Assert.Single(odczytana.Korygowane);
        Assert.Equal("FV/2026/07/9", odczytana.Korygowane[0].Numer);
    }

    [Fact]
    public void OdczytOddajeStopkeIPodstaweZwolnienia()
    {
        var zrodlo = Fabryka.PrzykladowaFaktura();
        zrodlo.Stopka = "Dziękujemy za współpracę.";
        zrodlo.Pozycje.Add(new PozycjaFaktury
        {
            Nazwa = "Usługa zwolniona",
            Jednostka = "usł.",
            Ilosc = 1m,
            CenaNetto = 100m,
            Stawka = StawkaVat.Zwolniona
        });
        zrodlo.PodstawaZwolnienia = "art. 43 ust. 1 pkt 19 ustawy o VAT";

        Faktura odczytana = TamIzPowrotem(zrodlo);

        Assert.Equal("Dziękujemy za współpracę.", odczytana.Stopka);
        Assert.Equal("art. 43 ust. 1 pkt 19 ustawy o VAT", odczytana.PodstawaZwolnienia);
    }

    /// <summary>Plik, który nie jest fakturą, ma zostać odrzucony wprost.</summary>
    [Fact]
    public void ObcyDokumentJestOdrzucany()
    {
        byte[] obcy = System.Text.Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><CosInnego><A>1</A></CosInnego>");

        Assert.Throws<BladOdczytuFakturyException>(() => Fa3Czytnik.Odczytaj(obcy));
    }

    [Fact]
    public void UszkodzonyPlikJestOdrzucany()
    {
        byte[] smiec = System.Text.Encoding.UTF8.GetBytes("to nie jest XML");

        Assert.Throws<BladOdczytuFakturyException>(() => Fa3Czytnik.Odczytaj(smiec));
    }

    /// <summary>
    /// Nieznana stawka podatku zatrzymuje odczyt.
    /// </summary>
    /// <remarks>
    /// Podstawienie w takim przypadku 23% dałoby wydruk z podatkiem, którego
    /// na fakturze nie ma - lepiej powiedzieć wprost, że pliku nie rozumiemy.
    /// </remarks>
    [Fact]
    public void NieznanaStawkaZatrzymujeOdczyt()
    {
        byte[] xml = Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura());

        string tekst = System.Text.Encoding.UTF8.GetString(xml)
            .Replace("<P_12>23</P_12>", "<P_12>77</P_12>", StringComparison.Ordinal);

        Assert.Throws<BladOdczytuFakturyException>(
            () => Fa3Czytnik.Odczytaj(System.Text.Encoding.UTF8.GetBytes(tekst)));
    }

    /// <summary>Odczytany dokument da się złożyć z powrotem i przejść schemat.</summary>
    /// <remarks>
    /// Domyka pętlę: skoro plik po podróży tam i z powrotem nadal przechodzi
    /// walidację Ministerstwa, to czytnik nie zgubił po drodze niczego,
    /// czego schemat wymaga.
    /// </remarks>
    [Fact]
    public void ZlozonyPonowniePrzechodziSchemat()
    {
        Faktura odczytana = TamIzPowrotem(Fabryka.PrzykladowaFaktura());

        IReadOnlyList<string> bledy =
            Fabryka.BledyWalidacjiXsd(Fa3Generator.ZbudujXml(odczytana));

        Assert.Empty(bledy);
    }

    private static Faktura TamIzPowrotem(Faktura faktura) =>
        Fa3Czytnik.Odczytaj(Fa3Generator.ZbudujXml(faktura));
}
