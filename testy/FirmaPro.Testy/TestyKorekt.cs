using FirmaPro.Domena;
using FirmaPro.Ksef;

namespace FirmaPro.Testy;

/// <summary>
/// Faktury korygujące.
/// </summary>
/// <remarks>
/// Korekta to jedyny sposób poprawienia faktury przyjętej przez KSeF, więc
/// jej poprawność jest tak samo ważna jak poprawność faktury pierwotnej.
/// Tutaj - w odróżnieniu od JPK - można oprzeć się na prawdziwym schemacie,
/// i to on rozstrzyga.
/// </remarks>
public sealed class TestyKorekt
{
    /// <summary>Korekta przechodzi walidację oficjalnym schematem FA(3).</summary>
    [Fact]
    public void KorektaJestZgodnaZeSchematem()
    {
        byte[] xml = Fa3Generator.ZbudujXml(PrzykladowaKorekta());

        Assert.Empty(Fabryka.BledyWalidacjiXsd(xml));
    }

    /// <summary>
    /// Korekta faktury wystawionej poza KSeF też jest zgodna ze schematem.
    /// </summary>
    /// <remarks>
    /// Schemat wymaga rozstrzygnięcia: albo numer KSeF faktury korygowanej,
    /// albo znacznik, że jej tam nie ma. Pominięcie obu unieważnia dokument,
    /// więc oba warianty mają własny test.
    /// </remarks>
    [Fact]
    public void KorektaFakturySprzedKsefJestZgodnaZeSchematem()
    {
        Faktura korekta = PrzykladowaKorekta();
        korekta.Korygowane =
            [new DaneFakturyKorygowanej("FV/2025/12/9", new DateOnly(2025, 12, 20), null)];

        byte[] xml = Fa3Generator.ZbudujXml(korekta);

        Assert.Empty(Fabryka.BledyWalidacjiXsd(xml));
        Assert.Equal("1", Fabryka.Wartosc(xml, "NrKSeFN"));
        Assert.Null(Fabryka.Wartosc(xml, "NrKSeFFaKorygowanej"));
    }

    [Fact]
    public void KorektaNiesieDaneFakturyKorygowanej()
    {
        byte[] xml = Fa3Generator.ZbudujXml(PrzykladowaKorekta());

        Assert.Equal("KOR", Fabryka.Wartosc(xml, "RodzajFaktury"));
        Assert.Equal("FV/2026/08/1", Fabryka.Wartosc(xml, "NrFaKorygowanej"));
        Assert.Equal("2026-08-05", Fabryka.Wartosc(xml, "DataWystFaKorygowanej"));
        Assert.Equal("2", Fabryka.Wartosc(xml, "TypKorekty"));
        Assert.Equal("Udzielony rabat", Fabryka.Wartosc(xml, "PrzyczynaKorekty"));
        Assert.Equal("5252248481-20260805-01A2B3-C4D5E6-70",
            Fabryka.Wartosc(xml, "NrKSeFFaKorygowanej"));
    }

    /// <summary>
    /// Korekta wykazuje różnicę, a nie nowy stan transakcji.
    /// </summary>
    /// <remarks>
    /// To sedno całej korekty. Gdyby podsumowanie pokazywało nowe kwoty,
    /// rejestr VAT policzyłby całą sprzedaż drugi raz, a podatek należny
    /// wyszedłby niemal podwójny.
    /// </remarks>
    [Fact]
    public void PodsumowanieKorektyToRoznica()
    {
        Faktura korekta = PrzykladowaKorekta();
        PodsumowanieFaktury podsumowanie = korekta.Podsumowanie();

        // Było 1 000,00 netto, jest 800,00 - różnica to minus 200,00.
        Assert.Equal(-200m, podsumowanie.RazemNetto);
        Assert.Equal(-46m, podsumowanie.RazemVat);
        Assert.Equal(-246m, podsumowanie.RazemBrutto);
    }

    [Fact]
    public void UjemneKwotyTrafiajaDoPliku()
    {
        byte[] xml = Fa3Generator.ZbudujXml(PrzykladowaKorekta());

        Assert.Equal("-200.00", Fabryka.Wartosc(xml, "P_13_1"));
        Assert.Equal("-46.00", Fabryka.Wartosc(xml, "P_14_1"));
        Assert.Equal("-246.00", Fabryka.Wartosc(xml, "P_15"));
    }

    /// <summary>
    /// Korekta w górę też działa - nie każda zmniejsza kwotę.
    /// </summary>
    [Fact]
    public void KorektaZwiekszajacaDajeDodatniaRoznice()
    {
        Faktura korekta = PrzykladowaKorekta();
        korekta.Pozycje[0].CenaNetto = 1500m;

        PodsumowanieFaktury podsumowanie = korekta.Podsumowanie();

        Assert.Equal(500m, podsumowanie.RazemNetto);
        Assert.Equal(115m, podsumowanie.RazemVat);
    }

    /// <summary>
    /// Pozycje sprzed korekty mają znacznik stanu przed, a nowe go nie mają.
    /// </summary>
    [Fact]
    public void PozycjePrzedKorektaSaOznaczone()
    {
        byte[] xml = Fa3Generator.ZbudujXml(PrzykladowaKorekta());
        string tresc = System.Text.Encoding.UTF8.GetString(xml);

        // Dwie grupy wierszy, wspólna numeracja, znacznik tylko przy pierwszej.
        Assert.Contains("<NrWierszaFa>1</NrWierszaFa>", tresc, StringComparison.Ordinal);
        Assert.Contains("<NrWierszaFa>2</NrWierszaFa>", tresc, StringComparison.Ordinal);

        int znacznikow = tresc.Split("<StanPrzed>1</StanPrzed>").Length - 1;
        Assert.Equal(1, znacznikow);
    }

    /// <summary>Zwykła faktura nie dostaje żadnych elementów korekty.</summary>
    [Fact]
    public void ZwyklaFakturaNieMaSekcjiKorekty()
    {
        byte[] xml = Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura());
        string tresc = System.Text.Encoding.UTF8.GetString(xml);

        Assert.DoesNotContain("DaneFaKorygowanej", tresc, StringComparison.Ordinal);
        Assert.DoesNotContain("TypKorekty", tresc, StringComparison.Ordinal);
        Assert.DoesNotContain("StanPrzed", tresc, StringComparison.Ordinal);
    }

    /// <summary>
    /// Korekta zbiorcza może dotyczyć kilku faktur naraz.
    /// </summary>
    /// <remarks>
    /// Rabat udzielony do wszystkich dostaw z danego okresu (art. 106j ust. 3)
    /// wskazuje więcej niż jedną fakturę korygowaną.
    /// </remarks>
    [Fact]
    public void KorektaMozeDotyczycKilkuFaktur()
    {
        Faktura korekta = PrzykladowaKorekta();
        korekta.Korygowane =
        [
            new DaneFakturyKorygowanej("FV/2026/08/1", new DateOnly(2026, 8, 5), null),
            new DaneFakturyKorygowanej("FV/2026/08/2", new DateOnly(2026, 8, 12), null),
            new DaneFakturyKorygowanej("FV/2026/08/3", new DateOnly(2026, 8, 20), null)
        ];

        byte[] xml = Fa3Generator.ZbudujXml(korekta);
        string tresc = System.Text.Encoding.UTF8.GetString(xml);

        Assert.Empty(Fabryka.BledyWalidacjiXsd(xml));
        Assert.Equal(3, tresc.Split("<DaneFaKorygowanej>").Length - 1);
    }

    [Theory]
    [InlineData(TypKorektyVat.WDaciePierwotnej, "1")]
    [InlineData(TypKorektyVat.WDacieKorekty, "2")]
    [InlineData(TypKorektyVat.WInnejDacie, "3")]
    public void TypKorektyTrafiaDoPliku(TypKorektyVat typ, string oczekiwany)
    {
        Faktura korekta = PrzykladowaKorekta();
        korekta.TypKorekty = typ;

        byte[] xml = Fa3Generator.ZbudujXml(korekta);

        Assert.Empty(Fabryka.BledyWalidacjiXsd(xml));
        Assert.Equal(oczekiwany, Fabryka.Wartosc(xml, "TypKorekty"));
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>
    /// Korekta obniżająca cenę z 1 000 zł na 800 zł.
    /// </summary>
    private static Faktura PrzykladowaKorekta()
    {
        Faktura wzor = Fabryka.PrzykladowaFaktura();

        var korekta = new Faktura
        {
            Numer = "FV/2026/08/7",
            DataWystawienia = new DateOnly(2026, 8, 25),
            DataSprzedazy = new DateOnly(2026, 8, 25),
            MiejsceWystawienia = wzor.MiejsceWystawienia,
            Rodzaj = RodzajFaktury.Korygujaca,
            Sprzedawca = wzor.Sprzedawca,
            Nabywca = wzor.Nabywca,
            PrzyczynaKorekty = "Udzielony rabat",
            TypKorekty = TypKorektyVat.WDacieKorekty,
            Korygowane =
            [
                new DaneFakturyKorygowanej("FV/2026/08/1", new DateOnly(2026, 8, 5),
                    "5252248481-20260805-01A2B3-C4D5E6-70")
            ],
            PozycjePrzedKorekta =
            [
                new PozycjaFaktury
                {
                    Nazwa = "Usługa serwisowa",
                    Jednostka = "szt.",
                    Ilosc = 1m,
                    CenaNetto = 1000m,
                    Stawka = StawkaVat.Vat23
                }
            ],
            Pozycje =
            [
                new PozycjaFaktury
                {
                    Nazwa = "Usługa serwisowa",
                    Jednostka = "szt.",
                    Ilosc = 1m,
                    CenaNetto = 800m,
                    Stawka = StawkaVat.Vat23
                }
            ]
        };

        return korekta;
    }
}
