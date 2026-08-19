using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Podatek;

/// <summary>
/// Zaliczki na podatek dochodowy.
/// </summary>
/// <remarks>
/// Ostatni rachunek, którego programowi brakowało. Wszystkie składniki są już
/// policzone gdzie indziej - dochód w księdze, remanenty w spisie, składki
/// w ZUS-ie - a tu się spotykają.
/// </remarks>
public sealed class IndexModel(
    FirmaProDbContext baza,
    UslugaPit uslugaPit) : PageModel
{
    public int Rok { get; private set; }

    public IReadOnlyList<OkresPit> Okresy { get; private set; } = [];

    public Firma Firma { get; private set; } = new();

    public SkalaPodatkowa? Skala { get; private set; }

    public DateOnly Dzis { get; } = DateOnly.FromDateTime(DateTime.Today);

    // --- formularz ustawień ---------------------------------------------------

    [BindProperty] public bool ZaliczkiKwartalne { get; set; }
    [BindProperty] public decimal StrataDoOdliczenia { get; set; }

    public List<string> Bledy { get; } = [];

    public bool Ryczalt => Firma.FormaOpodatkowania == FormaOpodatkowania.Ryczalt;

    public bool Liniowy => Firma.FormaOpodatkowania == FormaOpodatkowania.Liniowy;

    /// <summary>Nazwa okresu - miesiąc albo kwartał.</summary>
    public static string NazwaOkresu(OkresRozliczeniowy okres)
    {
        ArgumentNullException.ThrowIfNull(okres);

        return okres.Typ == TypOkresu.Kwartalny
            ? $"{okres.Numer}. kwartał"
            : NazwaMiesiaca(okres.Numer);
    }

    public static string NazwaMiesiaca(int miesiac) => miesiac switch
    {
        1 => "styczeń", 2 => "luty", 3 => "marzec", 4 => "kwiecień",
        5 => "maj", 6 => "czerwiec", 7 => "lipiec", 8 => "sierpień",
        9 => "wrzesień", 10 => "październik", 11 => "listopad", 12 => "grudzień",
        _ => miesiac.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>Nazwa formy opodatkowania wraz z zasadą rachunku.</summary>
    public static string NazwaFormy(FormaOpodatkowania forma) => forma switch
    {
        FormaOpodatkowania.Skala => "Skala podatkowa — 12% i 32% ponad progiem",
        FormaOpodatkowania.Liniowy => "Podatek liniowy — 19% bez kwoty wolnej",
        FormaOpodatkowania.Ryczalt => "Ryczałt — stawka od przychodu",
        _ => forma.ToString()
    };

    /// <summary>Suma podatku zapłaconego w roku.</summary>
    public long RazemZaplacone => Okresy.Sum(o => o.Zaplacono);

    /// <summary>Podatek narastająco po ostatnim policzonym okresie.</summary>
    public long PodatekRoku =>
        Okresy.Count == 0 ? 0L : Okresy[^1].Zaliczka.PodatekNarastajaco;

    /// <summary>Okresy po terminie - te wymagają reakcji.</summary>
    public IReadOnlyList<OkresPit> PoTerminie =>
        [.. Okresy.Where(o => o.PoTerminie(Dzis))];

    public async Task OnGetAsync(int? rok, CancellationToken anulowanie) =>
        await WczytajAsync(rok, anulowanie);

    /// <summary>Zapisuje sposób rozliczania i stratę do odliczenia.</summary>
    public async Task<IActionResult> OnPostUstawieniaAsync(int? rok,
                                                           CancellationToken anulowanie)
    {
        await WczytajAsync(rok, anulowanie);

        if (StrataDoOdliczenia < 0m)
        {
            Bledy.Add("Strata do odliczenia nie może być ujemna.");
            return Page();
        }

        // Warunek po identyfikatorze, bo Firma nie podlega filtrowi najemcy.
        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        firma.ZaliczkiKwartalne = ZaliczkiKwartalne;
        firma.StrataDoOdliczenia = StrataDoOdliczenia;

        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] = "Zapisano zasady rozliczania podatku.";

        return RedirectToPage(new { rok = Rok });
    }

    /// <summary>Potwierdza zapłatę zaliczki.</summary>
    public async Task<IActionResult> OnPostZaplacAsync(int rok, int numer,
                                                       bool kwartalna,
                                                       long kwota, DateOnly dzien,
                                                       CancellationToken anulowanie)
    {
        await uslugaPit.ZaplacAsync(rok, numer, kwartalna, kwota,
            dzien == default ? DateOnly.FromDateTime(DateTime.Today) : dzien,
            anulowanie);

        TempData["Komunikat"] = "Zapisano zapłatę zaliczki.";

        return RedirectToPage(new { rok });
    }

    /// <summary>Cofa potwierdzenie zapłaty.</summary>
    public async Task<IActionResult> OnPostCofnijAsync(int rok, int numer,
                                                       bool kwartalna,
                                                       CancellationToken anulowanie)
    {
        await uslugaPit.CofnijAsync(rok, numer, kwartalna, anulowanie);

        TempData["Komunikat"] = "Cofnięto zapłatę zaliczki.";

        return RedirectToPage(new { rok });
    }

    private async Task WczytajAsync(int? rok, CancellationToken anulowanie)
    {
        Rok = rok ?? DateTime.Today.Year;
        Skala = SkalaPodatkowa.Dla(Rok);

        Firma = await baza.Firmy.AsNoTracking()
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);
        Okresy = await uslugaPit.RokAsync(Rok, anulowanie);

        if (!HttpContext.Request.HasFormContentType)
        {
            ZaliczkiKwartalne = Firma.ZaliczkiKwartalne;
            StrataDoOdliczenia = Firma.StrataDoOdliczenia;
        }
    }
}
