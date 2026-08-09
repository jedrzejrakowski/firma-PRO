using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>Pojedynczy wiersz formularza pozycji faktury.</summary>
public sealed class WierszPozycji
{
    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal Ilosc { get; set; } = 1m;
    public decimal CenaNetto { get; set; }
    public string KodStawki { get; set; } = StawkaVat.Vat23.Kod;
    public string? Gtu { get; set; }

    /// <summary>Wiersz uznajemy za pusty, gdy nie ma nazwy - taki pomijamy.</summary>
    public bool CzyPusty => string.IsNullOrWhiteSpace(Nazwa);
}

/// <summary>Wystawianie nowej faktury sprzedaży.</summary>
public sealed class NowaModel(FirmaProDbContext baza, UslugaFaktur uslugaFaktur) : PageModel
{
    /// <summary>Kody GTU do wyboru na liście.</summary>
    public static IReadOnlyList<string> KodyGtu { get; } =
        Enumerable.Range(1, 13).Select(n => $"GTU_{n:00}").ToList();

    [BindProperty] public Guid KontrahentId { get; set; }
    [BindProperty] public DateOnly DataWystawienia { get; set; }
    [BindProperty] public DateOnly? DataSprzedazy { get; set; }
    [BindProperty] public DateOnly? TerminPlatnosci { get; set; }
    [BindProperty] public FormaPlatnosci? FormaPlatnosci { get; set; }
    [BindProperty] public string? PodstawaZwolnienia { get; set; }
    [BindProperty] public List<WierszPozycji> Pozycje { get; set; } = [];

    public IReadOnlyList<Kontrahent> Kontrahenci { get; private set; } = [];
    public List<string> Bledy { get; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        if (Kontrahenci.Count == 0)
        {
            TempData["Ostrzezenie"] =
                "Zanim wystawisz fakturę, dodaj przynajmniej jednego kontrahenta.";
            return RedirectToPage("/Kontrahenci/Nowy");
        }

        DateOnly dzisiaj = DateOnly.FromDateTime(DateTime.Today);
        DataWystawienia = dzisiaj;
        DataSprzedazy = dzisiaj;

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);
        TerminPlatnosci = dzisiaj.AddDays(firma.DomyslnyTerminPlatnosciDni);
        FormaPlatnosci = Domena.FormaPlatnosci.Przelew;

        // Jeden pusty wiersz na start - użytkownik od razu ma gdzie pisać.
        Pozycje = [new WierszPozycji()];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        List<WierszPozycji> wypelnione = Pozycje.Where(p => !p.CzyPusty).ToList();
        if (wypelnione.Count == 0)
        {
            Bledy.Add("Faktura musi mieć przynajmniej jedną pozycję z nazwą.");
            ZapewnijWiersz();
            return Page();
        }

        WynikWystawienia wynik = await uslugaFaktur.WystawAsync(
            KontrahentId,
            DataWystawienia,
            DataSprzedazy,
            TerminPlatnosci,
            FormaPlatnosci,
            PodstawaZwolnienia,
            wypelnione.Select(p => (p.Nazwa, p.Jednostka, p.Ilosc, p.CenaNetto,
                                    p.KodStawki, p.Gtu)).ToList(),
            anulowanie);

        if (!wynik.Udalo)
        {
            Bledy.AddRange(wynik.Walidacja.Problemy
                .Where(p => p.Poziom == PoziomProblemu.Blad)
                .Select(p => $"{p.Pole}: {p.Komunikat}"));
            ZapewnijWiersz();
            return Page();
        }

        TempData["Komunikat"] =
            $"Wystawiono fakturę {wynik.Faktura!.Numer} na kwotę " +
            $"{wynik.Faktura.RazemBrutto:N2} {wynik.Faktura.Waluta}.";

        return RedirectToPage("Szczegoly", new { id = wynik.Faktura.Id });
    }

    private async Task WczytajListyAsync(CancellationToken anulowanie)
    {
        Kontrahenci = await baza.Kontrahenci
            .Where(k => k.Aktywny)
            .OrderBy(k => k.Nazwa)
            .ToListAsync(anulowanie);
    }

    private void ZapewnijWiersz()
    {
        if (Pozycje.Count == 0)
        {
            Pozycje.Add(new WierszPozycji());
        }
    }

    /// <summary>Nazwa formy płatności pokazywana użytkownikowi.</summary>
    public static string OpisFormy(FormaPlatnosci forma) => forma switch
    {
        Domena.FormaPlatnosci.Gotowka => "Gotówka",
        Domena.FormaPlatnosci.Karta => "Karta",
        Domena.FormaPlatnosci.Bon => "Bon",
        Domena.FormaPlatnosci.Czek => "Czek",
        Domena.FormaPlatnosci.Kredyt => "Kredyt",
        Domena.FormaPlatnosci.Przelew => "Przelew",
        Domena.FormaPlatnosci.Mobilna => "Płatność mobilna",
        _ => forma.ToString()
    };
}
