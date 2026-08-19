using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Składka miesiąca razem z tym, co program o niej wie.</summary>
/// <param name="Okres">Miesiąc, za który należne są składki.</param>
/// <param name="Spoleczne">Naliczone składki społeczne.</param>
/// <param name="Zdrowotna">Naliczona składka zdrowotna.</param>
/// <param name="Termin">Termin zapłaty, przesunięty na dzień roboczy.</param>
/// <param name="Zapisana">Wiersz z bazy, jeśli składkę już potwierdzono.</param>
/// <param name="PodstawaZDochodu">
/// Kwota, od której policzono zdrowotną - dochód miesiąca poprzedniego albo
/// przychód narastająco przy ryczałcie. Pokazujemy ją, bo bez niej składka
/// zdrowotna jest liczbą, której nie da się sprawdzić.
/// </param>
public sealed record MiesiacZus(
    OkresRozliczeniowy Okres,
    SkladkiSpoleczne Spoleczne,
    SkladkaZdrowotna Zdrowotna,
    DateOnly Termin,
    SkladkaZusFirmy? Zapisana,
    decimal PodstawaZDochodu)
{
    /// <summary>Kwota jednego przelewu do ZUS.</summary>
    public decimal Razem => Kwoty.Zaokraglij(Spoleczne.Razem + Zdrowotna.Kwota);

    /// <summary>Czy zapłatę potwierdzono w programie.</summary>
    public bool Zaplacona => Zapisana?.Zaplacona == true;

    /// <summary>Czy termin minął, a zapłaty nie potwierdzono.</summary>
    public bool PoTerminie(DateOnly dzis) => !Zaplacona && Termin < dzis;

    /// <summary>Ile dni zostało do terminu; ujemne, gdy termin minął.</summary>
    public int DniDoTerminu(DateOnly dzis) => Termin.DayNumber - dzis.DayNumber;
}

/// <summary>
/// Składki ZUS przedsiębiorcy.
/// </summary>
/// <remarks>
/// <para>
/// Usługa łączy trzy rzeczy, które osobno nic nie znaczą: ustawienia firmy
/// (z jakiego tytułu płaci składki), kwoty roku ze <see cref="StawkiZus"/>
/// i dochód z księgi. Dopiero razem dają składkę - bo od 2022 roku zdrowotna
/// zależy od dochodu, a więc od tego, co program już policzył w księdze.
/// </para>
/// <para>
/// Program nie wysyła niczego do ZUS i nie zastępuje deklaracji. Liczy kwotę,
/// pilnuje terminu i wpisuje zapłacone składki do księgi.
/// </para>
/// </remarks>
public sealed class UslugaZus(FirmaProDbContext baza, UslugaKsiegi ksiega)
{
    /// <summary>
    /// Ustawienia ZUS firmy - zapisane albo domyślne.
    /// </summary>
    /// <remarks>
    /// Domyślnych <b>nie zapisujemy</b> do bazy. Samo otwarcie ekranu nie jest
    /// oświadczeniem, z jakiego tytułu firma płaci składki, a zapis przy
    /// odczycie sprawiłby, że spółka z o.o. dostawałaby ustawienia ZUS
    /// przedsiębiorcy tylko dlatego, że ktoś kliknął w menu.
    /// </remarks>
    public async Task<UstawieniaZusFirmy> UstawieniaAsync(CancellationToken anulowanie = default) =>
        await baza.UstawieniaZus.FirstOrDefaultAsync(anulowanie) ?? new UstawieniaZusFirmy();

    /// <summary>Czy firma ma zapisane zasady opłacania składek.</summary>
    public async Task<bool> MaUstawieniaAsync(CancellationToken anulowanie = default) =>
        await baza.UstawieniaZus.AnyAsync(anulowanie);

    /// <summary>Zapisane ustawienia do zmiany; tworzy wiersz przy pierwszym zapisie.</summary>
    public async Task<UstawieniaZusFirmy> UstawieniaDoZapisuAsync(
        CancellationToken anulowanie = default)
    {
        UstawieniaZusFirmy? zapisane = await baza.UstawieniaZus
            .FirstOrDefaultAsync(anulowanie);

        if (zapisane is not null)
        {
            return zapisane;
        }

        var nowe = new UstawieniaZusFirmy();
        baza.UstawieniaZus.Add(nowe);

        return nowe;
    }

    /// <summary>
    /// Składki za wszystkie miesiące roku.
    /// </summary>
    /// <remarks>
    /// Liczymy cały rok naraz, bo zdrowotna ryczałtowca zależy od przychodu
    /// narastająco - próg przekroczony w listopadzie zmienia składkę także
    /// za miesiące wcześniejsze, a tego nie widać, patrząc na jeden miesiąc.
    /// </remarks>
    public async Task<List<MiesiacZus>> RokAsync(int rok, CancellationToken anulowanie = default)
    {
        StawkiZus? stawki = StawkiZus.Dla(rok);

        if (stawki is null)
        {
            return [];
        }

        UstawieniaZusFirmy ustawienia = await UstawieniaAsync(anulowanie);
        FormaOpodatkowania forma = await ksiega.FormaAsync(anulowanie);

        List<SkladkaZusFirmy> zapisane = await baza.SkladkiZus
            .AsNoTracking()
            .Where(s => s.Rok == rok)
            .ToListAsync(anulowanie);

        List<MiesiacZus> miesiace = [];

        for (int miesiac = 1; miesiac <= 12; miesiac++)
        {
            var okres = OkresRozliczeniowy.Miesiac(rok, miesiac);

            decimal podstawa = await PodstawaZdrowotnejAsync(forma, okres, anulowanie);

            miesiace.Add(new MiesiacZus(
                okres,
                Zus.Spoleczne(ustawienia.NaModel(stawki, miesiac), stawki, miesiac),
                Zus.Zdrowotna(forma, podstawa, stawki),
                Zus.Termin(okres),
                zapisane.FirstOrDefault(s => s.Miesiac == miesiac),
                podstawa));
        }

        return miesiace;
    }

    /// <summary>
    /// Kwota, od której liczy się składkę zdrowotną za dany miesiąc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Przy skali i liniowym podstawą jest dochód <b>miesiąca poprzedniego</b>
    /// (art. 81 ust. 2 ustawy o świadczeniach opieki zdrowotnej) - składkę
    /// za luty liczy się od dochodu stycznia. Bierzemy różnicę sum
    /// narastających, bo tak właśnie ustala ją przepis.
    /// </para>
    /// <para>
    /// Przy ryczałcie podstawą nie jest dochód, tylko próg przychodu
    /// narastająco od początku roku.
    /// </para>
    /// </remarks>
    public async Task<decimal> PodstawaZdrowotnejAsync(FormaOpodatkowania forma,
                                                       OkresRozliczeniowy okres,
                                                       CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(okres);

        if (forma == FormaOpodatkowania.Ryczalt)
        {
            EwidencjaRyczaltu ewidencja = await ksiega.RyczaltAsync(okres, anulowanie);
            return ewidencja.PrzychodNarastajaco;
        }

        if (forma == FormaOpodatkowania.KsiegiRachunkowe)
        {
            return 0m;
        }

        OkresRozliczeniowy poprzedni = okres.Poprzedni;

        // Styczeń liczy się od dochodu grudnia, ale grudzień należy do roku
        // poprzedniego - księga tamtego roku bywa jeszcze niedomknięta, więc
        // bierzemy najniższą podstawę zamiast zgadywać.
        if (poprzedni.Rok != okres.Rok)
        {
            return 0m;
        }

        // Dochód samego miesiąca poprzedniego to różnica sum narastających.
        // Obie liczone tak, jak liczy się je do podatku - z zakupem towarów
        // i remanentami, a nie samą różnicą kolumn 9 i 14.
        decimal doPoprzedniego = await ksiega.DochodNarastajacoAsync(poprzedni, anulowanie);

        return doPoprzedniego - await DochodDoMiesiacaAsync(poprzedni, anulowanie);
    }

    /// <summary>Potwierdza zapłatę składek za miesiąc.</summary>
    /// <remarks>
    /// Kosztem i odliczeniem jest składka <b>zapłacona</b>, nie naliczona
    /// (art. 26 ust. 1 pkt 2 ustawy o PIT), dlatego do księgi trafia dopiero
    /// to, co użytkownik tu potwierdzi.
    /// </remarks>
    public async Task ZaplacAsync(int rok, int miesiac, DateOnly dzien,
                                  CancellationToken anulowanie = default)
    {
        StawkiZus? stawki = StawkiZus.Dla(rok)
            ?? throw new InvalidOperationException(
                $"Program nie zna kwot ZUS na rok {rok}.");

        UstawieniaZusFirmy ustawienia = await UstawieniaAsync(anulowanie);
        FormaOpodatkowania forma = await ksiega.FormaAsync(anulowanie);

        var okres = OkresRozliczeniowy.Miesiac(rok, miesiac);

        SkladkiSpoleczne spoleczne =
            Zus.Spoleczne(ustawienia.NaModel(stawki, miesiac), stawki, miesiac);

        SkladkaZdrowotna zdrowotna = Zus.Zdrowotna(
            forma, await PodstawaZdrowotnejAsync(forma, okres, anulowanie), stawki);

        SkladkaZusFirmy? wiersz = await baza.SkladkiZus
            .FirstOrDefaultAsync(s => s.Rok == rok && s.Miesiac == miesiac, anulowanie);

        if (wiersz is null)
        {
            wiersz = new SkladkaZusFirmy { Rok = rok, Miesiac = miesiac };
            baza.SkladkiZus.Add(wiersz);
        }

        wiersz.Emerytalna = spoleczne.Emerytalna;
        wiersz.Rentowa = spoleczne.Rentowa;
        wiersz.Chorobowe = spoleczne.Chorobowe;
        wiersz.Wypadkowa = spoleczne.Wypadkowa;
        wiersz.FunduszPracy = spoleczne.FunduszPracy;
        wiersz.Zdrowotna = zdrowotna.Kwota;
        wiersz.PodstawaSpolecznych = spoleczne.Podstawa;
        wiersz.PodstawaZdrowotnej = zdrowotna.Podstawa;
        wiersz.DataZaplaty = dzien;
        wiersz.SpoleczneWKosztach = ustawienia.SpoleczneWKosztach;

        await baza.SaveChangesAsync(anulowanie);
    }

    /// <summary>Cofa potwierdzenie zapłaty.</summary>
    public async Task CofnijZaplateAsync(int rok, int miesiac,
                                         CancellationToken anulowanie = default)
    {
        SkladkaZusFirmy? wiersz = await baza.SkladkiZus
            .FirstOrDefaultAsync(s => s.Rok == rok && s.Miesiac == miesiac, anulowanie);

        if (wiersz is null)
        {
            return;
        }

        baza.SkladkiZus.Remove(wiersz);
        await baza.SaveChangesAsync(anulowanie);
    }

    /// <summary>
    /// Ile składek społecznych odlicza się od dochodu w roku.
    /// </summary>
    /// <remarks>
    /// Odliczeniu podlegają składki zapłacone, których nie zaliczono
    /// do kosztów - ta sama składka nie może być jednocześnie jednym i drugim.
    /// </remarks>
    public async Task<decimal> SpoleczneDoOdliczeniaAsync(
        int rok, CancellationToken anulowanie = default)
    {
        List<SkladkaZusFirmy> zaplacone = await baza.SkladkiZus
            .AsNoTracking()
            .Where(s => s.Rok == rok && s.DataZaplaty != null && !s.SpoleczneWKosztach)
            .ToListAsync(anulowanie);

        return Kwoty.Zaokraglij(zaplacone.Sum(s => s.Ubezpieczenia));
    }

    /// <summary>Suma zdrowotnej zapłaconej w roku.</summary>
    public async Task<decimal> ZdrowotnaZaplaconaAsync(
        int rok, CancellationToken anulowanie = default)
    {
        List<decimal> kwoty = await baza.SkladkiZus
            .AsNoTracking()
            .Where(s => s.Rok == rok && s.DataZaplaty != null)
            .Select(s => s.Zdrowotna)
            .ToListAsync(anulowanie);

        return Kwoty.Zaokraglij(kwoty.Sum());
    }

    /// <summary>
    /// Czy jakiś termin składkowy jest pilny - do znacznika w menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pytanie zadaje każdy ekran programu, więc odpowiedź musi być tania:
    /// patrzymy wyłącznie na kalendarz i na to, które miesiące potwierdzono
    /// jako zapłacone. Liczenie tu składek oznaczałoby budowanie księgi za
    /// dwanaście miesięcy przy każdym kliknięciu.
    /// </para>
    /// <para>
    /// Firma bez zapisanych zasad opłacania składek znacznika nie dostaje.
    /// Spółka, która składek właściciela nie płaci, nie ma powodu widzieć
    /// ostrzeżenia, którego nie da się usunąć.
    /// </para>
    /// </remarks>
    public async Task<bool> PilnyTerminAsync(DateOnly dzis,
                                             CancellationToken anulowanie = default)
    {
        if (!await MaUstawieniaAsync(anulowanie))
        {
            return false;
        }

        DateOnly granica = dzis.AddDays(DniPrzypomnienia);

        List<int> zaplacone = await baza.SkladkiZus
            .AsNoTracking()
            .Where(s => s.Rok == dzis.Year && s.DataZaplaty != null)
            .Select(s => s.Miesiac)
            .ToListAsync(anulowanie);

        for (int miesiac = 1; miesiac <= 12; miesiac++)
        {
            if (zaplacone.Contains(miesiac))
            {
                continue;
            }

            if (Zus.Termin(OkresRozliczeniowy.Miesiac(dzis.Year, miesiac)) <= granica)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ile dni przed terminem program zaczyna o nim przypominać.</summary>
    public const int DniPrzypomnienia = 7;

    /// <summary>
    /// Roczne rozliczenie składki zdrowotnej.
    /// </summary>
    /// <remarks>
    /// Składki płaci się co miesiąc od dochodu miesięcznego, a należne są
    /// od rocznego - różnicę dopłaca się albo odbiera do 20 maja.
    /// </remarks>
    public async Task<RozliczenieZdrowotnej?> RozliczenieZdrowotnejAsync(
        int rok, CancellationToken anulowanie = default)
    {
        StawkiZus? stawki = StawkiZus.Dla(rok);

        if (stawki is null)
        {
            return null;
        }

        FormaOpodatkowania forma = await ksiega.FormaAsync(anulowanie);

        var grudzien = OkresRozliczeniowy.Miesiac(rok, 12);

        decimal podstawaRoczna;

        if (forma == FormaOpodatkowania.Ryczalt)
        {
            EwidencjaRyczaltu ewidencja = await ksiega.RyczaltAsync(grudzien, anulowanie);

            podstawaRoczna = Zus.PodstawaZdrowotnejRyczalt(
                ewidencja.PrzychodNarastajaco, stawki) * 12m;
        }
        else
        {
            Kpir ksiegaRoku = await ksiega.KpirAsync(grudzien, anulowanie);
            podstawaRoczna = ksiegaRoku.DochodNarastajaco;
        }

        return Zus.RozliczRoczna(forma, podstawaRoczna,
            await ZdrowotnaZaplaconaAsync(rok, anulowanie), stawki);
    }

    /// <summary>Dochód narastająco do końca miesiąca poprzedzającego wskazany.</summary>
    private async Task<decimal> DochodDoMiesiacaAsync(OkresRozliczeniowy okres,
                                                      CancellationToken anulowanie)
    {
        if (okres.Numer == 1)
        {
            return 0m;
        }

        return await ksiega.DochodNarastajacoAsync(okres.Poprzedni, anulowanie);
    }
}
