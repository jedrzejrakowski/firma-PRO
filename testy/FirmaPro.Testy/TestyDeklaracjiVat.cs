using FirmaPro.Domena;

namespace FirmaPro.Testy;

/// <summary>
/// Wyliczanie części deklaracyjnej JPK_V7.
/// </summary>
/// <remarks>
/// Kształtu pliku XML nie da się sprawdzić bez oficjalnego schematu, ale
/// kwoty owszem - i to one decydują o tym, ile podatnik zapłaci. Stąd nacisk
/// na zaokrąglenia, zgodność sum z pozycjami składowymi i przenoszenie
/// nadwyżki między okresami.
/// </remarks>
public sealed class TestyDeklaracjiVat
{
    private static readonly OkresRozliczeniowy Sierpien = OkresRozliczeniowy.Miesiac(2026, 8);

    [Fact]
    public void SprzedazTrafiaDoWlasciwychPol()
    {
        DeklaracjaVat deklaracja = Zbuduj(sprzedaz:
        [
            new KwotyWStawce(StawkaVat.Vat23, 10000m, 2300m),
            new KwotyWStawce(StawkaVat.Vat8, 5000m, 400m),
            new KwotyWStawce(StawkaVat.Vat5, 1000m, 50m),
            new KwotyWStawce(StawkaVat.Zwolniona, 700m, 0m)
        ]);

        Assert.Equal(10000, deklaracja.Pole(19));
        Assert.Equal(2300, deklaracja.Pole(20));
        Assert.Equal(5000, deklaracja.Pole(17));
        Assert.Equal(400, deklaracja.Pole(18));
        Assert.Equal(1000, deklaracja.Pole(15));
        Assert.Equal(50, deklaracja.Pole(16));
        Assert.Equal(700, deklaracja.Pole(10));

        Assert.Equal(2750, deklaracja.PodatekNalezny);
    }

    /// <summary>
    /// Stawka 22% dzieli pole z 23%, a 7% z 8%.
    /// </summary>
    /// <remarks>
    /// Stawki historyczne występują na korektach faktur sprzed obniżki
    /// i wykazuje się je w tych samych pozycjach co obecne.
    /// </remarks>
    [Fact]
    public void StawkiHistoryczneDzielaPoleZObecnymi()
    {
        DeklaracjaVat deklaracja = Zbuduj(sprzedaz:
        [
            new KwotyWStawce(StawkaVat.Vat23, 1000m, 230m),
            new KwotyWStawce(StawkaVat.Vat22, 1000m, 220m)
        ]);

        Assert.Equal(2000, deklaracja.Pole(19));
        Assert.Equal(450, deklaracja.Pole(20));
    }

    [Fact]
    public void ZeroWTrzechOdmianachTrafiaDoTrzechPol()
    {
        DeklaracjaVat deklaracja = Zbuduj(sprzedaz:
        [
            new KwotyWStawce(StawkaVat.ZeroKrajowa, 100m, 0m),
            new KwotyWStawce(StawkaVat.ZeroWdt, 200m, 0m),
            new KwotyWStawce(StawkaVat.ZeroEksport, 300m, 0m)
        ]);

        Assert.Equal(100, deklaracja.Pole(13));
        Assert.Equal(200, deklaracja.Pole(21));
        Assert.Equal(300, deklaracja.Pole(22));
        Assert.Equal(0, deklaracja.PodatekNalezny);
    }

    /// <summary>
    /// Usługi z art. 100 ust. 1 pkt 4 wykazuje się w dwóch polach naraz.
    /// </summary>
    [Fact]
    public void UslugiUnijneWykazywaneSaWDwochPolach()
    {
        DeklaracjaVat deklaracja = Zbuduj(sprzedaz:
            [new KwotyWStawce(StawkaVat.NiepodlegajacaII, 4000m, 0m)]);

        Assert.Equal(4000, deklaracja.Pole(11));
        Assert.Equal(4000, deklaracja.Pole(12));
    }

    [Fact]
    public void ZakupyTrafiajaDoPolCzterdziestych()
    {
        DeklaracjaVat deklaracja = Zbuduj(zakupy:
        [
            Zakup(8000m, RodzajZakupu.SrodkiTrwale),
            Zakup(2000m, RodzajZakupu.TowaryIUslugi)
        ]);

        Assert.Equal(8000, deklaracja.Pole(40));
        Assert.Equal(1840, deklaracja.Pole(41));
        Assert.Equal(2000, deklaracja.Pole(42));
        Assert.Equal(460, deklaracja.Pole(43));
        Assert.Equal(2300, deklaracja.PodatekNaliczony);
    }

    /// <summary>
    /// Kwoty deklaracji podaje się w pełnych złotych.
    /// </summary>
    /// <remarks>
    /// Końcówki poniżej 50 groszy pomija się, od 50 groszy podwyższa
    /// (art. 63 § 1 Ordynacji podatkowej). Domyślne zaokrąglanie w .NET jest
    /// bankierskie i dałoby tu 2 zamiast 3 dla kwoty 2,50.
    /// </remarks>
    [Theory]
    [InlineData(100.49, 100)]
    [InlineData(100.50, 101)]
    [InlineData(100.51, 101)]
    [InlineData(2.50, 3)]
    [InlineData(3.50, 4)]
    [InlineData(0.49, 0)]
    public void KwotyZaokraglaneSaDoPelnychZlotych(double netto, long oczekiwane)
    {
        DeklaracjaVat deklaracja = Zbuduj(sprzedaz:
            [new KwotyWStawce(StawkaVat.Zwolniona, (decimal)netto, 0m)]);

        Assert.Equal(oczekiwane, deklaracja.Pole(10));
    }

    /// <summary>
    /// Suma podatku należnego zgadza się z polami składowymi.
    /// </summary>
    /// <remarks>
    /// P_38 liczone jest z pól już zaokrąglonych, a nie z kwot w groszach.
    /// Inaczej suma na dole deklaracji nie zgadzałaby się z pozycjami
    /// widocznymi wyżej - a taka niezgodność od razu rzuca się w oczy.
    /// </remarks>
    [Fact]
    public void SumaPodatkuZgadzaSieZPolamiSkladowymi()
    {
        DeklaracjaVat deklaracja = Zbuduj(sprzedaz:
        [
            new KwotyWStawce(StawkaVat.Vat23, 100.40m, 23.09m),
            new KwotyWStawce(StawkaVat.Vat8, 100.40m, 8.03m),
            new KwotyWStawce(StawkaVat.Vat5, 100.40m, 5.02m)
        ]);

        long suma = deklaracja.Pole(16) + deklaracja.Pole(18) + deklaracja.Pole(20);

        Assert.Equal(suma, deklaracja.PodatekNalezny);
    }

    [Fact]
    public void PodatekDoWplatyToRoznicaNaKorzyscUrzedu()
    {
        DeklaracjaVat deklaracja = Zbuduj(
            sprzedaz: [new KwotyWStawce(StawkaVat.Vat23, 10000m, 2300m)],
            zakupy: [Zakup(4000m, RodzajZakupu.TowaryIUslugi)]);

        Assert.Equal(2300, deklaracja.PodatekNalezny);
        Assert.Equal(920, deklaracja.PodatekNaliczony);
        Assert.Equal(1380, deklaracja.DoWplaty);
        Assert.Equal(0, deklaracja.Nadwyzka);
    }

    [Fact]
    public void PrzewagaZakupowDajeNadwyzke()
    {
        DeklaracjaVat deklaracja = Zbuduj(
            sprzedaz: [new KwotyWStawce(StawkaVat.Vat23, 1000m, 230m)],
            zakupy: [Zakup(9000m, RodzajZakupu.TowaryIUslugi)]);

        Assert.Equal(0, deklaracja.DoWplaty);
        Assert.Equal(1840, deklaracja.Nadwyzka);
        Assert.Equal(1840, deklaracja.DoPrzeniesienia);
    }

    /// <summary>
    /// Nadwyżka z poprzedniego okresu pomniejsza podatek do zapłaty.
    /// </summary>
    /// <remarks>
    /// To różnica między rejestrem a deklaracją: rejestr pokazuje wynik
    /// samego okresu, deklaracja uwzględnia jeszcze to, co przeszło
    /// z poprzedniego miesiąca.
    /// </remarks>
    [Fact]
    public void NadwyzkaZPoprzedniegoOkresuWchodziDoPodatkuNaliczonego()
    {
        DeklaracjaVat deklaracja = Zbuduj(
            sprzedaz: [new KwotyWStawce(StawkaVat.Vat23, 10000m, 2300m)],
            zakupy: [Zakup(4000m, RodzajZakupu.TowaryIUslugi)],
            nadwyzka: 500);

        Assert.Equal(500, deklaracja.NadwyzkaZPoprzedniegoOkresu);
        Assert.Equal(1420, deklaracja.PodatekNaliczony);
        Assert.Equal(880, deklaracja.DoWplaty);
    }

    /// <summary>
    /// Nadwyżka większa od podatku należnego przechodzi dalej.
    /// </summary>
    [Fact]
    public void NadwyzkaWiekszaNizPodatekPrzechodziNaNastepnyOkres()
    {
        DeklaracjaVat deklaracja = Zbuduj(
            sprzedaz: [new KwotyWStawce(StawkaVat.Vat23, 1000m, 230m)],
            nadwyzka: 1000);

        Assert.Equal(0, deklaracja.DoWplaty);
        Assert.Equal(770, deklaracja.Nadwyzka);
        Assert.Equal(770, deklaracja.DoPrzeniesienia);
    }

    /// <summary>
    /// Stawka bez odpowiednika w deklaracji nie znika po cichu.
    /// </summary>
    /// <remarks>
    /// Ryczałt dla taksówek rozlicza się deklaracją VAT-12. Pominięcie kwoty
    /// bez słowa dawałoby deklarację, która nie zgadza się z ewidencją,
    /// a podatnik nie miałby jak się o tym dowiedzieć.
    /// </remarks>
    [Fact]
    public void StawkaSpozaDeklaracjiDajeOstrzezenie()
    {
        DeklaracjaVat deklaracja = Zbuduj(sprzedaz:
            [new KwotyWStawce(StawkaVat.Vat4, 1000m, 40m)]);

        Assert.False(deklaracja.Walidacja.BezZastrzezen);
        Assert.Contains("nie została w niej ujęta", deklaracja.Walidacja.Opis(),
            StringComparison.Ordinal);

        // Kwota nie może po cichu wejść do żadnego pola deklaracji.
        Assert.Empty(deklaracja.WypelnionePola);
    }

    [Fact]
    public void PustyOkresDajeDeklaracjeBezPol()
    {
        DeklaracjaVat deklaracja = Zbuduj();

        Assert.Empty(deklaracja.WypelnionePola);
        Assert.Equal(0, deklaracja.PodatekNalezny);
        Assert.Equal(0, deklaracja.DoWplaty);
    }

    [Fact]
    public void UjemnaNadwyzkaJestOdrzucana() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeklaracjaVat.Zbuduj(RejestrVat.Zbuduj(Sierpien, [], []), -1));

    // ------------------------------------------------------------ pomocnicze

    private static DeklaracjaVat Zbuduj(IReadOnlyList<KwotyWStawce>? sprzedaz = null,
                                        IReadOnlyList<WpisZakupu>? zakupy = null,
                                        long nadwyzka = 0)
    {
        var dzien = new DateOnly(2026, 8, 10);

        WpisSprzedazy[] wpisy = sprzedaz is null or { Count: 0 }
            ? []
            : [new WpisSprzedazy("FV/1", dzien, dzien, "Klient", null, null, sprzedaz)];

        return DeklaracjaVat.Zbuduj(
            RejestrVat.Zbuduj(Sierpien, wpisy, zakupy ?? []), nadwyzka);
    }

    private static WpisZakupu Zakup(decimal netto, RodzajZakupu rodzaj)
    {
        var dzien = new DateOnly(2026, 8, 10);

        return new WpisZakupu("FZ/1", dzien, dzien, dzien, "Dostawca", "1180000001",
            rodzaj, Odliczany: true, netto, StawkaVat.Vat23.PodatekOd(netto));
    }
}
