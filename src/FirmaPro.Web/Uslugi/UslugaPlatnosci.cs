using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Niezapłacona faktura wraz z rozliczeniem.</summary>
public sealed record Naleznosc(FakturaSprzedazy Faktura, Rozliczenie Rozliczenie);

/// <summary>Podsumowanie należności firmy.</summary>
public sealed record PodsumowanieNaleznosci(
    IReadOnlyList<Naleznosc> Pozycje,
    decimal Razem,
    decimal PoTerminie)
{
    public int IlePoTerminie => Pozycje.Count(p => p.Rozliczenie.PoTerminie);
}

/// <summary>
/// Wpłaty do faktur i pilnowanie należności.
/// </summary>
/// <remarks>
/// Program pokazywał dotąd, co zostało wystawione, ale nie to, kto jest
/// firmie winien pieniądze - a to drugie decyduje o tym, czy firma ma z czego
/// żyć.
/// </remarks>
public sealed class UslugaPlatnosci(
    FirmaProDbContext baza,
    UslugaFaktur uslugaFaktur,
    INadawcaPoczty poczta,
    TimeProvider czas)
{
    public bool PocztaDziala => poczta.Dziala;

    private DateOnly Dzisiaj => DateOnly.FromDateTime(czas.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<Platnosc>> WplatyAsync(
        Guid fakturaId, CancellationToken anulowanie = default) =>
        await baza.Platnosci
            .Where(p => p.FakturaId == fakturaId)
            .OrderByDescending(p => p.Data)
            .ThenByDescending(p => p.UtworzonoUtc)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    public async Task<Rozliczenie> RozliczenieAsync(Guid fakturaId,
                                                    CancellationToken anulowanie = default)
    {
        FakturaSprzedazy faktura = await baza.FakturySprzedazy
            .AsNoTracking()
            .SingleAsync(f => f.Id == fakturaId, anulowanie);

        decimal zaplacono = await SumaWplatAsync(fakturaId, anulowanie);

        return new Rozliczenie(faktura.RazemBrutto, zaplacono, faktura.TerminPlatnosci, Dzisiaj);
    }

    /// <summary>Zapisuje wpłatę do faktury.</summary>
    public async Task<WynikKonta<Platnosc>> DodajWplateAsync(
        Guid fakturaId, decimal kwota, DateOnly data, string? uwagi,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        FakturaSprzedazy? faktura = await baza.FakturySprzedazy
            .FirstOrDefaultAsync(f => f.Id == fakturaId, anulowanie);

        if (faktura is null)
        {
            walidacja.Blad("Faktura", "nie znaleziono faktury");
            return new WynikKonta<Platnosc>(null, walidacja);
        }

        if (kwota <= 0)
        {
            walidacja.Blad("Kwota", "kwota wpłaty musi być większa od zera");
        }

        // Wpłata sprzed wystawienia faktury to prawie zawsze pomyłka w dacie.
        // Zaliczki wpłacone wcześniej dokumentuje się osobną fakturą.
        if (data < faktura.DataWystawienia)
        {
            walidacja.Blad("Data",
                $"wpłata nie może być wcześniejsza niż wystawienie faktury " +
                $"({faktura.DataWystawienia:yyyy-MM-dd})");
        }

        if (walidacja.SaBledy)
        {
            return new WynikKonta<Platnosc>(null, walidacja);
        }

        var wplata = new Platnosc
        {
            FakturaId = fakturaId,
            Kwota = Kwoty.Zaokraglij(kwota),
            Data = data,
            Uwagi = string.IsNullOrWhiteSpace(uwagi) ? null : uwagi.Trim()
        };

        baza.Platnosci.Add(wplata);
        await baza.SaveChangesAsync(anulowanie);

        await OdswiezStanFakturyAsync(faktura, anulowanie);

        return new WynikKonta<Platnosc>(wplata, walidacja);
    }

    public async Task<bool> UsunWplateAsync(Guid id, CancellationToken anulowanie = default)
    {
        Platnosc? wplata = await baza.Platnosci.FirstOrDefaultAsync(p => p.Id == id, anulowanie);

        if (wplata is null)
        {
            return false;
        }

        Guid fakturaId = wplata.FakturaId;

        baza.Platnosci.Remove(wplata);
        await baza.SaveChangesAsync(anulowanie);

        FakturaSprzedazy? faktura = await baza.FakturySprzedazy
            .FirstOrDefaultAsync(f => f.Id == fakturaId, anulowanie);

        if (faktura is not null)
        {
            await OdswiezStanFakturyAsync(faktura, anulowanie);
        }

        return true;
    }

    /// <summary>
    /// Faktury, za które firma jeszcze nie dostała pieniędzy.
    /// </summary>
    /// <remarks>
    /// Kolejność według terminu płatności, a nie daty wystawienia: pierwsze
    /// mają być te, po których pieniądze powinny już były wpłynąć.
    /// </remarks>
    public async Task<PodsumowanieNaleznosci> NaleznosciAsync(
        CancellationToken anulowanie = default)
    {
        List<FakturaSprzedazy> faktury = await baza.FakturySprzedazy
            .OrderBy(f => f.TerminPlatnosci ?? f.DataWystawienia)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        // Jedno zapytanie na wszystkie wpłaty zamiast jednego na fakturę:
        // przy kilkuset dokumentach różnica jest widoczna gołym okiem.
        Dictionary<Guid, decimal> wplaty = await baza.Platnosci
            .GroupBy(p => p.FakturaId)
            .Select(g => new { g.Key, Suma = g.Sum(p => p.Kwota) })
            .ToDictionaryAsync(x => x.Key, x => x.Suma, anulowanie);

        List<Naleznosc> pozycje = [];

        foreach (FakturaSprzedazy faktura in faktury)
        {
            var rozliczenie = new Rozliczenie(
                faktura.RazemBrutto,
                wplaty.GetValueOrDefault(faktura.Id),
                faktura.TerminPlatnosci,
                Dzisiaj);

            if (rozliczenie.Pozostalo > 0)
            {
                pozycje.Add(new Naleznosc(faktura, rozliczenie));
            }
        }

        return new PodsumowanieNaleznosci(
            pozycje,
            Kwoty.Zaokraglij(pozycje.Sum(p => p.Rozliczenie.Pozostalo)),
            Kwoty.Zaokraglij(pozycje.Where(p => p.Rozliczenie.PoTerminie)
                                    .Sum(p => p.Rozliczenie.Pozostalo)));
    }

    /// <summary>Wysyła kontrahentowi przypomnienie o niezapłaconej fakturze.</summary>
    public async Task<WynikKonta<WyslanieFaktury>> WyslijPrzypomnienieAsync(
        Guid fakturaId, string adres, Guid? ktoWysyla,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();
        string odbiorca = (adres ?? string.Empty).Trim();

        if (!poczta.Dziala)
        {
            walidacja.Blad("Poczta", "poczta nie jest skonfigurowana");
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        if (odbiorca.Length == 0 || !odbiorca.Contains('@', StringComparison.Ordinal))
        {
            walidacja.Blad("Adres", "podaj poprawny adres e-mail odbiorcy");
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        FakturaSprzedazy? faktura = await baza.FakturySprzedazy
            .FirstOrDefaultAsync(f => f.Id == fakturaId, anulowanie);

        if (faktura is null)
        {
            walidacja.Blad("Faktura", "nie znaleziono faktury");
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        Rozliczenie rozliczenie = await RozliczenieAsync(fakturaId, anulowanie);

        if (rozliczenie.Pozostalo <= 0)
        {
            walidacja.Blad("Rozliczenie",
                "ta faktura jest już zapłacona - przypomnienie byłoby pomyłką");
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == faktura.FirmaId, anulowanie);

        try
        {
            await poczta.WyslijAsync(
                odbiorca,
                $"Przypomnienie o płatności - faktura {faktura.Numer}",
                TrescPrzypomnienia(faktura, firma, rozliczenie),
                [new Zalacznik($"Faktura_{BezpiecznyNumer(faktura.Numer)}.pdf",
                    await uslugaFaktur.ZbudujPdfAsync(fakturaId, anulowanie),
                    "application/pdf")],
                anulowanie);
        }
        catch (BladPocztyException blad)
        {
            walidacja.Blad("Poczta", blad.Message);
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        var wyslanie = new WyslanieFaktury
        {
            FakturaId = fakturaId,
            Adres = odbiorca,
            UzytkownikId = ktoWysyla,
            WyslanoUtc = czas.GetUtcNow(),
            Rodzaj = RodzajWysylki.Przypomnienie
        };

        baza.WysylkiFaktur.Add(wyslanie);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<WyslanieFaktury>(wyslanie, walidacja);
    }

    // ------------------------------------------------------------ pomocnicze

    private async Task<decimal> SumaWplatAsync(Guid fakturaId, CancellationToken anulowanie) =>
        await baza.Platnosci
            .Where(p => p.FakturaId == fakturaId)
            .SumAsync(p => (decimal?)p.Kwota, anulowanie) ?? 0m;

    /// <summary>
    /// Ustawia przy fakturze podsumowanie zapłaty na podstawie wpłat.
    /// </summary>
    /// <remarks>
    /// Pola <c>Zaplacono</c> i <c>DataZaplaty</c> trafiają na wydruk, więc
    /// muszą zgadzać się z listą wpłat. Utrzymuje je program, a nie
    /// użytkownik - dwa miejsca do ręcznego wpisania tej samej prawdy prędzej
    /// czy później by się rozjechały.
    /// </remarks>
    private async Task OdswiezStanFakturyAsync(FakturaSprzedazy faktura,
                                               CancellationToken anulowanie)
    {
        decimal suma = await SumaWplatAsync(faktura.Id, anulowanie);
        bool zaplacona = suma >= faktura.RazemBrutto && faktura.RazemBrutto > 0;

        DateOnly? ostatnia = zaplacona
            ? await baza.Platnosci
                .Where(p => p.FakturaId == faktura.Id)
                .MaxAsync(p => (DateOnly?)p.Data, anulowanie)
            : null;

        if (faktura.Zaplacono == zaplacona && faktura.DataZaplaty == ostatnia)
        {
            return;
        }

        faktura.Zaplacono = zaplacona;
        faktura.DataZaplaty = ostatnia;

        await baza.SaveChangesAsync(anulowanie);
    }

    private static string BezpiecznyNumer(string numer) =>
        new(numer.Select(z => char.IsLetterOrDigit(z) || z is '-' or '_' ? z : '_').ToArray());

    private static string TrescPrzypomnienia(FakturaSprzedazy faktura, Firma firma,
                                             Rozliczenie rozliczenie)
    {
        var wiersze = new List<string>
        {
            "Dzień dobry,",
            string.Empty,
            rozliczenie.PoTerminie
                ? $"przypominamy o niezapłaconej fakturze - termin minął " +
                  $"{rozliczenie.DniPoTerminie} dni temu."
                : "przypominamy o zbliżającym się terminie płatności.",
            string.Empty,
            $"Faktura {faktura.Numer} z dnia {faktura.DataWystawienia:yyyy-MM-dd}",
            $"Kwota faktury: {Kwoty.NaTekst(rozliczenie.Brutto)} {faktura.Waluta}"
        };

        if (rozliczenie.Zaplacono > 0)
        {
            wiersze.Add($"Wpłacono dotychczas: {Kwoty.NaTekst(rozliczenie.Zaplacono)} {faktura.Waluta}");
        }

        wiersze.Add($"Pozostaje do zapłaty: {Kwoty.NaTekst(rozliczenie.Pozostalo)} {faktura.Waluta}");

        if (faktura.TerminPlatnosci is DateOnly termin)
        {
            wiersze.Add($"Termin płatności: {termin:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(firma.RachunekBankowy))
        {
            wiersze.Add($"Numer rachunku: {firma.RachunekBankowy}");
        }

        wiersze.AddRange([
            string.Empty,
            "Jeśli płatność została już wykonana, prosimy potraktować tę wiadomość",
            "jako niebyłą.",
            string.Empty,
            "Pozdrawiamy,",
            firma.Nazwa
        ]);

        return string.Join(Environment.NewLine, wiersze);
    }
}
