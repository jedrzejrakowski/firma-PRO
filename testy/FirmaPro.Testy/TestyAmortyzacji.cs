using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Amortyzacja środków trwałych.
/// </summary>
/// <remarks>
/// Odpis amortyzacyjny jest kosztem zamiast zakupu (art. 22 ust. 8 ustawy
/// o PIT), więc błąd w tych regułach przekłada się wprost na zaniżony albo
/// zawyżony dochód - i to przez kilka lat, bo plan raz policzony obowiązuje
/// do pełnego umorzenia.
/// </remarks>
public sealed class TestyAmortyzacji
{
    private static SrodekTrwaly Sprzet(
        decimal wartosc = 24_000m,
        decimal stawka = 20m,
        MetodaAmortyzacji metoda = MetodaAmortyzacji.Liniowa,
        int miesiacPrzyjecia = 3) => new()
    {
        Nazwa = "Zestaw komputerowy",
        NumerInwentarzowy = "ST/2026/1",
        DataPrzyjecia = new DateOnly(2026, miesiacPrzyjecia, 15),
        WartoscPoczatkowa = wartosc,
        StawkaRoczna = stawka,
        Metoda = metoda
    };

    /// <summary>
    /// Pierwszy odpis przypada na miesiąc po przyjęciu do używania.
    /// </summary>
    /// <remarks>
    /// Art. 22h ust. 1 pkt 1. Sprzęt przyjęty w marcu daje pierwszy odpis
    /// w kwietniu - nie w marcu i nie w miesiącu zakupu.
    /// </remarks>
    [Fact]
    public void PierwszyOdpisJestMiesiacPoPrzyjeciu()
    {
        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(Sprzet());

        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 4), plan[0].Okres);
    }

    /// <summary>Przyjęcie w grudniu przenosi pierwszy odpis na styczeń.</summary>
    [Fact]
    public void PrzyjecieWGrudniuDajePierwszyOdpisWStyczniu()
    {
        IReadOnlyList<Odpis> plan =
            PlanAmortyzacji.Zbuduj(Sprzet(miesiacPrzyjecia: 12));

        Assert.Equal(OkresRozliczeniowy.Miesiac(2027, 1), plan[0].Okres);
    }

    [Fact]
    public void OdpisLiniowyJestRownyPrzezCalyOkres()
    {
        // 24 000 zł przy 20% rocznie to 4800 zł rocznie, czyli 400 zł miesięcznie.
        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(Sprzet());

        Assert.Equal(400m, plan[0].Kwota);
        Assert.Equal(400m, plan[10].Kwota);
        Assert.Equal(60, plan.Count);
    }

    /// <summary>
    /// Suma odpisów równa się wartości początkowej, co do grosza.
    /// </summary>
    /// <remarks>
    /// Najważniejszy test w tym pliku. Zaokrąglenia zbierają się przez
    /// kilkadziesiąt miesięcy; bez wyrównania ostatniego odpisu środek zostałby
    /// z groszem nieumorzonym albo umorzenie przekroczyłoby jego wartość.
    /// </remarks>
    [Theory]
    [InlineData(24_000, 20)]
    [InlineData(10_000, 30)]
    [InlineData(7_333.33, 14)]
    [InlineData(150_000, 20)]
    [InlineData(999.99, 100)]
    public void SumaOdpisowRownaSieWartosciPoczatkowej(decimal wartosc, decimal stawka)
    {
        IReadOnlyList<Odpis> plan =
            PlanAmortyzacji.Zbuduj(Sprzet(wartosc, stawka));

        Assert.Equal(wartosc, plan.Sum(o => o.Kwota));
        Assert.Equal(wartosc, plan[^1].Umorzenie);
    }

    [Fact]
    public void UmorzenieRosnieDoWartosciPoczatkowej()
    {
        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(Sprzet());

        Assert.Equal(400m, plan[0].Umorzenie);
        Assert.Equal(800m, plan[1].Umorzenie);
        Assert.Equal(24_000m, plan[^1].Umorzenie);
        Assert.Equal(0m, plan[^1].DoUmorzenia(24_000m));
    }

    // -------------------------------------------------------------- degresywna

    /// <summary>
    /// Metoda degresywna daje wyższe odpisy na początku.
    /// </summary>
    /// <remarks>
    /// Art. 22k ust. 1: stawka podwyższona współczynnikiem, od wartości
    /// pomniejszonej o dotychczasowe odpisy.
    /// </remarks>
    [Fact]
    public void DegresywnaZaczynaOdWyzszychOdpisow()
    {
        SrodekTrwaly srodek = Sprzet(metoda: MetodaAmortyzacji.Degresywna) with
        {
            Wspolczynnik = 2.0m
        };

        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(srodek);

        // 24 000 zł przy 20% × 2,0 to 40% rocznie, czyli 800 zł miesięcznie.
        Assert.Equal(800m, plan[0].Kwota);

        // Liniowa dałaby 400 zł - degresywna jest na starcie dwa razy wyższa.
        Assert.True(plan[0].Kwota > PlanAmortyzacji.Zbuduj(Sprzet())[0].Kwota);
    }

    /// <summary>
    /// Degresywna przechodzi na liniową, gdy ta staje się korzystniejsza.
    /// </summary>
    /// <remarks>
    /// Przejście jest obowiązkowe, nie wyborem podatnika - bez niego odpisy
    /// malałyby w nieskończoność i środek nigdy nie zamortyzowałby się do zera.
    /// </remarks>
    [Fact]
    public void DegresywnaPrzechodziNaLiniowa()
    {
        SrodekTrwaly srodek = Sprzet(metoda: MetodaAmortyzacji.Degresywna) with
        {
            Wspolczynnik = 2.0m
        };

        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(srodek);

        // Po przejściu odpis nie spada poniżej raty liniowej z wartości
        // początkowej - inaczej amortyzacja ciągnęłaby się bez końca.
        Assert.Contains(plan, o => o.Kwota == 400m);
        Assert.Equal(24_000m, plan.Sum(o => o.Kwota));
        Assert.True(plan.Count < 60, "Degresywna kończy się wcześniej niż liniowa.");
    }

    // ------------------------------------------------------------- jednorazowa

    [Fact]
    public void JednorazowaDajeJedenOdpisNaCalosc()
    {
        IReadOnlyList<Odpis> plan =
            PlanAmortyzacji.Zbuduj(Sprzet(metoda: MetodaAmortyzacji.Jednorazowa));

        Odpis odpis = Assert.Single(plan);

        Assert.Equal(24_000m, odpis.Kwota);
        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 4), odpis.Okres);
    }

    // ------------------------------------------------------- limit samochodowy

    /// <summary>
    /// Odpis od samochodu ponad limit nie jest w całości kosztem.
    /// </summary>
    /// <remarks>
    /// Art. 23 ust. 1 pkt 4 ustawy o PIT: przy samochodzie osobowym kosztem
    /// jest odpis w części przypadającej na wartość do 150 000 zł. Odpis liczy
    /// się nadal od pełnej wartości - obcięta jest tylko część kosztowa.
    /// </remarks>
    [Fact]
    public void OdpisOdSamochoduPonadLimitJestKosztemCzesciowo()
    {
        SrodekTrwaly samochod = new()
        {
            Nazwa = "Samochód osobowy",
            NumerInwentarzowy = "ST/2026/2",
            DataPrzyjecia = new DateOnly(2026, 1, 20),
            WartoscPoczatkowa = 300_000m,
            StawkaRoczna = 20m,
            LimitKosztu = 150_000m
        };

        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(samochod);

        // 300 000 zł przy 20% to 5000 zł miesięcznie - kosztem połowa.
        Assert.Equal(5000m, plan[0].Kwota);
        Assert.Equal(2500m, plan[0].Koszt);

        Assert.True(samochod.PrzekraczaLimit);
        Assert.Equal(0.5m, samochod.UdzialKosztu);

        // Umorzenie idzie od pełnej wartości, mimo obciętego kosztu.
        Assert.Equal(300_000m, plan.Sum(o => o.Kwota));
        Assert.Equal(150_000m, plan.Sum(o => o.Koszt));
    }

    [Fact]
    public void SamochodPonizejLimituAmortyzujeSieWCalosci()
    {
        SrodekTrwaly samochod = Sprzet(wartosc: 90_000m) with { LimitKosztu = 150_000m };

        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(samochod);

        Assert.False(samochod.PrzekraczaLimit);
        Assert.Equal(plan.Sum(o => o.Kwota), plan.Sum(o => o.Koszt));
    }

    // ------------------------------------------------------------- likwidacja

    /// <summary>Po likwidacji odpisów już nie ma.</summary>
    [Fact]
    public void LikwidacjaKonczyOdpisy()
    {
        SrodekTrwaly srodek = Sprzet() with
        {
            DataLikwidacji = new DateOnly(2026, 8, 31)
        };

        IReadOnlyList<Odpis> plan = PlanAmortyzacji.Zbuduj(srodek);

        // Od kwietnia do sierpnia - pięć odpisów, potem koniec.
        Assert.Equal(5, plan.Count);
        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 8), plan[^1].Okres);
        Assert.Equal(2000m, plan.Sum(o => o.Kwota));
    }

    // ------------------------------------------------------------ przypadki brzegowe

    [Fact]
    public void ZerowaWartoscNieDajeOdpisow() =>
        Assert.Empty(PlanAmortyzacji.Zbuduj(Sprzet(wartosc: 0m)));

    /// <summary>
    /// Zerowa stawka nie zawiesza programu.
    /// </summary>
    /// <remarks>
    /// Przy stawce zero rata jest zerem i pętla nigdy nie doszłaby do pełnego
    /// umorzenia - stąd twarde ograniczenie liczby miesięcy.
    /// </remarks>
    [Fact]
    public void ZerowaStawkaNieZapetlaPlanu() =>
        Assert.Empty(PlanAmortyzacji.Zbuduj(Sprzet(stawka: 0m)));

    [Fact]
    public void OdpisZaWskazanyOkresDaSieWyjac()
    {
        SrodekTrwaly srodek = Sprzet();

        Odpis? kwiecien = PlanAmortyzacji.ZaOkres(srodek, OkresRozliczeniowy.Miesiac(2026, 4));
        Odpis? marzec = PlanAmortyzacji.ZaOkres(srodek, OkresRozliczeniowy.Miesiac(2026, 3));

        Assert.NotNull(kwiecien);
        Assert.Equal(400m, kwiecien!.Kwota);

        // Marzec to miesiąc przyjęcia - odpisu jeszcze nie ma.
        Assert.Null(marzec);
    }
}
