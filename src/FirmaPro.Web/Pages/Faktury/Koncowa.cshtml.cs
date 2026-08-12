using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>Faktura zaliczkowa do wyboru na formularzu faktury końcowej.</summary>
public sealed record ZaliczkaDoRozliczenia(
    Guid Id, string Numer, DateOnly DataWystawienia, decimal Brutto, bool JuzRozliczona);

/// <summary>
/// Wystawianie faktury końcowej rozliczającej zaliczki.
/// </summary>
/// <remarks>
/// Formularz wychodzi od faktury zaliczkowej, bo to ona wie, czego dotyczy
/// całe zamówienie - jej pozycje podpowiadamy jako pozycje dostawy. Zaliczki
/// tego samego nabywcy można rozliczyć kilka naraz (art. 106f ust. 3 ustawy).
/// </remarks>
public sealed class KoncowaModel(FirmaProDbContext baza, UslugaFaktur uslugaFaktur) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    [BindProperty] public Guid KontrahentId { get; set; }
    [BindProperty] public DateOnly DataWystawienia { get; set; }
    [BindProperty] public DateOnly? TerminPlatnosci { get; set; }
    [BindProperty] public FormaPlatnosci? FormaPlatnosci { get; set; }
    [BindProperty] public List<Guid> Zaliczki { get; set; } = [];
    [BindProperty] public List<WierszPozycji> Pozycje { get; set; } = [];

    public string NabywcaNazwa { get; private set; } = string.Empty;
    public IReadOnlyList<ZaliczkaDoRozliczenia> Dostepne { get; private set; } = [];
    public List<string> Bledy { get; } = [];

    /// <summary>Suma zaliczek zaznaczonych na formularzu.</summary>
    public decimal SumaZaznaczonych =>
        Dostepne.Where(z => Zaliczki.Contains(z.Id)).Sum(z => z.Brutto);

    public async Task<IActionResult> OnGetAsync(CancellationToken anulowanie)
    {
        FakturaSprzedazy? zaliczkowa = await WczytajZaliczkowaAsync(anulowanie);
        if (zaliczkowa is null)
        {
            return NotFound();
        }

        KontrahentId = zaliczkowa.KontrahentId;
        DataWystawienia = DateOnly.FromDateTime(DateTime.Today);
        FormaPlatnosci = zaliczkowa.FormaPlatnosci ?? Domena.FormaPlatnosci.Przelew;

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);
        TerminPlatnosci = DataWystawienia.AddDays(firma.DomyslnyTerminPlatnosciDni);

        // Pozycje dostawy podpowiadamy z zamówienia - faktura końcowa
        // obejmuje całość, a nie samą resztę do dopłaty.
        Pozycje = [.. zaliczkowa.PozycjeZamowienia
            .OrderBy(p => p.NrWiersza)
            .Select(p => new WierszPozycji
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                KodStawki = p.KodStawki,
                Gtu = p.Gtu
            })];

        await WczytajZaliczkiAsync(anulowanie);
        Zaliczki = [.. Dostepne.Where(z => !z.JuzRozliczona && z.Id == Id).Select(z => z.Id)];

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        FakturaSprzedazy? zaliczkowa = await WczytajZaliczkowaAsync(anulowanie);
        if (zaliczkowa is null)
        {
            return NotFound();
        }

        await WczytajZaliczkiAsync(anulowanie);

        List<WierszPozycji> wypelnione = Pozycje.Where(p => !p.CzyPusty).ToList();

        if (wypelnione.Count == 0)
        {
            Bledy.Add("Faktura końcowa musi mieć przynajmniej jedną pozycję z nazwą.");
            return Page();
        }

        WynikWystawienia wynik = await uslugaFaktur.WystawKoncowaAsync(
            KontrahentId,
            DataWystawienia,
            TerminPlatnosci,
            FormaPlatnosci,
            Zaliczki,
            [.. wypelnione.Select(p => (p.Nazwa, p.Jednostka, p.Ilosc, p.CenaNetto,
                                        p.KodStawki, p.Gtu))],
            anulowanie);

        if (!wynik.Udalo)
        {
            Bledy.AddRange(wynik.Walidacja.Problemy
                .Where(p => p.Poziom == PoziomProblemu.Blad)
                .Select(p => $"{p.Pole}: {p.Komunikat}"));

            return Page();
        }

        TempData["Komunikat"] =
            $"Wystawiono fakturę końcową {wynik.Faktura!.Numer}, rozliczając zaliczki " +
            $"na {Kwoty.NaTekst(SumaZaznaczonych)} {wynik.Faktura.Waluta}.";

        return RedirectToPage("Szczegoly", new { id = wynik.Faktura.Id });
    }

    private async Task<FakturaSprzedazy?> WczytajZaliczkowaAsync(CancellationToken anulowanie)
    {
        FakturaSprzedazy? zaliczkowa = await baza.FakturySprzedazy
            .Include(f => f.PozycjeZamowienia)
            .FirstOrDefaultAsync(
                f => f.Id == Id && f.Rodzaj == RodzajFaktury.Zaliczkowa, anulowanie);

        if (zaliczkowa is not null)
        {
            NabywcaNazwa = zaliczkowa.NabywcaNazwa;
            KontrahentId = zaliczkowa.KontrahentId;
        }

        return zaliczkowa;
    }

    private async Task WczytajZaliczkiAsync(CancellationToken anulowanie)
    {
        HashSet<Guid> rozliczone = (await baza.RozliczoneZaliczki
                .Select(z => z.ZaliczkowaId)
                .ToListAsync(anulowanie))
            .ToHashSet();

        Dostepne = await baza.FakturySprzedazy
            .Where(f => f.Rodzaj == RodzajFaktury.Zaliczkowa && f.KontrahentId == KontrahentId)
            .OrderBy(f => f.DataWystawienia)
            .Select(f => new ZaliczkaDoRozliczenia(
                f.Id, f.Numer, f.DataWystawienia, f.RazemBrutto, rozliczone.Contains(f.Id)))
            .ToListAsync(anulowanie);
    }
}
