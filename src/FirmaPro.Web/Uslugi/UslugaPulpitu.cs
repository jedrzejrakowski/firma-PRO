using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Obrót jednego miesiąca - słupek na wykresie pulpitu.</summary>
public sealed record ObrotMiesiaca(int Rok, int Miesiac, decimal Sprzedaz)
{
    /// <summary>Trzyliterowy skrót nazwy miesiąca, np. „sty”.</summary>
    public string Skrot => new DateOnly(Rok, Miesiac, 1)
        .ToString("MMM", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"))
        .TrimEnd('.');
}

/// <summary>Sprawa, którą warto się dziś zająć.</summary>
/// <param name="Waga">„pilny”, „blisko” albo „spokojny” - decyduje o kolorze.</param>
public sealed record SprawaNaDzis(string Waga, string Tresc, string Szczegol, string Strona);

/// <summary>Wszystko, co widać na pulpicie.</summary>
public sealed record DanePulpitu(
    decimal DoZaplaty,
    decimal PoTerminie,
    int IlePoTerminie,
    decimal SprzedazMiesiaca,
    decimal KosztyMiesiaca,
    IReadOnlyList<ObrotMiesiaca> Miesiace,
    IReadOnlyList<FakturaSprzedazy> Ostatnie,
    IReadOnlyList<SprawaNaDzis> Sprawy)
{
    /// <summary>Najwyższy słupek - do wyskalowania wykresu.</summary>
    public decimal Szczyt => Miesiace.Count == 0 ? 0m : Miesiace.Max(m => m.Sprzedaz);
}

/// <summary>
/// Pulpit: co się dzieje w firmie i co wymaga reakcji.
/// </summary>
/// <remarks>
/// Program po zalogowaniu otwierał listę faktur, czyli odpowiadał na pytanie
/// „co wystawiliśmy”. Pulpit odpowiada na pytanie „jak nam idzie i czym się
/// zająć” - i to jest pierwsza rzecz, jakiej szuka właściciel firmy.
/// </remarks>
public sealed class UslugaPulpitu(
    FirmaProDbContext baza,
    UslugaPlatnosci uslugaPlatnosci,
    TimeProvider czas)
{
    /// <summary>Ile miesięcy pokazuje wykres.</summary>
    public const int IleMiesiecy = 6;

    private DateOnly Dzisiaj => DateOnly.FromDateTime(czas.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Liczba faktur po terminie zapłaty - znacznik przy pozycji menu.
    /// </summary>
    /// <remarks>
    /// Osobne, tanie zapytanie, a nie pełne wyliczenie należności: leci przy
    /// każdym otwarciu dowolnego ekranu, więc musi być zwykłym zliczeniem po
    /// indeksie.
    /// </remarks>
    public async Task<int> IlePoTerminieAsync(CancellationToken anulowanie = default)
    {
        DateOnly dzisiaj = Dzisiaj;

        return await baza.FakturySprzedazy
            .CountAsync(f => !f.Zaplacono
                          && f.TerminPlatnosci != null
                          && f.TerminPlatnosci < dzisiaj, anulowanie);
    }

    public async Task<DanePulpitu> ZbudujAsync(CancellationToken anulowanie = default)
    {
        DateOnly dzisiaj = Dzisiaj;
        var poczatekMiesiaca = new DateOnly(dzisiaj.Year, dzisiaj.Month, 1);
        DateOnly poczatekWykresu = poczatekMiesiaca.AddMonths(-(IleMiesiecy - 1));

        PodsumowanieNaleznosci naleznosci = await uslugaPlatnosci.NaleznosciAsync(anulowanie);

        decimal sprzedazMiesiaca = await baza.FakturySprzedazy
            .Where(f => f.DataWystawienia >= poczatekMiesiaca)
            .SumAsync(f => (decimal?)f.RazemNetto, anulowanie) ?? 0m;

        decimal kosztyMiesiaca = await baza.FakturyZakupu
            .Where(f => f.DataWystawienia >= poczatekMiesiaca)
            .SumAsync(f => (decimal?)f.RazemNetto, anulowanie) ?? 0m;

        // Grupowanie po roku i miesiącu robi baza; do pamięci wraca tyle
        // wierszy, ile słupków na wykresie.
        var obroty = await baza.FakturySprzedazy
            .Where(f => f.DataWystawienia >= poczatekWykresu)
            .GroupBy(f => new { f.DataWystawienia.Year, f.DataWystawienia.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Suma = g.Sum(f => f.RazemNetto) })
            .ToListAsync(anulowanie);

        List<ObrotMiesiaca> miesiace = [];

        for (int krok = 0; krok < IleMiesiecy; krok++)
        {
            DateOnly miesiac = poczatekWykresu.AddMonths(krok);

            // Miesiąc bez sprzedaży też musi mieć swój słupek - inaczej wykres
            // udawałby, że firma pracowała bez przerwy.
            decimal suma = obroty
                .FirstOrDefault(o => o.Year == miesiac.Year && o.Month == miesiac.Month)
                ?.Suma ?? 0m;

            miesiace.Add(new ObrotMiesiaca(miesiac.Year, miesiac.Month, Kwoty.Zaokraglij(suma)));
        }

        List<FakturaSprzedazy> ostatnie = await baza.FakturySprzedazy
            .OrderByDescending(f => f.DataWystawienia)
            .ThenByDescending(f => f.UtworzonoUtc)
            .Take(6)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        return new DanePulpitu(
            naleznosci.Razem,
            naleznosci.PoTerminie,
            naleznosci.IlePoTerminie,
            Kwoty.Zaokraglij(sprzedazMiesiaca),
            Kwoty.Zaokraglij(kosztyMiesiaca),
            miesiace,
            ostatnie,
            await SprawyAsync(naleznosci, anulowanie));
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>
    /// Lista spraw wymagających reakcji.
    /// </summary>
    /// <remarks>
    /// Kolejność jest tu istotniejsza od kompletności - na górze stoi to, co
    /// kosztuje pieniądze albo grozi karą, niżej zwykłe porządki. Pusta lista
    /// jest dobrą wiadomością i tak też wygląda na ekranie.
    /// </remarks>
    private async Task<IReadOnlyList<SprawaNaDzis>> SprawyAsync(
        PodsumowanieNaleznosci naleznosci, CancellationToken anulowanie)
    {
        List<SprawaNaDzis> sprawy = [];

        if (naleznosci.IlePoTerminie > 0)
        {
            sprawy.Add(new SprawaNaDzis(
                "pilny",
                $"{naleznosci.IlePoTerminie} faktur po terminie zapłaty",
                $"Razem {Kwoty.NaTekst(naleznosci.PoTerminie)} zł - wyślij przypomnienie",
                "/Naleznosci"));
        }

        int odrzucone = await baza.FakturySprzedazy
            .CountAsync(f => f.Status == StatusKsef.Odrzucona, anulowanie);

        if (odrzucone > 0)
        {
            sprawy.Add(new SprawaNaDzis(
                "pilny",
                $"{odrzucone} faktur odrzuconych przez KSeF",
                "Popraw dane i wyślij ponownie",
                "/Faktury/Index"));
        }

        int robocze = await baza.FakturySprzedazy
            .CountAsync(f => f.Status == StatusKsef.Robocza, anulowanie);

        if (robocze > 0)
        {
            sprawy.Add(new SprawaNaDzis(
                "blisko",
                $"{robocze} faktur czeka na wysyłkę do KSeF",
                "Faktura bez numeru KSeF nie jest jeszcze doręczona nabywcy",
                "/Faktury/Index"));
        }

        Firma firma = await baza.Firmy
            .AsNoTracking()
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        // Certyfikat wygasa po cichu - o jego końcu trzeba przypomnieć
        // zawczasu, bo dzień po terminie nie da się wystawić ani jednej faktury.
        if (firma.CertyfikatWaznyDo is DateTimeOffset koniec)
        {
            int dni = (koniec.UtcDateTime.Date - Dzisiaj.ToDateTime(TimeOnly.MinValue)).Days;

            if (dni <= 30)
            {
                sprawy.Add(new SprawaNaDzis(
                    dni <= 0 ? "pilny" : "blisko",
                    dni <= 0 ? "Certyfikat KSeF stracił ważność" : $"Certyfikat KSeF wygasa za {dni} dni",
                    "Wystaw nowy certyfikat w ustawieniach dostępu do KSeF",
                    "/Ustawienia"));
            }
        }

        if (sprawy.Count == 0)
        {
            sprawy.Add(new SprawaNaDzis(
                "spokojny",
                "Nic nie wymaga uwagi",
                "Wszystkie faktury są rozliczone i przyjęte przez KSeF",
                "/Faktury/Index"));
        }

        return sprawy;
    }
}
