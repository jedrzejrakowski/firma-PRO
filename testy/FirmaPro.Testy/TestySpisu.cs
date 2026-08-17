using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Spis z natury i roczne rozliczenie dochodu.
/// </summary>
/// <remarks>
/// Tu leży reguła mylona najczęściej: zakup towaru nie jest kosztem w chwili
/// zakupu - kosztem jest towar sprzedany. Różnicę pokazuje spis z natury,
/// a błąd w tym miejscu przekłada się wprost na dochód roczny i na zeznanie.
/// </remarks>
public sealed class TestySpisu
{
    private static readonly OkresRozliczeniowy Grudzien = OkresRozliczeniowy.Miesiac(2026, 12);

    private static SpisZNatury Spis(DateOnly data, params (decimal Ilosc, decimal Cena)[] pozycje) =>
        new(data, [.. pozycje.Select((p, i) =>
            new PozycjaSpisu($"Towar {i + 1}", "szt.", p.Ilosc, p.Cena))]);

    private static Kpir Ksiega(decimal przychod, decimal zakupTowarow,
                               decimal kosztyUboczne = 0m, decimal pozostale = 0m)
    {
        var data = new DateOnly(2026, 6, 15);

        return Kpir.Zbuduj(Grudzien,
        [
            new WpisKsiegi(data, "FV/1", "Klient", null, "Sprzedaż",
                KolumnaKpir.SprzedazTowarowIUslug, przychod),
            new WpisKsiegi(data, "FZ/1", "Dostawca", null, "Towar",
                KolumnaKpir.ZakupTowarow, zakupTowarow),
            new WpisKsiegi(data, "FZ/2", "Przewoźnik", null, "Transport",
                KolumnaKpir.KosztyUboczneZakupu, kosztyUboczne),
            new WpisKsiegi(data, "FZ/3", "Biuro", null, "Materiały",
                KolumnaKpir.PozostaleWydatki, pozostale)
        ]);
    }

    // ------------------------------------------------------------ sam spis

    [Fact]
    public void WartoscSpisuToSumaPozycji()
    {
        SpisZNatury spis = Spis(new DateOnly(2026, 12, 31), (10m, 25.50m), (3m, 100m));

        // 10 × 25,50 = 255 zł, 3 × 100 = 300 zł.
        Assert.Equal(555m, spis.Wartosc);
    }

    [Fact]
    public void PozycjaZaokraglaSieDoGroszy()
    {
        var pozycja = new PozycjaSpisu("Śruba", "kg", 3.33m, 7.77m);

        // 3,33 × 7,77 = 25,8741 - w dół do groszy.
        Assert.Equal(25.87m, pozycja.Wartosc);
    }

    /// <summary>Spis o wartości zero to nie to samo co brak spisu.</summary>
    /// <remarks>
    /// Firma usługowa też ma obowiązek go sporządzić - zero trzeba wykazać.
    /// </remarks>
    [Fact]
    public void PustySpisMaWartoscZero()
    {
        SpisZNatury spis = SpisZNatury.Pusty(new DateOnly(2026, 12, 31));

        Assert.Empty(spis.Pozycje);
        Assert.Equal(0m, spis.Wartosc);
    }

    // ------------------------------------------------- rozliczenie roczne

    /// <summary>
    /// Towar, który został na magazynie, wypada z kosztów.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Kupiono towaru za 50 000 zł, sprzedano
    /// za 60 000, ale na koniec roku na półce zostało towaru za 20 000 zł.
    /// Kosztem jest 30 000 zł, a nie 50 000 - i dochód wynosi 30 000 zł,
    /// a nie 10 000, jak sugerowałby sam rachunek faktur.
    /// </remarks>
    [Fact]
    public void TowarNaMagazynieWypadaZKosztow()
    {
        RozliczenieRoczne rozliczenie = RozliczenieRoczne.Zbuduj(
            Ksiega(przychod: 60_000m, zakupTowarow: 50_000m),
            remanentPoczatkowy: null,
            remanentKoncowy: Spis(new DateOnly(2026, 12, 31), (1m, 20_000m)));

        Assert.Equal(30_000m, rozliczenie.WartoscSprzedanychTowarow);
        Assert.Equal(30_000m, rozliczenie.Dochod);

        // Bez spisu dochód wyszedłby 10 000 zł - o 20 000 zł za mało.
        Assert.Equal(20_000m, rozliczenie.WplywRemanentow);
    }

    /// <summary>
    /// Towar z zeszłego roku wchodzi w koszty roku, w którym się sprzedał.
    /// </summary>
    /// <remarks>
    /// Odwrotny kierunek: magazyn się opróżnia, więc koszty rosną ponad zakupy
    /// tego roku, a dochód spada.
    /// </remarks>
    [Fact]
    public void TowarZPoprzedniegoRokuWchodziWKoszty()
    {
        RozliczenieRoczne rozliczenie = RozliczenieRoczne.Zbuduj(
            Ksiega(przychod: 60_000m, zakupTowarow: 10_000m),
            remanentPoczatkowy: Spis(new DateOnly(2026, 1, 1), (1m, 20_000m)),
            remanentKoncowy: SpisZNatury.Pusty(new DateOnly(2026, 12, 31)));

        // 20 000 + 10 000 − 0 = 30 000 zł kosztu przy zakupach za 10 000.
        Assert.Equal(30_000m, rozliczenie.WartoscSprzedanychTowarow);
        Assert.Equal(30_000m, rozliczenie.Dochod);
        Assert.Equal(-20_000m, rozliczenie.WplywRemanentow);
    }

    /// <summary>Rachunek idzie w kolejności z objaśnień do księgi.</summary>
    [Fact]
    public void RachunekIdzieWKolejnosciZObjasnien()
    {
        RozliczenieRoczne rozliczenie = RozliczenieRoczne.Zbuduj(
            Ksiega(przychod: 100_000m, zakupTowarow: 40_000m,
                   kosztyUboczne: 2_000m, pozostale: 15_000m),
            remanentPoczatkowy: Spis(new DateOnly(2026, 1, 1), (1m, 5_000m)),
            remanentKoncowy: Spis(new DateOnly(2026, 12, 31), (1m, 8_000m)));

        // 5000 + 40 000 + 2000 = 47 000
        Assert.Equal(47_000m, rozliczenie.RazemZZakupami);

        // 47 000 − 8000 = 39 000
        Assert.Equal(39_000m, rozliczenie.WartoscSprzedanychTowarow);

        // 39 000 + 0 + 15 000 = 54 000
        Assert.Equal(54_000m, rozliczenie.KosztyUzyskania);

        // 100 000 − 54 000 = 46 000
        Assert.Equal(46_000m, rozliczenie.Dochod);
    }

    /// <summary>Bez spisów rozliczenie sprowadza się do samych faktur.</summary>
    [Fact]
    public void BezSpisowLiczySieSamaKsiega()
    {
        RozliczenieRoczne rozliczenie = RozliczenieRoczne.Zbuduj(
            Ksiega(przychod: 50_000m, zakupTowarow: 20_000m, pozostale: 5_000m),
            null, null);

        Assert.False(rozliczenie.MaRemanenty);
        Assert.Equal(25_000m, rozliczenie.Dochod);
        Assert.Equal(0m, rozliczenie.WplywRemanentow);
    }

    // ------------------------------------------------------- dobór spisów

    [Fact]
    public void SpisNaPierwszyStyczniaOtwieraRok()
    {
        SpisZNatury styczen = Spis(new DateOnly(2026, 1, 1), (1m, 1000m));
        SpisZNatury grudzien = Spis(new DateOnly(2026, 12, 31), (1m, 3000m));

        Assert.Equal(styczen, Remanenty.Poczatkowy([styczen, grudzien], 2026));
        Assert.Equal(grudzien, Remanenty.Koncowy([styczen, grudzien], 2026));
    }

    /// <summary>
    /// Spis z 31 grudnia zamyka jeden rok i otwiera następny.
    /// </summary>
    /// <remarks>
    /// To ten sam stan magazynu, tylko inaczej datowany - obu zapisów używa
    /// się w praktyce, więc program musi rozumieć oba.
    /// </remarks>
    [Fact]
    public void SpisZKoncaRokuOtwieraRokNastepny()
    {
        SpisZNatury poprzedni = Spis(new DateOnly(2025, 12, 31), (1m, 4000m));
        SpisZNatury biezacy = Spis(new DateOnly(2026, 12, 31), (1m, 6000m));

        Assert.Equal(poprzedni, Remanenty.Poczatkowy([poprzedni, biezacy], 2026));
        Assert.Equal(biezacy, Remanenty.Koncowy([poprzedni, biezacy], 2026));
    }

    /// <summary>
    /// Jeden spis nie może być jednocześnie początkowym i końcowym.
    /// </summary>
    /// <remarks>
    /// Inaczej różnica remanentów wyszłaby zerem i cały towar z magazynu
    /// zniknąłby z rachunku - dokładnie ten błąd, przed którym spis chroni.
    /// </remarks>
    [Fact]
    public void JedenSpisNieLiczySieDwaRazy()
    {
        SpisZNatury jedyny = Spis(new DateOnly(2026, 1, 1), (1m, 5000m));

        Assert.Equal(jedyny, Remanenty.Poczatkowy([jedyny], 2026));
        Assert.Null(Remanenty.Koncowy([jedyny], 2026));
    }

    [Fact]
    public void SpisZPrzyszlegoRokuNieWchodzi()
    {
        SpisZNatury przyszly = Spis(new DateOnly(2027, 12, 31), (1m, 9000m));

        Assert.Null(Remanenty.Poczatkowy([przyszly], 2026));
        Assert.Null(Remanenty.Koncowy([przyszly], 2026));
    }

    [Fact]
    public void BrakSpisowDajePusteRemanenty()
    {
        Assert.Null(Remanenty.Poczatkowy([], 2026));
        Assert.Null(Remanenty.Koncowy([], 2026));
    }
}
