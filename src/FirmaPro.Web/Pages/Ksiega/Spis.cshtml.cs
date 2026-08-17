using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Ksiega;

/// <summary>
/// Spisy z natury i roczne rozliczenie dochodu.
/// </summary>
/// <remarks>
/// Spis nie jest ani przychodem, ani kosztem - jest stanem magazynu. Wchodzi
/// do rachunku dopiero różnicą między remanentem początkowym a końcowym,
/// i tylko raz w roku. Dlatego stoi obok księgi, a nie w niej.
/// </remarks>
public sealed class SpisModel(
    FirmaProDbContext baza,
    UslugaKsiegi uslugaKsiegi) : PageModel
{
    public IReadOnlyList<SpisZNaturyFirmy> Spisy { get; private set; } = [];

    public SpisZNaturyFirmy? Otwarty { get; private set; }

    public int Rok { get; private set; }

    public RozliczenieRoczne? Rozliczenie { get; private set; }

    public SpisZNatury? RemanentPoczatkowy { get; private set; }

    public SpisZNatury? RemanentKoncowy { get; private set; }

    // --- formularze ----------------------------------------------------------

    [BindProperty] public DateOnly DataSpisu { get; set; }
    [BindProperty] public string? UwagiSpisu { get; set; }

    [BindProperty] public string Nazwa { get; set; } = string.Empty;
    [BindProperty] public string Jednostka { get; set; } = "szt.";
    [BindProperty] public decimal Ilosc { get; set; } = 1m;
    [BindProperty] public decimal CenaJednostkowa { get; set; }
    [BindProperty] public SposobWyceny Wycena { get; set; } = SposobWyceny.CenaZakupu;

    public List<string> Bledy { get; } = [];

    public static IReadOnlyList<SposobWyceny> SposobyWyceny { get; } =
        Enum.GetValues<SposobWyceny>();

    /// <summary>Nazwa sposobu wyceny wraz z tym, czego dotyczy.</summary>
    public static string NazwaWyceny(SposobWyceny wycena) => wycena switch
    {
        SposobWyceny.CenaZakupu => "Cena zakupu — towary i materiały",
        SposobWyceny.CenaRynkowa => "Cena rynkowa — gdy niższa od zakupu",
        SposobWyceny.KosztWytworzenia => "Koszt wytworzenia — wyroby własne",
        SposobWyceny.Oszacowanie => "Oszacowanie — odpady użytkowe",
        _ => wycena.ToString()
    };

    public async Task OnGetAsync(int? rok, Guid? spis, CancellationToken anulowanie)
    {
        await WczytajAsync(rok, spis, anulowanie);

        if (DataSpisu == default)
        {
            DataSpisu = new DateOnly(Rok, 12, 31);
        }
    }

    /// <summary>Zakłada nowy arkusz spisu.</summary>
    public async Task<IActionResult> OnPostZalozAsync(int? rok, CancellationToken anulowanie)
    {
        await WczytajAsync(rok, null, anulowanie);

        bool zajety = await baza.Spisy.AnyAsync(s => s.Data == DataSpisu, anulowanie);

        if (zajety)
        {
            // Dwa spisy na ten sam dzień oznaczałyby, że nie wiadomo, który
            // jest remanentem - a od tego zależy dochód roczny.
            Bledy.Add($"Spis na dzień {DataSpisu:yyyy-MM-dd} już istnieje.");
            return Page();
        }

        var nowy = new SpisZNaturyFirmy
        {
            Data = DataSpisu,
            Uwagi = string.IsNullOrWhiteSpace(UwagiSpisu) ? null : UwagiSpisu.Trim()
        };

        baza.Spisy.Add(nowy);
        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] = $"Założono spis na dzień {DataSpisu:yyyy-MM-dd}.";

        return RedirectToPage(new { rok = Rok, spis = nowy.Id });
    }

    /// <summary>Dopisuje pozycję do otwartego arkusza.</summary>
    public async Task<IActionResult> OnPostDodajAsync(int? rok, Guid spis,
                                                      CancellationToken anulowanie)
    {
        await WczytajAsync(rok, spis, anulowanie);

        if (Otwarty is null)
        {
            return NotFound();
        }

        if (Otwarty.Zamkniety)
        {
            Bledy.Add("Spis jest zamknięty — poprawkę robi się nowym spisem.");
        }

        if (string.IsNullOrWhiteSpace(Nazwa))
        {
            Bledy.Add("Pozycja musi mieć nazwę.");
        }

        if (Ilosc <= 0)
        {
            Bledy.Add("Ilość musi być większa od zera.");
        }

        if (CenaJednostkowa < 0)
        {
            Bledy.Add("Cena nie może być ujemna.");
        }

        if (Bledy.Count > 0)
        {
            return Page();
        }

        int nastepny = Otwarty.Pozycje.Count == 0
            ? 1
            : Otwarty.Pozycje.Max(p => p.NrPozycji) + 1;

        baza.PozycjeSpisow.Add(new PozycjaSpisuFirmy
        {
            SpisId = Otwarty.Id,
            NrPozycji = nastepny,
            Nazwa = Nazwa.Trim(),
            Jednostka = string.IsNullOrWhiteSpace(Jednostka) ? "szt." : Jednostka.Trim(),
            Ilosc = Ilosc,
            CenaJednostkowa = CenaJednostkowa,
            Wycena = Wycena
        });

        await baza.SaveChangesAsync(anulowanie);

        return RedirectToPage(new { rok = Rok, spis });
    }

    public async Task<IActionResult> OnPostUsunPozycjeAsync(int? rok, Guid spis, Guid id,
                                                            CancellationToken anulowanie)
    {
        PozycjaSpisuFirmy? pozycja = await baza.PozycjeSpisow
            .FirstOrDefaultAsync(p => p.Id == id, anulowanie);

        if (pozycja is not null)
        {
            baza.PozycjeSpisow.Remove(pozycja);
            await baza.SaveChangesAsync(anulowanie);
        }

        return RedirectToPage(new { rok, spis });
    }

    /// <summary>
    /// Zamyka spis.
    /// </summary>
    /// <remarks>
    /// Spis podpisuje się i przechowuje razem z księgą, więc po zamknięciu
    /// nie dopisuje się do niego pozycji - poprawka to nowy spis.
    /// </remarks>
    public async Task<IActionResult> OnPostZamknijAsync(int? rok, Guid spis,
                                                        CancellationToken anulowanie)
    {
        SpisZNaturyFirmy? arkusz = await baza.Spisy
            .FirstOrDefaultAsync(s => s.Id == spis, anulowanie);

        if (arkusz is not null)
        {
            arkusz.Zamkniety = true;
            await baza.SaveChangesAsync(anulowanie);

            TempData["Komunikat"] = "Spis zamknięty i policzony do rozliczenia rocznego.";
        }

        return RedirectToPage(new { rok, spis });
    }

    public async Task<IActionResult> OnPostUsunSpisAsync(int? rok, Guid id,
                                                         CancellationToken anulowanie)
    {
        SpisZNaturyFirmy? arkusz = await baza.Spisy
            .FirstOrDefaultAsync(s => s.Id == id, anulowanie);

        if (arkusz is not null)
        {
            baza.Spisy.Remove(arkusz);
            await baza.SaveChangesAsync(anulowanie);

            TempData["Komunikat"] = "Usunięto spis z natury.";
        }

        return RedirectToPage(new { rok });
    }

    private async Task WczytajAsync(int? rok, Guid? spis, CancellationToken anulowanie)
    {
        Rok = rok ?? DateTime.Today.Year;

        Spisy = await baza.Spisy
            .AsNoTracking()
            .Include(s => s.Pozycje)
            .OrderByDescending(s => s.Data)
            .ToListAsync(anulowanie);

        if (spis is { } id)
        {
            Otwarty = Spisy.FirstOrDefault(s => s.Id == id);
        }

        List<SpisZNatury> modele = [.. Spisy.Select(s => s.NaModel())];

        RemanentPoczatkowy = Remanenty.Poczatkowy(modele, Rok);
        RemanentKoncowy = Remanenty.Koncowy(modele, Rok);

        Rozliczenie = await uslugaKsiegi.RozliczenieRoczneAsync(Rok, anulowanie);
    }
}
