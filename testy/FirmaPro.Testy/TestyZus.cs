using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Składki przedsiębiorcy.
/// </summary>
/// <remarks>
/// Testy sprawdzają reguły z ustawy, a nie samo działanie kodu. Pomyłka
/// w podstawie wymiaru albo w progu zdrowotnej nie wychodzi od razu - wychodzi
/// przy kontroli ZUS, razem z odsetkami za kilka lat wstecz.
/// </remarks>
public sealed class TestyZus
{
    private static readonly StawkiZus Stawki2025 = StawkiZus.Dla(2025)!;

    private static UstawieniaZus Pelny(bool chorobowe = true) =>
        new(TytulUbezpieczenia.Pelny, chorobowe);

    // ------------------------------------------------------ podstawy wymiaru

    /// <summary>Pełny ZUS liczy się od 60% prognozowanego przeciętnego.</summary>
    [Fact]
    public void PodstawaPelnaToSzescdziesiatProcentPrzecietnego()
    {
        // 8673 × 60% = 5203,80 zł.
        Assert.Equal(5203.80m, Stawki2025.PodstawaPelna);
    }

    /// <summary>Preferencyjny liczy się od 30% minimalnego wynagrodzenia.</summary>
    [Fact]
    public void PodstawaPreferencyjnaToTrzydziesciProcentMinimalnego()
    {
        // 4666 × 30% = 1399,80 zł.
        Assert.Equal(1399.80m, Stawki2025.PodstawaPreferencyjna(1));
    }

    /// <summary>
    /// Zmiana minimalnego w połowie roku zmienia podstawę preferencyjną.
    /// </summary>
    /// <remarks>
    /// W 2024 roku minimalne wynagrodzenie rosło dwa razy, więc składka
    /// preferencyjna była od lipca inna niż od stycznia. Wzięcie kwoty
    /// ze stycznia na cały rok dałoby niedopłatę za sześć miesięcy.
    /// </remarks>
    [Fact]
    public void MinimalneZmienioneWLipcuZmieniaPodstawe()
    {
        StawkiZus stawki = StawkiZus.Dla(2024)!;

        Assert.Equal(4242m, stawki.MinimalneWMiesiacu(6));
        Assert.Equal(4300m, stawki.MinimalneWMiesiacu(7));

        Assert.Equal(1272.60m, stawki.PodstawaPreferencyjna(6));
        Assert.Equal(1290.00m, stawki.PodstawaPreferencyjna(7));
    }

    /// <summary>Ulga na start to brak składek społecznych, nie ich obniżka.</summary>
    [Fact]
    public void UlgaNaStartNieMaSkladekSpolecznych()
    {
        SkladkiSpoleczne skladki = Zus.Spoleczne(
            new UstawieniaZus(TytulUbezpieczenia.UlgaNaStart), Stawki2025, 1);

        Assert.Equal(0m, skladki.Razem);
        Assert.Equal(0m, skladki.Emerytalna);
    }

    // -------------------------------------------------------------- składki

    [Fact]
    public void SkladkiPelneLiczaSieOdPodstawy()
    {
        SkladkiSpoleczne skladki = Zus.Spoleczne(Pelny(), Stawki2025, 1);

        Assert.Equal(5203.80m, skladki.Podstawa);
        Assert.Equal(1015.78m, skladki.Emerytalna);   // 19,52%
        Assert.Equal(416.30m, skladki.Rentowa);       // 8%
        Assert.Equal(127.49m, skladki.Chorobowe);     // 2,45%
        Assert.Equal(86.90m, skladki.Wypadkowa);      // 1,67%
        Assert.Equal(127.49m, skladki.FunduszPracy);  // 2,45%
    }

    /// <summary>Chorobowe jest dobrowolne - bez niego składka jest niższa.</summary>
    [Fact]
    public void BezChorobowegoSkladkaJestNizsza()
    {
        SkladkiSpoleczne z = Zus.Spoleczne(Pelny(chorobowe: true), Stawki2025, 1);
        SkladkiSpoleczne bez = Zus.Spoleczne(Pelny(chorobowe: false), Stawki2025, 1);

        Assert.Equal(0m, bez.Chorobowe);
        Assert.Equal(z.Razem - 127.49m, bez.Razem);
    }

    /// <summary>
    /// Przy składkach preferencyjnych nie ma Funduszu Pracy.
    /// </summary>
    /// <remarks>
    /// Fundusz Pracy należy się dopiero od podstawy sięgającej minimalnego
    /// wynagrodzenia, a podstawa preferencyjna to 30% minimalnego. Doliczenie
    /// go byłoby nadpłatą, o którą trzeba by potem wnioskować.
    /// </remarks>
    [Fact]
    public void PreferencyjneNieMajaFunduszuPracy()
    {
        SkladkiSpoleczne skladki = Zus.Spoleczne(
            new UstawieniaZus(TytulUbezpieczenia.Preferencyjny), Stawki2025, 1);

        Assert.Equal(0m, skladki.FunduszPracy);
        Assert.True(skladki.Emerytalna > 0m);
    }

    [Fact]
    public void PelnaPodstawaNiesieFunduszPracy()
    {
        SkladkiSpoleczne skladki = Zus.Spoleczne(Pelny(), Stawki2025, 1);

        Assert.True(skladki.FunduszPracy > 0m);
    }

    /// <summary>Zwolnienie wiekowe zdejmuje Fundusz Pracy mimo pełnej podstawy.</summary>
    [Fact]
    public void ZwolnienieWiekoweZdejmujeFunduszPracy()
    {
        SkladkiSpoleczne skladki = Zus.Spoleczne(
            new UstawieniaZus(TytulUbezpieczenia.Pelny, BezFunduszuPracy: true),
            Stawki2025, 1);

        Assert.Equal(0m, skladki.FunduszPracy);
        Assert.Equal(skladki.Ubezpieczenia, skladki.Razem);
    }

    /// <summary>Fundusz Pracy nie jest odliczeniem od dochodu.</summary>
    /// <remarks>
    /// Odliczyć od dochodu wolno składki na ubezpieczenia społeczne
    /// (art. 26 ust. 1 pkt 2 ustawy o PIT). Fundusz Pracy do nich nie należy,
    /// więc suma przelewu i suma odliczenia to dwie różne kwoty.
    /// </remarks>
    [Fact]
    public void FunduszPracyStoiPozaOdliczeniem()
    {
        SkladkiSpoleczne skladki = Zus.Spoleczne(Pelny(), Stawki2025, 1);

        Assert.NotEqual(skladki.Ubezpieczenia, skladki.Razem);
        Assert.Equal(skladki.Ubezpieczenia + skladki.FunduszPracy, skladki.Razem);
    }

    // --------------------------------------------------------- Mały ZUS Plus

    /// <summary>Podstawa Małego ZUS Plus to połowa przeciętnego dochodu.</summary>
    [Fact]
    public void MalyZusPlusLiczySieZDochodu()
    {
        // 120 000 / 365 × 30 = 9863,01; połowa = 4931,51 zł.
        decimal podstawa = Zus.PodstawaMalegoZusPlus(120_000m, 365, Stawki2025);

        Assert.Equal(4931.51m, podstawa);
    }

    /// <summary>
    /// Podstawa Małego ZUS Plus nigdy nie przekracza podstawy pełnej.
    /// </summary>
    /// <remarks>
    /// Inaczej ulga potrafiłaby wyjść drożej niż jej brak - a to nie jest ulga.
    /// </remarks>
    [Fact]
    public void MalyZusPlusNiePrzekraczaPodstawyPelnej()
    {
        decimal podstawa = Zus.PodstawaMalegoZusPlus(500_000m, 365, Stawki2025);

        Assert.Equal(Stawki2025.PodstawaPelna, podstawa);
    }

    /// <summary>Strata w poprzednim roku nie odbiera ulgi.</summary>
    [Fact]
    public void StrataDajePodstaweMinimalna()
    {
        decimal podstawa = Zus.PodstawaMalegoZusPlus(-20_000m, 365, Stawki2025);

        Assert.Equal(Stawki2025.PodstawaPreferencyjna(1), podstawa);
    }

    /// <summary>Działalność zaczęta w trakcie roku przelicza się na pełny miesiąc.</summary>
    [Fact]
    public void KrotszyRokPrzeliczaSieNaMiesiac()
    {
        // Ten sam dochód na pół roku daje dwa razy wyższą podstawę miesięczną.
        decimal caly = Zus.PodstawaMalegoZusPlus(60_000m, 360, Stawki2025);
        decimal polowa = Zus.PodstawaMalegoZusPlus(60_000m, 180, Stawki2025);

        Assert.Equal(2500.00m, caly);
        Assert.Equal(5000.00m, polowa);
    }

    /// <summary>Przychód ponad limit odbiera prawo do ulgi.</summary>
    [Fact]
    public void PrzychodPonadLimitOdbieraMalyZusPlus()
    {
        Assert.True(Zus.MalyZusPlusPrzysluguje(120_000m, Stawki2025));
        Assert.False(Zus.MalyZusPlusPrzysluguje(120_000.01m, Stawki2025));
    }

    // ----------------------------------------------------- składka zdrowotna

    /// <summary>Na skali zdrowotna to 9% dochodu.</summary>
    [Fact]
    public void NaSkaliZdrowotnaToDziewiecProcentDochodu()
    {
        SkladkaZdrowotna skladka = Zus.Zdrowotna(
            FormaOpodatkowania.Skala, 20_000m, Stawki2025);

        Assert.Equal(1800m, skladka.Kwota);
        Assert.False(skladka.ZNajnizszejPodstawy);
    }

    /// <summary>Na liniowym zdrowotna to 4,9% dochodu.</summary>
    [Fact]
    public void NaLiniowymZdrowotnaToCzteryDziewiecProcentDochodu()
    {
        SkladkaZdrowotna skladka = Zus.Zdrowotna(
            FormaOpodatkowania.Liniowy, 20_000m, Stawki2025);

        Assert.Equal(980m, skladka.Kwota);
    }

    /// <summary>
    /// Miesiąc bez dochodu i tak niesie składkę zdrowotną.
    /// </summary>
    /// <remarks>
    /// Najniższa podstawa to od 2025 roku 75% minimalnego wynagrodzenia,
    /// nie całe minimalne. Strata nie zwalnia ze składki - to zaskakuje
    /// najczęściej, bo społeczne przy zawieszeniu działalności odpadają,
    /// a zdrowotna zostaje.
    /// </remarks>
    [Fact]
    public void StrataNieZwalniaZeZdrowotnej()
    {
        SkladkaZdrowotna skladka = Zus.Zdrowotna(
            FormaOpodatkowania.Skala, -5_000m, Stawki2025);

        // 4666 × 75% = 3499,50; 9% z tego = 314,96 zł.
        Assert.Equal(3499.50m, skladka.Podstawa);
        Assert.Equal(314.96m, skladka.Kwota);
        Assert.True(skladka.ZNajnizszejPodstawy);
    }

    /// <summary>Przy liniowym dolna granica też jest liczona dziewięcioma procentami.</summary>
    /// <remarks>
    /// Stopa 4,9% dotyczy dochodu, ale minimalna składka jest dla wszystkich
    /// jednakowa - 9% najniższej podstawy.
    /// </remarks>
    [Fact]
    public void DolnaGranicaJestTaSamaDlaLiniowego()
    {
        SkladkaZdrowotna skladka = Zus.Zdrowotna(
            FormaOpodatkowania.Liniowy, 1_000m, Stawki2025);

        Assert.Equal(314.96m, skladka.Kwota);
        Assert.True(skladka.ZNajnizszejPodstawy);
    }

    /// <summary>Ryczałt ma trzy progi przychodu, nie stopę od dochodu.</summary>
    [Theory]
    [InlineData(50_000, 0.60)]
    [InlineData(60_000, 0.60)]
    [InlineData(60_000.01, 1.00)]
    [InlineData(300_000, 1.00)]
    [InlineData(300_000.01, 1.80)]
    public void RyczaltMaTrzyProgiPrzychodu(decimal przychod, decimal udzial)
    {
        decimal podstawa = Zus.PodstawaZdrowotnejRyczalt(przychod, Stawki2025);

        Assert.Equal(Kwoty.Zaokraglij(Stawki2025.PrzecietneDoRyczaltu * udzial), podstawa);
    }

    [Fact]
    public void ZdrowotnaRyczaltowcaToDziewiecProcentPodstawyProgu()
    {
        SkladkaZdrowotna skladka = Zus.Zdrowotna(
            FormaOpodatkowania.Ryczalt, 100_000m, Stawki2025);

        // Drugi próg: 100% z 8549,18; 9% z tego = 769,43 zł.
        Assert.Equal(8549.18m, skladka.Podstawa);
        Assert.Equal(769.43m, skladka.Kwota);
    }

    // ------------------------------------------------------------ odliczenia

    /// <summary>Na skali zdrowotnej nie odlicza się wcale.</summary>
    /// <remarks>
    /// To nie jest brak w programie, tylko stan prawny od 2022 roku.
    /// </remarks>
    [Fact]
    public void NaSkaliZdrowotnaNieJestOdliczana()
    {
        Assert.Equal(0m,
            Zus.ZdrowotnaDoOdliczenia(FormaOpodatkowania.Skala, 10_000m, Stawki2025));
    }

    [Fact]
    public void NaLiniowymZdrowotnaOdliczaSieDoLimitu()
    {
        Assert.Equal(10_000m,
            Zus.ZdrowotnaDoOdliczenia(FormaOpodatkowania.Liniowy, 10_000m, Stawki2025));

        // Limit roczny obcina nadwyżkę.
        Assert.Equal(12_900m,
            Zus.ZdrowotnaDoOdliczenia(FormaOpodatkowania.Liniowy, 20_000m, Stawki2025));
    }

    [Fact]
    public void NaRyczalcieOdliczaSiePolowaZdrowotnej()
    {
        Assert.Equal(5_000m,
            Zus.ZdrowotnaDoOdliczenia(FormaOpodatkowania.Ryczalt, 10_000m, Stawki2025));
    }

    // --------------------------------------------------- rozliczenie roczne

    /// <summary>Roczne rozliczenie wychwytuje niedopłatę z miesięcy.</summary>
    [Fact]
    public void RocznaDoplataGdyMiesiaceBylyNizsze()
    {
        RozliczenieZdrowotnej rozliczenie = Zus.RozliczRoczna(
            FormaOpodatkowania.Skala,
            podstawaRoczna: 200_000m,
            zaplaconoWRoku: 15_000m,
            Stawki2025);

        Assert.Equal(18_000m, rozliczenie.Nalezna);
        Assert.Equal(3_000m, rozliczenie.DoDoplaty);
        Assert.Equal(0m, rozliczenie.DoZwrotu);
    }

    [Fact]
    public void RocznyZwrotGdyZaplaconoWiecej()
    {
        RozliczenieZdrowotnej rozliczenie = Zus.RozliczRoczna(
            FormaOpodatkowania.Skala,
            podstawaRoczna: 100_000m,
            zaplaconoWRoku: 10_000m,
            Stawki2025);

        Assert.Equal(1_000m, rozliczenie.DoZwrotu);
        Assert.Equal(0m, rozliczenie.DoDoplaty);
    }

    /// <summary>Roczna składka też ma dolną granicę.</summary>
    [Fact]
    public void RocznaNieSpadaPonizejDwunastuMinimalnych()
    {
        RozliczenieZdrowotnej rozliczenie = Zus.RozliczRoczna(
            FormaOpodatkowania.Skala,
            podstawaRoczna: 0m,
            zaplaconoWRoku: 0m,
            Stawki2025);

        // 314,96 × 12 = 3779,46 zł (liczone z podstawy, nie z zaokrąglonej składki).
        Assert.Equal(Kwoty.Zaokraglij(3499.50m * 0.09m * 12m), rozliczenie.Nalezna);
    }

    // ---------------------------------------------------------------- termin

    /// <summary>Składki płaci się do 20. dnia następnego miesiąca.</summary>
    [Fact]
    public void TerminToDwudziestyNastepnegoMiesiaca()
    {
        DateOnly termin = Zus.Termin(OkresRozliczeniowy.Miesiac(2025, 3));

        Assert.Equal(new DateOnly(2025, 4, 22), termin);  // 20 kwietnia to niedziela,
                                                          // 21 - poniedziałek wielkanocny
    }

    [Fact]
    public void TerminWDzienRoboczyZostajeNaMiejscu()
    {
        Assert.Equal(new DateOnly(2025, 2, 20), Zus.Termin(OkresRozliczeniowy.Miesiac(2025, 1)));
    }

    /// <summary>Termin z grudnia wypada w styczniu roku następnego.</summary>
    [Fact]
    public void TerminZaGrudzienWypadaWStyczniu()
    {
        Assert.Equal(new DateOnly(2026, 1, 20), Zus.Termin(OkresRozliczeniowy.Miesiac(2025, 12)));
    }

    // ------------------------------------------------------- brak stawek roku

    /// <summary>
    /// Roku bez wpisanych kwot program nie liczy zeszłorocznymi.
    /// </summary>
    /// <remarks>
    /// Rachunek wyszedłby wtedy wiarygodnie wyglądającą kwotą i niedopłatą,
    /// o której nikt by się nie dowiedział do czasu kontroli.
    /// </remarks>
    [Fact]
    public void NieznanyRokNieDajeStawek()
    {
        Assert.Null(StawkiZus.Dla(2019));
        Assert.NotNull(StawkiZus.Dla(2025));
    }

    /// <summary>Rok wpisany wstępnie jest oznaczony jako niepotwierdzony.</summary>
    [Fact]
    public void RokWstepnyJestOznaczony()
    {
        Assert.True(StawkiZus.Dla(2025)!.Potwierdzone);
        Assert.False(StawkiZus.Dla(2026)!.Potwierdzone);
    }
}

/// <summary>Dni wolne i przesuwanie terminów.</summary>
public sealed class TestyDniRoboczych
{
    /// <summary>Wielkanoc wyznacza trzy dni wolne w roku.</summary>
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    public void WielkanocLiczySiePoprawnie(int rok, int miesiac, int dzien)
    {
        Assert.Equal(new DateOnly(rok, miesiac, dzien), DniRobocze.Wielkanoc(rok));
    }

    [Fact]
    public void BozeCialoJestSzescdziesiatDniPoWielkanocy()
    {
        // 2025: Wielkanoc 20 kwietnia, Boże Ciało 19 czerwca.
        Assert.True(DniRobocze.Wolny(new DateOnly(2025, 6, 19)));
    }

    [Fact]
    public void SobotaINiedzielaSaWolne()
    {
        Assert.True(DniRobocze.Wolny(new DateOnly(2025, 6, 7)));   // sobota
        Assert.True(DniRobocze.Wolny(new DateOnly(2025, 6, 8)));   // niedziela
        Assert.False(DniRobocze.Wolny(new DateOnly(2025, 6, 9)));  // poniedziałek
    }

    /// <summary>Wigilia jest dniem wolnym dopiero od 2025 roku.</summary>
    [Fact]
    public void WigiliaJestWolnaOd2025()
    {
        Assert.False(DniRobocze.Wolny(new DateOnly(2024, 12, 24)));
        Assert.True(DniRobocze.Wolny(new DateOnly(2025, 12, 24)));
    }

    /// <summary>Termin przesuwa się do przodu, nigdy do tyłu.</summary>
    [Fact]
    public void TerminPrzesuwaSieDoPrzodu()
    {
        // 1 listopada 2025 to sobota, 2 listopada - niedziela.
        Assert.Equal(new DateOnly(2025, 11, 3),
            DniRobocze.NajblizszyRoboczy(new DateOnly(2025, 11, 1)));
    }

    [Fact]
    public void DzienRoboczyZostajeNaMiejscu()
    {
        DateOnly wtorek = new(2025, 9, 16);

        Assert.Equal(wtorek, DniRobocze.NajblizszyRoboczy(wtorek));
    }
}
