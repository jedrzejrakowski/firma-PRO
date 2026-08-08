using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>Sumy kontrolne numerów identyfikacyjnych.</summary>
public class TestyNumerow
{
    [Theory]
    [InlineData("5252248481")]
    [InlineData("7010001453")]
    [InlineData("1180000001")]
    // Myślniki i spacje w zapisie nie powinny przeszkadzać.
    [InlineData("525-000-01-27")]
    public void PoprawneNumeryNip(string nip)
    {
        Assert.True(Walidator.NipPoprawny(nip));
    }

    [Theory]
    [InlineData("5252248482")]   // zmieniona cyfra kontrolna
    [InlineData("7010001454")]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345678901")]
    [InlineData(null)]
    public void BledneNumeryNip(string? nip)
    {
        Assert.False(Walidator.NipPoprawny(nip));
    }

    [Theory]
    [InlineData("88102010260000120200060290")]
    [InlineData("88 1020 1026 0000 1202 0006 0290")]
    public void PoprawneNumeryRachunku(string rachunek)
    {
        Assert.True(Walidator.RachunekPoprawny(rachunek));
    }

    [Theory]
    [InlineData("49102010260000120200060293")]  // błędna suma kontrolna
    [InlineData("123")]
    [InlineData("")]
    [InlineData(null)]
    public void BledneNumeryRachunku(string? rachunek)
    {
        Assert.False(Walidator.RachunekPoprawny(rachunek));
    }
}

/// <summary>Reguły sprawdzania faktury przed wysyłką.</summary>
public class TestySprawdzaniaFaktury
{
    private static IEnumerable<string> PolaZBledami(WynikWalidacji wynik) =>
        wynik.Problemy.Where(p => p.Poziom == PoziomProblemu.Blad).Select(p => p.Pole);

    [Fact]
    public void PoprawnaFakturaNieMaZastrzezen()
    {
        WynikWalidacji wynik = Walidator.SprawdzFakture(Fabryka.PrzykladowaFaktura());
        Assert.True(wynik.BezZastrzezen, wynik.Opis());
    }

    [Fact]
    public void BrakNumeruIDaty()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Numer = "";
        faktura.DataWystawienia = default;

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.True(wynik.SaBledy);
        Assert.Contains("Numer faktury", PolaZBledami(wynik));
        Assert.Contains("Data wystawienia", PolaZBledami(wynik));
    }

    [Fact]
    public void FakturaBezPozycji()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje.Clear();

        Assert.Contains("Pozycje", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }

    [Fact]
    public void IloscMusiBycDodatnia()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje[0].Ilosc = 0m;

        Assert.Contains("Pozycja 1 / Ilość", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }

    [Fact]
    public void BlednyKodGtu()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje[0].Gtu = "GTU_14";

        Assert.Contains("Pozycja 1 / GTU", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }

    [Fact]
    public void SprzedawcaMusiMiecNip()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Sprzedawca.Nip = "";

        Assert.Contains("Sprzedawca / NIP", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }

    [Fact]
    public void NabywcaBezNipDajeTylkoOstrzezenie()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Nabywca.Nip = "";

        WynikWalidacji wynik = Walidator.SprawdzFakture(faktura);

        Assert.False(wynik.SaBledy);
        Assert.Contains(wynik.Problemy, p => p.Poziom == PoziomProblemu.Ostrzezenie);
    }

    [Fact]
    public void BlednyRachunekBlokujeWysylke()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Platnosc.Rachunek = "12345678901234567890123456";

        Assert.Contains("Płatność / Rachunek", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }

    [Fact]
    public void SprzedazZwolnionaBezPodstawyPrawnej()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje[0].Stawka = StawkaVat.Zwolniona;
        faktura.PodstawaZwolnienia = null;

        // Schemat wymaga wskazania przepisu - bez niego KSeF odrzuci dokument.
        Assert.Contains("Podstawa zwolnienia", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }

    [Fact]
    public void ZbytDlugieNazwyPozycji()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje[0].Nazwa = new string('X', 600);

        // Typ TZnakowy512 dopuszcza najwyżej 512 znaków.
        Assert.Contains("Pozycja 1 / Nazwa", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }

    [Fact]
    public void NiedozwolonaWaluta()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Waluta = "ZŁ";

        Assert.Contains("Waluta", PolaZBledami(Walidator.SprawdzFakture(faktura)));
    }
}

/// <summary>Wyliczanie sum faktury.</summary>
public class TestyWyliczaniaSum
{
    [Fact]
    public void PodatekLiczonyOdSumyANieZPozycji()
    {
        // Trzy pozycje po 0,10 zł: podatek od sumy 0,30 zł to 0,07 zł,
        // a suma podatków z pozycji dałaby 3 × 0,02 = 0,06 zł.
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje = Enumerable.Range(1, 3)
            .Select(i => new PozycjaFaktury
            {
                Nazwa = $"Drobiazg {i}",
                Ilosc = 1m,
                CenaNetto = 0.10m,
                Stawka = StawkaVat.Vat23
            })
            .ToList();

        PodsumowanieFaktury podsumowanie = faktura.Podsumowanie();

        Assert.Equal(0.30m, podsumowanie.RazemNetto);
        Assert.Equal(0.07m, podsumowanie.RazemVat);
    }

    [Fact]
    public void RozbicieNaStawki()
    {
        PodsumowanieFaktury podsumowanie = Fabryka.PrzykladowaFaktura().Podsumowanie();

        Assert.Equal(1500.00m, podsumowanie.Pole("P_13_1"));
        Assert.Equal(345.00m, podsumowanie.Pole("P_14_1"));
        Assert.Equal(1000.00m, podsumowanie.Pole("P_13_2"));
        Assert.Equal(80.00m, podsumowanie.Pole("P_14_2"));
        Assert.Equal(2925.00m, podsumowanie.RazemBrutto);
    }

    [Fact]
    public void StawkiBezPodatkuNieGenerujaVat()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Pozycje =
        [
            new PozycjaFaktury { Nazwa = "Usługa zwolniona", Ilosc = 1m, CenaNetto = 100m, Stawka = StawkaVat.Zwolniona }
        ];

        PodsumowanieFaktury podsumowanie = faktura.Podsumowanie();

        Assert.Equal(0.00m, podsumowanie.RazemVat);
        Assert.Equal(100.00m, podsumowanie.RazemBrutto);
    }

    [Theory]
    // Zaokrąglanie "w górę od połowy" - domyślne Math.Round w .NET stosuje
    // zaokrąglanie bankierskie i dałoby tu inne wyniki.
    [InlineData(2.345, 2.35)]
    [InlineData(2.344, 2.34)]
    [InlineData(-2.345, -2.35)]
    [InlineData(0.125, 0.13)]
    public void ZaokraglanieWGoreOdPolowy(decimal wejscie, decimal oczekiwane)
    {
        Assert.Equal(oczekiwane, Kwoty.Zaokraglij(wejscie));
    }

    [Fact]
    public void WartoscPozycjiToIloscRazyCena()
    {
        var pozycja = new PozycjaFaktury
        {
            Nazwa = "Usługa",
            Ilosc = 7.5m,
            CenaNetto = 133.33m,
            Stawka = StawkaVat.Vat23
        };

        Assert.Equal(999.98m, pozycja.WartoscNetto);
        Assert.Equal(230.00m, pozycja.KwotaVat);
    }

    [Fact]
    public void NieznanyKodStawkiJestOdrzucany()
    {
        ArgumentException blad = Assert.Throws<ArgumentException>(
            () => StawkaVat.ZKodu("19"));

        // Komunikat ma podpowiadać, co wolno wpisać.
        Assert.Contains("0 WDT", blad.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KazdaStawkaMaPrzypisanePoleaPodsumowania()
    {
        // Zabezpieczenie przed dodaniem stawki bez wskazania, gdzie ma trafić.
        Assert.All(StawkaVat.Wszystkie,
            stawka => Assert.False(string.IsNullOrWhiteSpace(stawka.PoleNetto)));
    }
}
