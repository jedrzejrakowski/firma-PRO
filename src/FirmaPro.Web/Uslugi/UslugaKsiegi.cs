using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Księga przychodów i rozchodów oraz ewidencja przychodów.
/// </summary>
/// <remarks>
/// <para>
/// Usługa zbiera zapisy z trzech źródeł: faktur sprzedaży, faktur zakupu
/// i zapisów wprowadzonych ręcznie. Reguły kwalifikacji siedzą tutaj, a nie
/// w modelu księgi - model ma liczyć sumy, a nie wiedzieć, skąd wzięły się
/// kwoty.
/// </para>
/// <para>
/// Najważniejsza rzecz, którą ta klasa robi, to <b>odsiewanie</b>. Do rejestru
/// VAT wchodzi każda faktura, bo z każdej liczy się podatek należny. Do księgi
/// wchodzi wyłącznie to, co jest przychodem albo kosztem w podatku dochodowym -
/// a to inny zbiór, i pomyłka w tym miejscu zawyża albo zaniża dochód.
/// </para>
/// </remarks>
public sealed class UslugaKsiegi(FirmaProDbContext baza)
{
    /// <summary>Buduje księgę przychodów i rozchodów za okres.</summary>
    public async Task<Kpir> KpirAsync(OkresRozliczeniowy okres,
                                      CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(okres);

        (DateOnly od, DateOnly doDnia) = ZakresRoku(okres);

        List<WpisKsiegi> wpisy = [];

        wpisy.AddRange(await PrzychodyAsync(od, doDnia, anulowanie));
        wpisy.AddRange(await KosztyAsync(od, doDnia, anulowanie));
        wpisy.AddRange(await ReczneAsync(od, doDnia, anulowanie));
        wpisy.AddRange(await OdpisyAsync(okres, anulowanie));
        wpisy.AddRange(await SkladkiAsync(od, doDnia, anulowanie));

        return Kpir.Zbuduj(okres, wpisy);
    }

    /// <summary>Buduje ewidencję przychodów za okres.</summary>
    public async Task<EwidencjaRyczaltu> RyczaltAsync(OkresRozliczeniowy okres,
                                                      CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(okres);

        (DateOnly od, DateOnly doDnia) = ZakresRoku(okres);

        Firma firma = await FirmaAsync(anulowanie);

        List<WpisRyczaltu> wpisy = [];

        foreach (FakturaSprzedazy faktura in await SprzedazAsync(od, doDnia, anulowanie))
        {
            wpisy.Add(new WpisRyczaltu(
                DataPrzychodu(faktura),
                faktura.Numer,
                "Sprzedaż towarów i usług",
                faktura.StawkaRyczaltu ?? firma.StawkaRyczaltu,
                PrzychodZFaktury(faktura)));
        }

        // Zapisy ręczne wchodzą do ewidencji tylko wtedy, gdy są przychodem -
        // ryczałt kosztów nie zna, więc wydatek nie ma tu czego robić.
        foreach (ZapisKsiegi zapis in await ZapisyAsync(od, doDnia, anulowanie))
        {
            if (!Kolumny.Przychod(zapis.Kolumna))
            {
                continue;
            }

            wpisy.Add(new WpisRyczaltu(
                zapis.Data,
                zapis.NumerDowodu,
                zapis.Opis,
                zapis.StawkaRyczaltu ?? firma.StawkaRyczaltu,
                zapis.Kwota));
        }

        return EwidencjaRyczaltu.Zbuduj(okres, wpisy);
    }

    /// <summary>
    /// Roczne rozliczenie dochodu z uwzględnieniem remanentów.
    /// </summary>
    /// <remarks>
    /// Dochód widoczny w miesiącach a dochód roczny to dwie różne liczby
    /// i nie jest to usterka: zakup towaru nie jest kosztem w chwili zakupu,
    /// kosztem jest towar sprzedany. Różnicę pokazują dopiero spisy z natury,
    /// dlatego rozliczenie liczy się raz, za cały rok.
    /// </remarks>
    public async Task<RozliczenieRoczne> RozliczenieRoczneAsync(
        int rok, CancellationToken anulowanie = default)
    {
        Kpir ksiega = await KpirAsync(OkresRozliczeniowy.Miesiac(rok, 12), anulowanie);

        List<SpisZNatury> spisy = await SpisyAsync(anulowanie);

        return RozliczenieRoczne.Zbuduj(ksiega,
            Remanenty.Poczatkowy(spisy, rok),
            Remanenty.Koncowy(spisy, rok));
    }

    /// <summary>
    /// Dochód narastająco od 1 stycznia - podstawa zaliczki i składki zdrowotnej.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nie jest to różnica kolumn 9 i 14 z księgi.</b> Kolumna 14 nie
    /// obejmuje zakupu towarów ani kosztów ubocznych, bo te rozlicza się przez
    /// spis z natury - wzięcie jej wprost zawyżyłoby dochód firmy handlowej
    /// o wartość całego zakupionego towaru i kazało płacić zaliczkę od pieniędzy,
    /// których nie ma.
    /// </para>
    /// <para>
    /// Remanent początkowy wchodzi zawsze, gdy jest. Remanent śródroczny -
    /// gdy firma sporządziła spis w trakcie roku; nie ma takiego obowiązku,
    /// ale kto go zrobi, ten liczy zaliczkę od dochodu bliższego prawdy.
    /// </para>
    /// </remarks>
    public async Task<decimal> DochodNarastajacoAsync(OkresRozliczeniowy okres,
                                                      CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(okres);

        Kpir ksiega = await KpirAsync(okres, anulowanie);
        List<SpisZNatury> spisy = await SpisyAsync(anulowanie);

        int rok = okres.PierwszyDzien.Year;

        return RozliczenieRoczne.Zbuduj(ksiega,
            Remanenty.Poczatkowy(spisy, rok),
            Remanenty.DoDnia(spisy, rok, okres.OstatniDzien)).Dochod;
    }

    /// <summary>Wszystkie spisy z natury firmy, od najstarszego.</summary>
    public async Task<List<SpisZNatury>> SpisyAsync(CancellationToken anulowanie = default) =>
        [.. (await baza.Spisy
                .AsNoTracking()
                .Include(s => s.Pozycje)
                .OrderBy(s => s.Data)
                .ToListAsync(anulowanie))
            .Select(s => s.NaModel())];

    /// <summary>Forma opodatkowania firmy - po niej wybiera się księgę.</summary>
    public async Task<FormaOpodatkowania> FormaAsync(CancellationToken anulowanie = default) =>
        (await FirmaAsync(anulowanie)).FormaOpodatkowania;

    // ------------------------------------------------------------- przychody

    /// <summary>
    /// Przychody ze sprzedaży.
    /// </summary>
    /// <remarks>
    /// Kwoty netto - podatek należny nie jest przychodem (art. 14 ust. 1
    /// ustawy o PIT). Przy fakturze walutowej przeliczamy tym samym kursem,
    /// którym przeliczono ją do VAT: dwa różne kursy w jednym dokumencie
    /// byłyby nie do wytłumaczenia księgowej.
    /// </remarks>
    private async Task<List<WpisKsiegi>> PrzychodyAsync(
        DateOnly od, DateOnly doDnia, CancellationToken anulowanie)
    {
        List<WpisKsiegi> wpisy = [];

        foreach (FakturaSprzedazy faktura in await SprzedazAsync(od, doDnia, anulowanie))
        {
            wpisy.Add(new WpisKsiegi(
                DataPrzychodu(faktura),
                faktura.Numer,
                faktura.NabywcaNazwa,
                faktura.NabywcaAdresLinia1,
                OpisSprzedazy(faktura),
                KolumnaKpir.SprzedazTowarowIUslug,
                PrzychodZFaktury(faktura),
                faktura.CzyKorekta ? "korekta" : null));
        }

        return wpisy;
    }

    /// <summary>
    /// Data, pod którą przychód wchodzi do księgi.
    /// </summary>
    /// <remarks>
    /// Data wystawienia faktury. Art. 14 ust. 1c ustawy o PIT każe brać dzień
    /// wydania rzeczy albo wykonania usługi, ale nie później niż dzień
    /// wystawienia faktury - a przy fakturze wystawionej po dostawie to właśnie
    /// wystawienie jest tym późniejszym zdarzeniem. Data ujęcia w VAT nie
    /// nadaje się do tego celu: rządzą nią inne przepisy.
    /// </remarks>
    private static DateOnly DataPrzychodu(FakturaSprzedazy faktura) =>
        faktura.DataWystawienia;

    /// <summary>Kwota przychodu z faktury, w złotych.</summary>
    private static decimal PrzychodZFaktury(FakturaSprzedazy faktura) =>
        faktura.Walutowa ? faktura.NettoWZlotych : faktura.RazemNetto;

    private static string OpisSprzedazy(FakturaSprzedazy faktura) => faktura.Rodzaj switch
    {
        RodzajFaktury.Korygujaca => "Korekta sprzedaży",
        RodzajFaktury.KorektaZaliczkowej => "Korekta faktury zaliczkowej",
        RodzajFaktury.KorektaRozliczeniowej => "Korekta faktury końcowej",
        RodzajFaktury.Zaliczkowa => "Zaliczka na poczet dostawy",
        _ => "Sprzedaż towarów i usług"
    };

    // ---------------------------------------------------------------- koszty

    /// <summary>
    /// Koszty z faktur zakupu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dwa odsiewy, oba istotne. Pierwszy: wydatek oznaczony jako niebędący
    /// kosztem podatkowym w ogóle do księgi nie wchodzi - reprezentacja
    /// i zakupy prywatne nie są kosztem, choć bywają w rejestrze VAT.
    /// </para>
    /// <para>
    /// Drugi: <b>środki trwałe</b>. Zakup środka trwałego jest wydatkiem, ale
    /// nie kosztem miesiąca zakupu - rozlicza się go odpisami amortyzacyjnymi
    /// (art. 22 ust. 8 ustawy o PIT). Wrzucenie go do kolumny 13 zaniżyłoby
    /// dochód o całą wartość zakupu, a to najczęstszy błąd przy prowadzeniu
    /// księgi samodzielnie. Odpisy wprowadza się jako zapisy ręczne.
    /// </para>
    /// </remarks>
    private async Task<List<WpisKsiegi>> KosztyAsync(
        DateOnly od, DateOnly doDnia, CancellationToken anulowanie)
    {
        List<FakturaZakupu> zakupy = await baza.FakturyZakupu
            .AsNoTracking()
            .Where(f => f.DataWystawienia >= od && f.DataWystawienia <= doDnia)
            .ToListAsync(anulowanie);

        List<WpisKsiegi> wpisy = [];

        foreach (FakturaZakupu zakup in zakupy)
        {
            if (!zakup.KosztPodatkowy || zakup.Rodzaj == RodzajZakupu.SrodkiTrwale)
            {
                continue;
            }

            wpisy.Add(new WpisKsiegi(
                zakup.DataWystawienia,
                zakup.Numer,
                zakup.SprzedawcaNazwa,
                null,
                Kolumny.Nazwa(zakup.KolumnaKpir),
                zakup.KolumnaKpir,
                zakup.RazemNetto,
                zakup.Odliczany ? null : "VAT bez odliczenia"));
        }

        return wpisy;
    }

    // ------------------------------------------------------------- amortyzacja

    /// <summary>
    /// Odpisy amortyzacyjne od początku roku do końca okresu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zakup środka trwałego świadomie nie wchodzi do księgi jako koszt -
    /// kosztem są właśnie te odpisy (art. 22 ust. 8 ustawy o PIT). To domyka
    /// obieg: faktura za samochód wypada z kosztów, a co miesiąc wchodzi
    /// przypadająca na niego rata.
    /// </para>
    /// <para>
    /// Do księgi trafia <b>kwota kosztowa</b>, nie pełny odpis. Przy samochodzie
    /// droższym od limitu część odpisu nie jest kosztem nigdy
    /// (art. 23 ust. 1 pkt 4), więc wpisanie pełnej raty zaniżałoby dochód.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Zapłacone składki ZUS jako koszt księgi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Do kolumny 13 wchodzi Fundusz Pracy zawsze, a składki społeczne tylko
    /// wtedy, gdy podatnik wybrał ujęcie ich w kosztach zamiast odliczenia
    /// od dochodu - ta sama składka nie może być jednym i drugim naraz.
    /// </para>
    /// <para>
    /// Składka zdrowotna nie jest kosztem <b>nigdy</b>. Przy liniowym
    /// i ryczałcie odlicza się ją poza księgą, przy skali nie odlicza wcale.
    /// </para>
    /// <para>
    /// Liczy się data zapłaty, nie miesiąc, za który składka jest należna
    /// (art. 26 ust. 1 pkt 2 ustawy o PIT mówi o składkach zapłaconych) -
    /// dlatego składka za grudzień trafia zwykle do księgi w styczniu.
    /// </para>
    /// </remarks>
    private async Task<List<WpisKsiegi>> SkladkiAsync(
        DateOnly od, DateOnly doDnia, CancellationToken anulowanie)
    {
        List<SkladkaZusFirmy> zaplacone = await baza.SkladkiZus
            .AsNoTracking()
            .Where(s => s.DataZaplaty != null
                        && s.DataZaplaty >= od && s.DataZaplaty <= doDnia)
            .OrderBy(s => s.DataZaplaty)
            .ToListAsync(anulowanie);

        List<WpisKsiegi> wpisy = [];

        foreach (SkladkaZusFirmy skladka in zaplacone)
        {
            decimal kwota = skladka.FunduszPracy
                          + (skladka.SpoleczneWKosztach ? skladka.Ubezpieczenia : 0m);

            if (kwota <= 0m)
            {
                continue;
            }

            wpisy.Add(new WpisKsiegi(
                skladka.DataZaplaty!.Value,
                $"ZUS/{skladka.Miesiac:00}/{skladka.Rok}",
                "Zakład Ubezpieczeń Społecznych",
                null,
                skladka.SpoleczneWKosztach
                    ? $"Składki społeczne i Fundusz Pracy za {skladka.Miesiac:00}/{skladka.Rok}"
                    : $"Fundusz Pracy za {skladka.Miesiac:00}/{skladka.Rok}",
                KolumnaKpir.PozostaleWydatki,
                Kwoty.Zaokraglij(kwota)));
        }

        return wpisy;
    }

    private async Task<List<WpisKsiegi>> OdpisyAsync(OkresRozliczeniowy okres,
                                                     CancellationToken anulowanie)
    {
        List<SrodekTrwalyFirmy> srodki = await baza.SrodkiTrwale
            .AsNoTracking()
            .ToListAsync(anulowanie);

        var wpisy = new List<WpisKsiegi>();

        // Liczymy od stycznia, bo księga potrzebuje sum narastających.
        var poczatekRoku = new DateOnly(okres.PierwszyDzien.Year, 1, 1);

        foreach (SrodekTrwalyFirmy srodek in srodki)
        {
            SrodekTrwaly model = srodek.NaModel();

            foreach (Odpis odpis in PlanAmortyzacji.Zbuduj(model))
            {
                DateOnly dzien = odpis.Okres.OstatniDzien;

                if (dzien < poczatekRoku || dzien > okres.OstatniDzien)
                {
                    continue;
                }

                if (odpis.Koszt == 0)
                {
                    continue;
                }

                wpisy.Add(new WpisKsiegi(
                    dzien,
                    srodek.NumerInwentarzowy,
                    string.Empty,
                    null,
                    "Odpis amortyzacyjny — " + srodek.Nazwa,
                    KolumnaKpir.PozostaleWydatki,
                    odpis.Koszt,
                    model.PrzekraczaLimit
                        ? "odpis " + Kwoty.NaTekst(odpis.Kwota)
                          + " zł, koszt ograniczony limitem"
                        : null));
            }
        }

        return wpisy;
    }

    // -------------------------------------------------------- zapisy ręczne

    private async Task<List<WpisKsiegi>> ReczneAsync(
        DateOnly od, DateOnly doDnia, CancellationToken anulowanie) =>
        [.. (await ZapisyAsync(od, doDnia, anulowanie))
            .Select(z => new WpisKsiegi(
                z.Data,
                z.NumerDowodu,
                z.Kontrahent ?? string.Empty,
                z.Adres,
                z.Opis,
                z.Kolumna,
                z.Kwota,
                z.Uwagi))];

    private Task<List<ZapisKsiegi>> ZapisyAsync(
        DateOnly od, DateOnly doDnia, CancellationToken anulowanie) =>
        baza.ZapisyKsiegi
            .AsNoTracking()
            .Where(z => z.Data >= od && z.Data <= doDnia)
            .ToListAsync(anulowanie);

    // ------------------------------------------------------------ pomocnicze

    /// <summary>
    /// Zakres dat potrzebny do zbudowania księgi.
    /// </summary>
    /// <remarks>
    /// Od początku roku, a nie od początku okresu: sumy narastające są
    /// potrzebne do zaliczki na podatek dochodowy, więc wcześniejsze miesiące
    /// muszą się w zapytaniu znaleźć.
    /// </remarks>
    private static (DateOnly Od, DateOnly Do) ZakresRoku(OkresRozliczeniowy okres) =>
        (new DateOnly(okres.PierwszyDzien.Year, 1, 1), okres.OstatniDzien);

    private Task<List<FakturaSprzedazy>> SprzedazAsync(
        DateOnly od, DateOnly doDnia, CancellationToken anulowanie) =>
        baza.FakturySprzedazy
            .AsNoTracking()
            .Where(f => f.DataWystawienia >= od && f.DataWystawienia <= doDnia)
            .ToListAsync(anulowanie);

    private Task<Firma> FirmaAsync(CancellationToken anulowanie) =>
        baza.Firmy.AsNoTracking().SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);
}
