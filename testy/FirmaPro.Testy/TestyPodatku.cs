using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Zaliczka na podatek dochodowy.
/// </summary>
/// <remarks>
/// Zaliczka jest kwotą, którą przedsiębiorca faktycznie przelewa do urzędu.
/// Pomyłka o złotówkę jest nieszkodliwa, pomyłka w regule - nie: zaniżona
/// zaliczka to odsetki, zawyżona to pieniądze zamrożone do maja.
/// </remarks>
public sealed class TestyPodatku
{
    private static readonly SkalaPodatkowa Skala = SkalaPodatkowa.Dla(2025)!;
    private static readonly OkresRozliczeniowy Czerwiec = OkresRozliczeniowy.Miesiac(2025, 6);

    // ------------------------------------------------------------ sama skala

    /// <summary>
    /// Kwota wolna działa przez zmniejszenie podatku, nie przez zmniejszenie podstawy.
    /// </summary>
    /// <remarks>
    /// Odjęcie 30 000 zł od podstawy dałoby zupełnie inną kwotę - to najczęstsze
    /// nieporozumienie wokół kwoty wolnej.
    /// </remarks>
    [Fact]
    public void KwotaWolnaDzialaPrzezZmniejszeniePodatku()
    {
        Assert.Equal(3_600m, Skala.KwotaZmniejszajaca);

        // 50 000 × 12% = 6000, minus 3600 = 2400 zł.
        Assert.Equal(2_400m, Skala.Podatek(50_000m));

        // Gdyby kwota wolna szła od podstawy: (50 000 − 30 000) × 12% = 2400.
        // Przy tej stawce wychodzi tyle samo - dlatego test idzie dalej,
        // do progu, gdzie oba rachunki się rozjeżdżają.
    }

    /// <summary>Dochód poniżej kwoty wolnej nie daje podatku.</summary>
    [Fact]
    public void PonizejKwotyWolnejNieMaPodatku()
    {
        Assert.Equal(0m, Skala.Podatek(30_000m));
        Assert.Equal(0m, Skala.Podatek(10_000m));
        Assert.Equal(0m, Skala.Podatek(0m));
    }

    /// <summary>Podatek nigdy nie wychodzi ujemny.</summary>
    [Fact]
    public void PodatekNieBywaUjemny()
    {
        Assert.Equal(0m, Skala.Podatek(-50_000m));
    }

    /// <summary>Na progu podatek wynosi 10 800 zł.</summary>
    [Fact]
    public void NaProguPodatekToDziesiecOsiemset()
    {
        // 120 000 × 12% = 14 400, minus 3600 = 10 800 zł.
        Assert.Equal(10_800m, Skala.Podatek(120_000m));
        Assert.False(Skala.PonadProgiem(120_000m));
    }

    /// <summary>Ponad progiem druga stawka dotyczy samej nadwyżki.</summary>
    /// <remarks>
    /// Nie całego dochodu - to drugie z najczęstszych nieporozumień
    /// wokół skali podatkowej.
    /// </remarks>
    [Fact]
    public void DrugaStawkaDotyczySamejNadwyzki()
    {
        // 10 800 + (30 000 × 32%) = 10 800 + 9600 = 20 400 zł.
        Assert.Equal(20_400m, Skala.Podatek(150_000m));
        Assert.True(Skala.PonadProgiem(150_000m));

        // Gdyby 32% szło od całości, wyszłoby 48 000 zł.
        Assert.NotEqual(48_000m, Skala.Podatek(150_000m));
    }

    // -------------------------------------------------------------- zaliczka

    /// <summary>Zaliczka liczy się od dochodu narastająco, minus wpłacone wcześniej.</summary>
    [Fact]
    public void ZaliczkaOdejmujeWczesniejszeWplaty()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.Zaliczka(
            Czerwiec, dochodNarastajaco: 100_000m, odliczenia: 0m,
            Skala, zaplaconeWczesniej: 5_000L);

        // 100 000 × 12% − 3600 = 8400 zł podatku narastająco.
        Assert.Equal(8_400L, zaliczka.PodatekNarastajaco);
        Assert.Equal(3_400L, zaliczka.DoZaplaty);
    }

    /// <summary>
    /// Strata w okresie nie daje zwrotu w trakcie roku.
    /// </summary>
    /// <remarks>
    /// Podatek narastająco spada poniżej wpłaconych zaliczek, ale nadwyżki
    /// nie odzyskuje się miesięcznie - dopiero w zeznaniu rocznym. Zaliczka
    /// ujemna byłaby kwotą, której nie da się przelać.
    /// </remarks>
    [Fact]
    public void StrataNieDajeUjemnejZaliczki()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.Zaliczka(
            Czerwiec, dochodNarastajaco: 20_000m, odliczenia: 0m,
            Skala, zaplaconeWczesniej: 5_000L);

        Assert.Equal(0L, zaliczka.PodatekNarastajaco);
        Assert.Equal(0L, zaliczka.DoZaplaty);
        Assert.False(zaliczka.CosDoZaplaty);
    }

    /// <summary>Składki obniżają podstawę, a nie podatek.</summary>
    [Fact]
    public void SkladkiObnizajaPodstawe()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.Zaliczka(
            Czerwiec, dochodNarastajaco: 100_000m, odliczenia: 10_000m,
            Skala, zaplaconeWczesniej: 0L);

        Assert.Equal(90_000L, zaliczka.Podstawa);

        // 90 000 × 12% − 3600 = 7200 zł.
        Assert.Equal(7_200L, zaliczka.PodatekNarastajaco);
    }

    /// <summary>Podstawa zaokrągla się do pełnych złotych.</summary>
    /// <remarks>
    /// Art. 63 § 1 Ordynacji podatkowej: końcówki poniżej 50 groszy pomija się,
    /// od 50 groszy podwyższa. Zaokrąglenie idzie przed policzeniem podatku,
    /// nie po - inaczej wynik nie zgadzałby się z deklaracją.
    /// </remarks>
    [Fact]
    public void PodstawaZaokraglaSieDoZlotych()
    {
        ZaliczkaPit wDol = PodatekDochodowy.Zaliczka(
            Czerwiec, 50_000.49m, 0m, Skala, 0L);

        ZaliczkaPit wGore = PodatekDochodowy.Zaliczka(
            Czerwiec, 50_000.50m, 0m, Skala, 0L);

        Assert.Equal(50_000L, wDol.Podstawa);
        Assert.Equal(50_001L, wGore.Podstawa);
    }

    /// <summary>Odliczenia większe niż dochód dają zero, nie liczbę ujemną.</summary>
    [Fact]
    public void OdliczeniaPonadDochodDajaZero()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.Zaliczka(
            Czerwiec, dochodNarastajaco: 5_000m, odliczenia: 20_000m,
            Skala, zaplaconeWczesniej: 0L);

        Assert.Equal(0L, zaliczka.Podstawa);
        Assert.Equal(0L, zaliczka.PodatekNarastajaco);
    }

    // ------------------------------------------------------ podatek liniowy

    /// <summary>Liniowy to 19% bez kwoty wolnej.</summary>
    /// <remarks>
    /// Przy niskim dochodzie liniowy wychodzi drożej niż skala właśnie dlatego,
    /// że nie ma tu kwoty wolnej - program musi to pokazywać, a nie wygładzać.
    /// </remarks>
    [Fact]
    public void LiniowyNieMaKwotyWolnej()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.Zaliczka(
            Czerwiec, dochodNarastajaco: 30_000m, odliczenia: 0m,
            skala: null, zaplaconeWczesniej: 0L);

        Assert.Equal(5_700L, zaliczka.PodatekNarastajaco);

        // Ten sam dochód na skali nie daje podatku wcale.
        Assert.Equal(0m, Skala.Podatek(30_000m));
    }

    [Fact]
    public void LiniowyNieZnaProgu()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.Zaliczka(
            Czerwiec, dochodNarastajaco: 500_000m, odliczenia: 0m,
            skala: null, zaplaconeWczesniej: 0L);

        Assert.Equal(95_000L, zaliczka.PodatekNarastajaco);
    }

    // -------------------------------------------------------------- ryczałt

    /// <summary>Podatek liczy się osobno w każdej stawce.</summary>
    [Fact]
    public void RyczaltLiczySieStawkaPoStawce()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.ZaliczkaRyczalt(
            Czerwiec,
            [new PrzychodWStawce(12m, 100_000m), new PrzychodWStawce(8.5m, 40_000m)],
            odliczenia: 0m,
            zaplaconeWczesniej: 0L);

        // 100 000 × 12% = 12 000, 40 000 × 8,5% = 3400.
        Assert.Equal(15_400L, zaliczka.PodatekNarastajaco);
    }

    /// <summary>
    /// Odliczenia dzielą się między stawki proporcjonalnie do przychodu.
    /// </summary>
    /// <remarks>
    /// Odliczenie w całości od najwyższej stawki byłoby korzystniejsze, ale
    /// przepis go nie przewiduje - program nie może wybierać wariantu
    /// wygodniejszego dla podatnika.
    /// </remarks>
    [Fact]
    public void OdliczeniaDzielaSieProporcjonalnie()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.ZaliczkaRyczalt(
            Czerwiec,
            [new PrzychodWStawce(12m, 75_000m), new PrzychodWStawce(8.5m, 25_000m)],
            odliczenia: 10_000m,
            zaplaconeWczesniej: 0L);

        // Udziały 75% i 25%, więc odliczenie 7500 i 2500.
        Assert.Equal(7_500m, zaliczka.WedlugStawek[0].Odliczenie);
        Assert.Equal(2_500m, zaliczka.WedlugStawek[1].Odliczenie);

        // (75 000 − 7500) × 12% = 8100; (25 000 − 2500) × 8,5% = 1912,50 → 1913.
        Assert.Equal(8_100L + 1_913L, zaliczka.PodatekNarastajaco);
    }

    /// <summary>Podział odliczeń nie gubi ani nie tworzy grosza.</summary>
    /// <remarks>
    /// Reszta z dzielenia trafia do ostatniej stawki - inaczej suma odliczeń
    /// nie zgadzałaby się z kwotą faktycznie zapłaconych składek.
    /// </remarks>
    [Fact]
    public void PodzialOdliczenNieGubiGrosza()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.ZaliczkaRyczalt(
            Czerwiec,
            [
                new PrzychodWStawce(12m, 10_000m),
                new PrzychodWStawce(8.5m, 10_000m),
                new PrzychodWStawce(5.5m, 10_000m)
            ],
            odliczenia: 1_000m,
            zaplaconeWczesniej: 0L);

        Assert.Equal(1_000m, zaliczka.WedlugStawek.Sum(s => s.Odliczenie));
    }

    /// <summary>Odliczenie większe niż przychód w stawce nie daje ujemnej podstawy.</summary>
    [Fact]
    public void OdliczeniePonadPrzychodDajeZeroWStawce()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.ZaliczkaRyczalt(
            Czerwiec,
            [new PrzychodWStawce(12m, 5_000m)],
            odliczenia: 20_000m,
            zaplaconeWczesniej: 0L);

        Assert.Equal(0m, zaliczka.WedlugStawek[0].Podstawa);
        Assert.Equal(0L, zaliczka.PodatekNarastajaco);
    }

    [Fact]
    public void RyczaltBezPrzychoduNieDajePodatku()
    {
        ZaliczkaPit zaliczka = PodatekDochodowy.ZaliczkaRyczalt(
            Czerwiec, [], odliczenia: 5_000m, zaplaconeWczesniej: 0L);

        Assert.Empty(zaliczka.WedlugStawek);
        Assert.Equal(0L, zaliczka.PodatekNarastajaco);
    }

    // --------------------------------------------------------------- termin

    /// <summary>Zaliczkę płaci się do 20. dnia następnego miesiąca.</summary>
    [Fact]
    public void TerminToDwudziestyNastepnegoMiesiaca()
    {
        Assert.Equal(new DateOnly(2025, 7, 21),
            PodatekDochodowy.Termin(OkresRozliczeniowy.Miesiac(2025, 6)));  // 20 lipca to niedziela
    }

    /// <summary>Zaliczka za grudzień płatna jest w styczniu na zwykłych zasadach.</summary>
    /// <remarks>
    /// Dawna reguła nakazująca zapłacić ją wcześniej już nie obowiązuje.
    /// </remarks>
    [Fact]
    public void ZaliczkaZaGrudzienPlatnaWStyczniu()
    {
        Assert.Equal(new DateOnly(2026, 1, 20),
            PodatekDochodowy.Termin(OkresRozliczeniowy.Miesiac(2025, 12)));
    }

    /// <summary>Przy rozliczeniu kwartalnym termin liczy się od końca kwartału.</summary>
    [Fact]
    public void TerminKwartalnyLiczySieOdKoncaKwartalu()
    {
        Assert.Equal(new DateOnly(2025, 4, 22),
            PodatekDochodowy.Termin(OkresRozliczeniowy.Kwartal(2025, 1)));
    }

    // ----------------------------------------------------------- brak skali

    /// <summary>Roku bez wpisanej skali program nie liczy skalą z innego.</summary>
    [Fact]
    public void NieznanyRokNieMaSkali()
    {
        Assert.Null(SkalaPodatkowa.Dla(2019));
        Assert.NotNull(SkalaPodatkowa.Dla(2025));
    }
}

/// <summary>
/// Podstawa zaliczki liczona z księgi.
/// </summary>
/// <remarks>
/// Osobna klasa, bo to nie jest rachunek podatkowy, tylko odpowiedź na pytanie
/// „od czego”. Wzięcie złej podstawy jest groźniejsze niż zła stawka - stawkę
/// widać, podstawę trzeba wyprowadzić.
/// </remarks>
public sealed class TestyPodstawyZaliczki
{
    private static readonly OkresRozliczeniowy Czerwiec = OkresRozliczeniowy.Miesiac(2025, 6);

    private static Kpir Ksiega(decimal przychod, decimal zakupTowarow, decimal pozostale)
    {
        var data = new DateOnly(2025, 3, 10);

        return Kpir.Zbuduj(Czerwiec,
        [
            new WpisKsiegi(data, "FV/1", "Klient", null, "Sprzedaż",
                KolumnaKpir.SprzedazTowarowIUslug, przychod),
            new WpisKsiegi(data, "FZ/1", "Dostawca", null, "Towar",
                KolumnaKpir.ZakupTowarow, zakupTowarow),
            new WpisKsiegi(data, "FZ/2", "Biuro", null, "Materiały",
                KolumnaKpir.PozostaleWydatki, pozostale)
        ]);
    }

    /// <summary>
    /// Podstawa zaliczki obejmuje zakup towarów, choć kolumna 14 go nie obejmuje.
    /// </summary>
    /// <remarks>
    /// Kolumna 14 sumuje wyłącznie wynagrodzenia i pozostałe wydatki, bo zakup
    /// towarów rozlicza się przez spis z natury. Wzięcie różnicy kolumn 9 i 14
    /// wprost jako podstawy zaliczki zawyżyłoby dochód firmy handlowej
    /// o wartość całego zakupionego towaru.
    /// </remarks>
    [Fact]
    public void PodstawaObejmujeZakupTowarow()
    {
        Kpir ksiega = Ksiega(przychod: 100_000m, zakupTowarow: 60_000m, pozostale: 5_000m);

        // Tak pokazuje to księga - kolumna 9 minus kolumna 14.
        Assert.Equal(95_000m, ksiega.DochodNarastajaco);

        // A tak liczy się podatek: 100 000 − 60 000 − 5000 = 35 000 zł.
        RozliczenieRoczne podstawa = RozliczenieRoczne.Zbuduj(ksiega, null, null);

        Assert.Equal(35_000m, podstawa.Dochod);
    }

    /// <summary>Spis śródroczny koryguje podstawę o towar leżący w magazynie.</summary>
    [Fact]
    public void SpisSrodrocznyKorygujePodstawe()
    {
        Kpir ksiega = Ksiega(przychod: 100_000m, zakupTowarow: 60_000m, pozostale: 5_000m);

        var remanent = new SpisZNatury(new DateOnly(2025, 6, 30),
            [new PozycjaSpisu("Towar", "szt.", 1m, 20_000m)]);

        RozliczenieRoczne podstawa = RozliczenieRoczne.Zbuduj(ksiega, null, remanent);

        // Towar za 20 000 zł został na półce, więc kosztem jest 40 000, nie 60 000.
        Assert.Equal(55_000m, podstawa.Dochod);
    }
}
