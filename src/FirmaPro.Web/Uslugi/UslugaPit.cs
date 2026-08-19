using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Zaliczka okresu razem z tym, co program o niej wie.</summary>
/// <param name="Zaliczka">Rachunek zaliczki.</param>
/// <param name="Zapisana">Wiersz z bazy, jeśli zapłatę potwierdzono.</param>
/// <param name="SkladkiSpoleczne">Ile z odliczeń to składki społeczne.</param>
/// <param name="SkladkaZdrowotna">Ile z odliczeń to składka zdrowotna.</param>
/// <param name="Strata">Ile z odliczeń to strata z lat ubiegłych.</param>
public sealed record OkresPit(
    ZaliczkaPit Zaliczka,
    ZaliczkaPitFirmy? Zapisana,
    decimal SkladkiSpoleczne,
    decimal SkladkaZdrowotna,
    decimal Strata)
{
    /// <summary>Czy zapłatę potwierdzono w programie.</summary>
    public bool Zaplacona => Zapisana is not null;

    /// <summary>Kwota faktycznie zapłacona; zero, dopóki nie potwierdzono.</summary>
    public long Zaplacono => Zapisana?.Kwota ?? 0L;

    /// <summary>Czy termin minął, a zapłaty nie potwierdzono.</summary>
    public bool PoTerminie(DateOnly dzis) =>
        !Zaplacona && Zaliczka.CosDoZaplaty && Zaliczka.Termin < dzis;

    /// <summary>Ile dni zostało do terminu; ujemne, gdy termin minął.</summary>
    public int DniDoTerminu(DateOnly dzis) => Zaliczka.Termin.DayNumber - dzis.DayNumber;
}

/// <summary>
/// Zaliczki na podatek dochodowy.
/// </summary>
/// <remarks>
/// <para>
/// Usługa spina wszystko, co program policzył wcześniej: dochód z księgi
/// (z zakupem towarów i remanentami), składki z ZUS-u i stratę z lat
/// ubiegłych. Sam rachunek podatku siedzi w dziedzinie - tutaj jest tylko
/// zbieranie składników i pilnowanie, żeby żaden nie wszedł dwa razy.
/// </para>
/// <para>
/// Od podatku narastająco odejmujemy zaliczki <b>zapłacone</b>, a nie
/// naliczone. Kwota naliczona zmienia się przy każdej dopisanej fakturze;
/// zapłacona już nie. Gdyby program odejmował naliczone, korekta faktury
/// sprzed pół roku po cichu przeliczyłaby wszystkie późniejsze zaliczki.
/// </para>
/// </remarks>
public sealed class UslugaPit(FirmaProDbContext baza, UslugaKsiegi ksiega, UslugaZus zus)
{
    /// <summary>Zaliczki za wszystkie okresy roku.</summary>
    public async Task<List<OkresPit>> RokAsync(int rok, CancellationToken anulowanie = default)
    {
        Firma firma = await FirmaAsync(anulowanie);

        if (firma.FormaOpodatkowania == FormaOpodatkowania.KsiegiRachunkowe)
        {
            return [];
        }

        SkalaPodatkowa? skala = SkalaPodatkowa.Dla(rok);

        if (firma.FormaOpodatkowania == FormaOpodatkowania.Skala && skala is null)
        {
            return [];
        }

        bool kwartalnie = firma.ZaliczkiKwartalne;
        int ile = kwartalnie ? 4 : 12;

        List<ZaliczkaPitFirmy> zapisane = await baza.ZaliczkiPit
            .AsNoTracking()
            .Where(z => z.Rok == rok && z.Kwartalna == kwartalnie)
            .ToListAsync(anulowanie);

        // Składki odliczają się w kwocie zapłaconej w roku, bez podziału
        // na okresy - dlatego liczymy je raz, narastająco do końca okresu.
        List<OkresPit> okresy = [];

        long zaplaconeWczesniej = 0L;

        for (int numer = 1; numer <= ile; numer++)
        {
            OkresRozliczeniowy okres = kwartalnie
                ? OkresRozliczeniowy.Kwartal(rok, numer)
                : OkresRozliczeniowy.Miesiac(rok, numer);

            OkresPit wynik = await ZaliczkaAsync(
                firma, skala, okres, zaplaconeWczesniej, anulowanie);

            ZaliczkaPitFirmy? zapis = zapisane.FirstOrDefault(z => z.Numer == numer);

            okresy.Add(wynik with { Zapisana = zapis });

            zaplaconeWczesniej += zapis?.Kwota ?? 0L;
        }

        return okresy;
    }

    /// <summary>Zaliczka za jeden okres.</summary>
    public async Task<OkresPit> ZaliczkaAsync(Firma firma,
                                              SkalaPodatkowa? skala,
                                              OkresRozliczeniowy okres,
                                              long zaplaconeWczesniej,
                                              CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(firma);
        ArgumentNullException.ThrowIfNull(okres);

        int rok = okres.PierwszyDzien.Year;

        decimal spoleczne = await zus.SpoleczneDoOdliczeniaAsync(rok, anulowanie);

        decimal zdrowotna = StawkiZus.Dla(rok) is { } stawki
            ? Zus.ZdrowotnaDoOdliczenia(firma.FormaOpodatkowania,
                await zus.ZdrowotnaZaplaconaAsync(rok, anulowanie), stawki)
            : 0m;

        if (firma.FormaOpodatkowania == FormaOpodatkowania.Ryczalt)
        {
            EwidencjaRyczaltu ewidencja = await ksiega.RyczaltAsync(okres, anulowanie);

            // Przy ryczałcie strata z lat ubiegłych też odlicza się od przychodu.
            ZaliczkaPit zaliczka = PodatekDochodowy.ZaliczkaRyczalt(
                okres,
                ewidencja.NarastajacoWedlugStawek,
                spoleczne + zdrowotna + firma.StrataDoOdliczenia,
                zaplaconeWczesniej);

            return new OkresPit(zaliczka, null, spoleczne, zdrowotna, firma.StrataDoOdliczenia);
        }

        decimal dochod = await ksiega.DochodNarastajacoAsync(okres, anulowanie);

        // Przy podatku liniowym zdrowotna odlicza się od dochodu; przy skali
        // nie odlicza się wcale, więc ZdrowotnaDoOdliczenia zwraca tam zero.
        ZaliczkaPit wynik = PodatekDochodowy.Zaliczka(
            okres,
            dochod,
            spoleczne + zdrowotna + firma.StrataDoOdliczenia,
            firma.FormaOpodatkowania == FormaOpodatkowania.Skala ? skala : null,
            zaplaconeWczesniej);

        return new OkresPit(wynik, null, spoleczne, zdrowotna, firma.StrataDoOdliczenia);
    }

    /// <summary>Potwierdza zapłatę zaliczki za okres.</summary>
    /// <remarks>
    /// Zapisujemy kwotę podaną przez użytkownika, a nie naliczoną przez
    /// program. Zaliczka bywa zapłacona w innej wysokości - z zaokrągleniem,
    /// z dopłatą po korekcie albo według wyliczenia księgowej - i to kwota
    /// faktycznie przelana odejmuje się w okresach następnych.
    /// </remarks>
    public async Task ZaplacAsync(int rok, int numer, bool kwartalna,
                                  long kwota, DateOnly dzien,
                                  CancellationToken anulowanie = default)
    {
        ZaliczkaPitFirmy? wiersz = await baza.ZaliczkiPit
            .FirstOrDefaultAsync(
                z => z.Rok == rok && z.Numer == numer && z.Kwartalna == kwartalna,
                anulowanie);

        if (wiersz is null)
        {
            wiersz = new ZaliczkaPitFirmy
            {
                Rok = rok,
                Numer = numer,
                Kwartalna = kwartalna
            };

            baza.ZaliczkiPit.Add(wiersz);
        }

        wiersz.Kwota = Math.Max(0L, kwota);
        wiersz.DataZaplaty = dzien;

        await baza.SaveChangesAsync(anulowanie);
    }

    /// <summary>Cofa potwierdzenie zapłaty.</summary>
    public async Task CofnijAsync(int rok, int numer, bool kwartalna,
                                  CancellationToken anulowanie = default)
    {
        ZaliczkaPitFirmy? wiersz = await baza.ZaliczkiPit
            .FirstOrDefaultAsync(
                z => z.Rok == rok && z.Numer == numer && z.Kwartalna == kwartalna,
                anulowanie);

        if (wiersz is null)
        {
            return;
        }

        baza.ZaliczkiPit.Remove(wiersz);
        await baza.SaveChangesAsync(anulowanie);
    }

    /// <summary>
    /// Czy jakiś termin zaliczki jest pilny - do znacznika w menu.
    /// </summary>
    /// <remarks>
    /// Tanie pytanie: sam kalendarz i lista potwierdzonych okresów, bez
    /// liczenia podatku. Zadaje je każdy ekran programu.
    /// </remarks>
    public async Task<bool> PilnyTerminAsync(DateOnly dzis,
                                             CancellationToken anulowanie = default)
    {
        Firma firma = await FirmaAsync(anulowanie);

        if (firma.FormaOpodatkowania == FormaOpodatkowania.KsiegiRachunkowe)
        {
            return false;
        }

        bool kwartalnie = firma.ZaliczkiKwartalne;
        int ile = kwartalnie ? 4 : 12;

        DateOnly granica = dzis.AddDays(DniPrzypomnienia);

        List<int> zaplacone = await baza.ZaliczkiPit
            .AsNoTracking()
            .Where(z => z.Rok == dzis.Year && z.Kwartalna == kwartalnie)
            .Select(z => z.Numer)
            .ToListAsync(anulowanie);

        for (int numer = 1; numer <= ile; numer++)
        {
            if (zaplacone.Contains(numer))
            {
                continue;
            }

            OkresRozliczeniowy okres = kwartalnie
                ? OkresRozliczeniowy.Kwartal(dzis.Year, numer)
                : OkresRozliczeniowy.Miesiac(dzis.Year, numer);

            // Termin, który jeszcze nie nadszedł w tym roku, nie jest zaległy.
            if (okres.OstatniDzien >= dzis)
            {
                break;
            }

            if (PodatekDochodowy.Termin(okres) <= granica)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ile dni przed terminem program zaczyna o nim przypominać.</summary>
    public const int DniPrzypomnienia = 7;

    /// <summary>
    /// Firma, w której kontekście pracuje użytkownik.
    /// </summary>
    /// <remarks>
    /// Warunek po identyfikatorze jest konieczny: <c>Firma</c> nie jest encją
    /// firmową, więc nie obejmuje jej globalny filtr najemcy. Zapytanie bez
    /// tego warunku zwróciłoby w koncie biura rachunkowego dowolną z firm -
    /// i program liczyłby podatek według cudzych ustawień.
    /// </remarks>
    private async Task<Firma> FirmaAsync(CancellationToken anulowanie) =>
        await baza.Firmy.AsNoTracking()
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);
}
