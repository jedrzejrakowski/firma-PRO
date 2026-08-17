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
