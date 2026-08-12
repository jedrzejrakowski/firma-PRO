using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Rozbicie zaliczki na stawki podatku.
/// </summary>
/// <remarks>
/// Faktura zaliczkowa dokumentuje pieniądze, a nie towar - ale podatek trzeba
/// od nich wykazać w podziale na stawki. Najważniejsze jest to, że grosze
/// z zaokrągleń nie mogą przepaść ani się namnożyć: suma rozbicia musi co do
/// grosza równać się wpłacie, inaczej faktura nie zgadza się z przelewem.
/// </remarks>
public class TestyZaliczek
{
    private static PozycjaZamowienia Pozycja(decimal cena, StawkaVat stawka, decimal ilosc = 1m) =>
        new() { Nazwa = "Pozycja", Ilosc = ilosc, CenaNetto = cena, Stawka = stawka };

    [Fact]
    public void ZaliczkaWJednejStawceLiczonaJestWStu()
    {
        PozycjaZamowienia[] zamowienie = [Pozycja(1000m, StawkaVat.Vat23)];

        CzescZaliczki czesc = Assert.Single(Zaliczka.Rozbij(zamowienie, 615m));

        // 615 zł brutto przy stawce 23% to 500 zł netto i 115 zł podatku.
        Assert.Equal(StawkaVat.Vat23, czesc.Stawka);
        Assert.Equal(500m, czesc.Netto);
        Assert.Equal(115m, czesc.Vat);
        Assert.Equal(615m, czesc.Brutto);
    }

    [Fact]
    public void ZaliczkaDzieliSieProporcjonalnieMiedzyStawki()
    {
        // Zamówienie: 1000 zł netto w 23% (1230 brutto) i 1000 zł netto
        // w 8% (1080 brutto). Razem 2310 zł brutto.
        PozycjaZamowienia[] zamowienie =
        [
            Pozycja(1000m, StawkaVat.Vat23),
            Pozycja(1000m, StawkaVat.Vat8)
        ];

        IReadOnlyList<CzescZaliczki> czesci = Zaliczka.Rozbij(zamowienie, 1155m);

        Assert.Equal(2, czesci.Count);
        Assert.Equal(1155m, czesci.Sum(c => c.Brutto));

        // Połowa zamówienia to połowa każdej stawki.
        Assert.Equal(615m, czesci.Single(c => c.Stawka == StawkaVat.Vat23).Brutto);
        Assert.Equal(540m, czesci.Single(c => c.Stawka == StawkaVat.Vat8).Brutto);
    }

    /// <summary>
    /// Grosz z zaokrąglenia trafia do największej części, a nie w próżnię.
    /// </summary>
    [Fact]
    public void SumaRozbiciaZawszeRownaSieWplacie()
    {
        PozycjaZamowienia[] zamowienie =
        [
            Pozycja(33.33m, StawkaVat.Vat23),
            Pozycja(66.67m, StawkaVat.Vat8),
            Pozycja(10.01m, StawkaVat.Vat5)
        ];

        foreach (decimal wplata in new[] { 0.01m, 1m, 7.77m, 13.13m, 99.99m, 100m })
        {
            IReadOnlyList<CzescZaliczki> czesci = Zaliczka.Rozbij(zamowienie, wplata);

            Assert.Equal(wplata, czesci.Sum(c => c.Brutto));
            Assert.All(czesci, c => Assert.Equal(c.Brutto, c.Netto + c.Vat));
        }
    }

    [Fact]
    public void StawkaBezPodatkuNieDodajeVat()
    {
        PozycjaZamowienia[] zamowienie = [Pozycja(1000m, StawkaVat.Zwolniona)];

        CzescZaliczki czesc = Assert.Single(Zaliczka.Rozbij(zamowienie, 400m));

        Assert.Equal(400m, czesc.Netto);
        Assert.Equal(0m, czesc.Vat);
    }

    [Fact]
    public void PozycjeWTejSamejStawceSkladajaSieWJednaCzesc()
    {
        PozycjaZamowienia[] zamowienie =
        [
            Pozycja(100m, StawkaVat.Vat23),
            Pozycja(200m, StawkaVat.Vat23)
        ];

        CzescZaliczki czesc = Assert.Single(Zaliczka.Rozbij(zamowienie, 123m));
        Assert.Equal(123m, czesc.Brutto);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ZaliczkaNiedodatniaNieDajeNiczego(decimal wplata)
    {
        PozycjaZamowienia[] zamowienie = [Pozycja(1000m, StawkaVat.Vat23)];

        Assert.Empty(Zaliczka.Rozbij(zamowienie, wplata));
    }

    [Fact]
    public void WartoscZamowieniaLiczonaJestZPodatkiem()
    {
        PozycjaZamowienia[] zamowienie =
        [
            Pozycja(1000m, StawkaVat.Vat23),
            Pozycja(500m, StawkaVat.Vat8)
        ];

        Assert.Equal(1770m, Zaliczka.WartoscZamowienia(zamowienie));
    }

    /// <summary>
    /// Faktura końcowa żąda tylko dopłaty, a nie całej wartości dostawy.
    /// </summary>
    /// <remarks>
    /// To kwota drukowana na dokumencie, który dostaje nabywca - pomyłka
    /// tutaj kazałaby mu zapłacić zaliczkę drugi raz.
    /// </remarks>
    [Fact]
    public void DoZaplatyPomniejszaSieOZafakturowaneZaliczki()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();
        faktura.Rodzaj = RodzajFaktury.Rozliczeniowa;

        decimal brutto = faktura.Podsumowanie().RazemBrutto;

        faktura.Zaliczkowe =
        [
            new DaneZaliczki("FV/1", new DateOnly(2026, 7, 1), null, 1230m),
            new DaneZaliczki("FV/2", new DateOnly(2026, 7, 20), null, 615m)
        ];

        Assert.Equal(1845m, faktura.ZafakturowaneZaliczkami);
        Assert.Equal(brutto - 1845m, faktura.DoZaplaty);
    }

    [Fact]
    public void ZwyklaFakturaZadaCalejKwoty()
    {
        Faktura faktura = Fabryka.PrzykladowaFaktura();

        Assert.Equal(0m, faktura.ZafakturowaneZaliczkami);
        Assert.Equal(faktura.Podsumowanie().RazemBrutto, faktura.DoZaplaty);
    }
}
