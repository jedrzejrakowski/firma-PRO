using FirmaPro.Domena;

namespace FirmaPro.Testy;

/// <summary>Składanie rejestru VAT z dokumentów.</summary>
public sealed class TestyRejestruVat
{
    private static readonly OkresRozliczeniowy Sierpien = OkresRozliczeniowy.Miesiac(2026, 8);

    [Fact]
    public void RejestrBierzeTylkoDokumentyZOkresu()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien,
        [
            Sprzedaz("FV/1", new DateOnly(2026, 7, 31), 100m),
            Sprzedaz("FV/2", new DateOnly(2026, 8, 1), 200m),
            Sprzedaz("FV/3", new DateOnly(2026, 8, 31), 300m),
            Sprzedaz("FV/4", new DateOnly(2026, 9, 1), 400m)
        ],
        [
            Zakup("Z/1", new DateOnly(2026, 8, 15), 500m),
            Zakup("Z/2", new DateOnly(2026, 9, 15), 600m)
        ]);

        Assert.Equal(["FV/2", "FV/3"], rejestr.Sprzedaz.Wpisy.Select(w => w.Numer));
        Assert.Equal(["Z/1"], rejestr.Zakupy.Wpisy.Select(w => w.Numer));
    }

    [Fact]
    public void PodsumowanieSprzedazyGrupujeStawki()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien,
        [
            new WpisSprzedazy("FV/1", new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 5),
                "Klient A", "7010001453", null,
                [
                    new KwotyWStawce(StawkaVat.Vat23, 1000m, 230m),
                    new KwotyWStawce(StawkaVat.Vat8, 500m, 40m)
                ]),
            new WpisSprzedazy("FV/2", new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 9),
                "Klient B", null, null,
                [
                    new KwotyWStawce(StawkaVat.Vat23, 2000m, 460m),
                    new KwotyWStawce(StawkaVat.Zwolniona, 300m, 0m)
                ])
        ], []);

        IReadOnlyList<KwotyWStawce> stawki = rejestr.Sprzedaz.WedlugStawek;

        Assert.Equal(3, stawki.Count);

        // Kolejność ze schematu FA(3): 23%, 8%, zwolniona.
        Assert.Equal(StawkaVat.Vat23, stawki[0].Stawka);
        Assert.Equal(3000m, stawki[0].Netto);
        Assert.Equal(690m, stawki[0].Vat);

        Assert.Equal(StawkaVat.Vat8, stawki[1].Stawka);
        Assert.Equal(StawkaVat.Zwolniona, stawki[2].Stawka);

        Assert.Equal(3800m, rejestr.Sprzedaz.RazemNetto);
        Assert.Equal(730m, rejestr.Sprzedaz.PodatekNalezny);
    }

    /// <summary>
    /// Zakup bez prawa do odliczenia liczy się do obrotu, ale nie do podatku.
    /// </summary>
    [Fact]
    public void ZakupBezOdliczeniaNiePomniejszaPodatku()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien, [],
        [
            Zakup("Z/1", new DateOnly(2026, 8, 10), 1000m, odliczany: true),
            Zakup("Z/2", new DateOnly(2026, 8, 11), 2000m, odliczany: false)
        ]);

        Assert.Equal(3000m, rejestr.Zakupy.RazemNetto);
        Assert.Equal(690m, rejestr.Zakupy.RazemVat);
        Assert.Equal(230m, rejestr.Zakupy.PodatekNaliczony);
    }

    [Fact]
    public void ZakupyDzielaSieNaSrodkiTrwaleIPozostale()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien, [],
        [
            Zakup("Z/1", new DateOnly(2026, 8, 10), 1000m),
            Zakup("Z/2", new DateOnly(2026, 8, 11), 5000m,
                  rodzaj: RodzajZakupu.SrodkiTrwale)
        ]);

        Assert.Equal(1000m, rejestr.Zakupy.NettoPozostale);
        Assert.Equal(230m, rejestr.Zakupy.VatPozostale);
        Assert.Equal(5000m, rejestr.Zakupy.NettoSrodkiTrwale);
        Assert.Equal(1150m, rejestr.Zakupy.VatSrodkiTrwale);
    }

    /// <summary>
    /// Zakup bez prawa do odliczenia nie wchodzi do podziału deklaracyjnego.
    /// </summary>
    /// <remarks>
    /// Wykazanie samej kwoty netto, bez podatku, dałoby zestawienie
    /// wewnętrznie sprzeczne - a to od razu rzuca się w oczy przy kontroli.
    /// </remarks>
    [Fact]
    public void PodzialDeklaracyjnyPomijaZakupyBezOdliczenia()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien, [],
        [
            Zakup("Z/1", new DateOnly(2026, 8, 10), 1000m),
            Zakup("Z/2", new DateOnly(2026, 8, 11), 300m, odliczany: false)
        ]);

        // Dokument zostaje w rejestrze wraz ze swoją kwotą...
        Assert.Equal(2, rejestr.Zakupy.Wpisy.Count);
        Assert.Equal(1300m, rejestr.Zakupy.RazemNetto);

        // ...ale do deklaracji trafia wyłącznie zakup dający odliczenie.
        Assert.Equal(1000m, rejestr.Zakupy.NettoPozostale);
        Assert.Equal(230m, rejestr.Zakupy.VatPozostale);
    }

    [Fact]
    public void RozliczenieWskazujeKwoteDoZaplaty()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien,
            [Sprzedaz("FV/1", new DateOnly(2026, 8, 5), 10000m)],
            [Zakup("Z/1", new DateOnly(2026, 8, 6), 4000m)]);

        RozliczenieOkresu rozliczenie = rejestr.Rozliczenie;

        Assert.Equal(2300m, rozliczenie.PodatekNalezny);
        Assert.Equal(920m, rozliczenie.PodatekNaliczony);
        Assert.Equal(1380m, rozliczenie.DoZaplaty);
        Assert.Equal(0m, rozliczenie.Nadwyzka);
    }

    [Fact]
    public void PrzewagaZakupowDajeNadwyzkeDoPrzeniesienia()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien,
            [Sprzedaz("FV/1", new DateOnly(2026, 8, 5), 1000m)],
            [Zakup("Z/1", new DateOnly(2026, 8, 6), 9000m)]);

        RozliczenieOkresu rozliczenie = rejestr.Rozliczenie;

        Assert.Equal(0m, rozliczenie.DoZaplaty);
        Assert.Equal(1840m, rozliczenie.Nadwyzka);
    }

    [Fact]
    public void PustyOkresDajeZeraZamiastBledu()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien, [], []);

        Assert.Empty(rejestr.Sprzedaz.Wpisy);
        Assert.Empty(rejestr.Sprzedaz.WedlugStawek);
        Assert.Equal(0m, rejestr.Rozliczenie.DoZaplaty);
        Assert.Equal(0m, rejestr.Rozliczenie.Nadwyzka);
    }

    /// <summary>
    /// Rejestr kwartalny obejmuje wszystkie trzy miesiące.
    /// </summary>
    [Fact]
    public void RejestrKwartalnyLaczyTrzyMiesiace()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(OkresRozliczeniowy.Kwartal(2026, 3),
        [
            Sprzedaz("FV/1", new DateOnly(2026, 7, 5), 100m),
            Sprzedaz("FV/2", new DateOnly(2026, 8, 5), 200m),
            Sprzedaz("FV/3", new DateOnly(2026, 9, 5), 300m),
            Sprzedaz("FV/4", new DateOnly(2026, 10, 5), 400m)
        ], []);

        Assert.Equal(3, rejestr.Sprzedaz.Wpisy.Count);
        Assert.Equal(600m, rejestr.Sprzedaz.RazemNetto);
    }

    [Fact]
    public void WpisySaUporzadkowaneWedlugDatyINumeru()
    {
        RejestrVat rejestr = RejestrVat.Zbuduj(Sierpien,
        [
            Sprzedaz("FV/3", new DateOnly(2026, 8, 20), 100m),
            Sprzedaz("FV/1", new DateOnly(2026, 8, 5), 100m),
            Sprzedaz("FV/2", new DateOnly(2026, 8, 5), 100m)
        ], []);

        Assert.Equal(["FV/1", "FV/2", "FV/3"], rejestr.Sprzedaz.Wpisy.Select(w => w.Numer));
    }

    // ------------------------------------------------------------ pomocnicze

    private static WpisSprzedazy Sprzedaz(string numer, DateOnly dataUjecia, decimal netto) =>
        new(numer, dataUjecia, dataUjecia, "Klient", "7010001453", null,
            [new KwotyWStawce(StawkaVat.Vat23, netto, StawkaVat.Vat23.PodatekOd(netto))]);

    private static WpisZakupu Zakup(string numer, DateOnly dataUjecia, decimal netto,
                                    bool odliczany = true,
                                    RodzajZakupu rodzaj = RodzajZakupu.TowaryIUslugi) =>
        new(numer, dataUjecia, dataUjecia, dataUjecia, "Dostawca", "1180000001",
            rodzaj, odliczany, netto, StawkaVat.Vat23.PodatekOd(netto));
}
