using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Srodki;

/// <summary>
/// Ewidencja środków trwałych.
/// </summary>
/// <remarks>
/// Ewidencja jest źródłem odpisów amortyzacyjnych, a te są kosztem zamiast
/// zakupu (art. 22 ust. 8 ustawy o PIT). Wpisanie tu środka trwałego nie jest
/// więc formalnością - dopóki go nie ma, koszt nie wchodzi do księgi wcale.
/// </remarks>
public sealed class IndexModel(FirmaProDbContext baza) : PageModel
{
    public IReadOnlyList<SrodekTrwalyFirmy> Srodki { get; private set; } = [];

    /// <summary>Plan odpisów środka wskazanego w adresie.</summary>
    public IReadOnlyList<Odpis> Plan { get; private set; } = [];

    public SrodekTrwalyFirmy? Wybrany { get; private set; }

    // --- formularz -----------------------------------------------------------

    [BindProperty] public string Nazwa { get; set; } = string.Empty;
    [BindProperty] public string NumerInwentarzowy { get; set; } = string.Empty;
    [BindProperty] public DateOnly DataPrzyjecia { get; set; }
    [BindProperty] public decimal WartoscPoczatkowa { get; set; }
    [BindProperty] public MetodaAmortyzacji Metoda { get; set; } = MetodaAmortyzacji.Liniowa;
    [BindProperty] public decimal StawkaRoczna { get; set; } = 20m;
    [BindProperty] public decimal Wspolczynnik { get; set; } = 2.0m;
    [BindProperty] public decimal? LimitKosztu { get; set; }
    [BindProperty] public string? Uwagi { get; set; }

    public List<string> Bledy { get; } = [];

    public static IReadOnlyList<MetodaAmortyzacji> Metody { get; } =
        Enum.GetValues<MetodaAmortyzacji>();

    /// <summary>Nazwa metody w języku użytkownika.</summary>
    public static string NazwaMetody(MetodaAmortyzacji metoda) => metoda switch
    {
        MetodaAmortyzacji.Liniowa => "Liniowa — równe odpisy",
        MetodaAmortyzacji.Degresywna => "Degresywna — wyższe na początku",
        MetodaAmortyzacji.Jednorazowa => "Jednorazowa — cała wartość naraz",
        _ => metoda.ToString()
    };

    /// <summary>Suma dotychczasowych odpisów środka na dziś.</summary>
    public static decimal Umorzenie(SrodekTrwalyFirmy srodek)
    {
        DateOnly dzis = DateOnly.FromDateTime(DateTime.Today);

        return PlanAmortyzacji.Zbuduj(srodek.NaModel())
            .Where(o => o.Okres.OstatniDzien <= dzis)
            .Select(o => o.Umorzenie)
            .LastOrDefault();
    }

    /// <summary>Ile jeszcze zostało do zamortyzowania.</summary>
    public static decimal DoUmorzenia(SrodekTrwalyFirmy srodek) =>
        Kwoty.Zaokraglij(srodek.WartoscPoczatkowa - Umorzenie(srodek));

    public async Task OnGetAsync(Guid? plan, CancellationToken anulowanie)
    {
        await WczytajAsync(plan, anulowanie);

        if (DataPrzyjecia == default)
        {
            DataPrzyjecia = DateOnly.FromDateTime(DateTime.Today);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        await WczytajAsync(null, anulowanie);

        if (string.IsNullOrWhiteSpace(Nazwa))
        {
            Bledy.Add("Środek trwały musi mieć nazwę.");
        }

        if (string.IsNullOrWhiteSpace(NumerInwentarzowy))
        {
            Bledy.Add("Środek trwały musi mieć numer inwentarzowy.");
        }

        if (WartoscPoczatkowa <= 0)
        {
            Bledy.Add("Wartość początkowa musi być większa od zera.");
        }

        if (Metoda != MetodaAmortyzacji.Jednorazowa && StawkaRoczna <= 0)
        {
            Bledy.Add("Roczna stawka amortyzacji musi być większa od zera.");
        }

        // Współczynnik ponad 2,0 przysługuje wyłącznie w gminach zagrożonych
        // bezrobociem (art. 22k ust. 1) - poza nimi to zwykła pomyłka.
        if (Metoda == MetodaAmortyzacji.Degresywna && (Wspolczynnik < 1m || Wspolczynnik > 3m))
        {
            Bledy.Add("Współczynnik przy metodzie degresywnej mieści się między 1,0 a 3,0.");
        }

        bool numerZajety = await baza.SrodkiTrwale
            .AnyAsync(s => s.NumerInwentarzowy == NumerInwentarzowy.Trim(), anulowanie);

        if (numerZajety)
        {
            Bledy.Add($"Numer inwentarzowy {NumerInwentarzowy.Trim()} jest już zajęty.");
        }

        if (Bledy.Count > 0)
        {
            return Page();
        }

        baza.SrodkiTrwale.Add(new SrodekTrwalyFirmy
        {
            Nazwa = Nazwa.Trim(),
            NumerInwentarzowy = NumerInwentarzowy.Trim(),
            DataPrzyjecia = DataPrzyjecia,
            WartoscPoczatkowa = Kwoty.Zaokraglij(WartoscPoczatkowa),
            Metoda = Metoda,
            StawkaRoczna = StawkaRoczna,
            Wspolczynnik = Wspolczynnik,
            LimitKosztu = LimitKosztu,
            Uwagi = string.IsNullOrWhiteSpace(Uwagi) ? null : Uwagi.Trim()
        });

        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] =
            $"Wpisano środek trwały {Nazwa.Trim()} do ewidencji. "
            + "Odpisy wejdą do księgi od następnego miesiąca.";

        return RedirectToPage();
    }

    /// <summary>Wycofuje środek z ewidencji - odpisy znikają z księgi.</summary>
    public async Task<IActionResult> OnPostUsunAsync(Guid id, CancellationToken anulowanie)
    {
        SrodekTrwalyFirmy? srodek = await baza.SrodkiTrwale
            .FirstOrDefaultAsync(s => s.Id == id, anulowanie);

        if (srodek is not null)
        {
            baza.SrodkiTrwale.Remove(srodek);
            await baza.SaveChangesAsync(anulowanie);

            TempData["Komunikat"] = $"Usunięto {srodek.Nazwa} z ewidencji.";
        }
        else
        {
            TempData["Ostrzezenie"] = "Nie znaleziono wskazanego środka trwałego.";
        }

        return RedirectToPage();
    }

    /// <summary>
    /// Zamyka amortyzację z dniem likwidacji albo sprzedaży.
    /// </summary>
    /// <remarks>
    /// Usunięcie środka wymazałoby także odpisy z lat poprzednich, a te były
    /// kosztem i muszą w księdze zostać. Likwidacja zatrzymuje plan, nie kasuje
    /// historii.
    /// </remarks>
    public async Task<IActionResult> OnPostZlikwidujAsync(Guid id, DateOnly dzien,
                                                          CancellationToken anulowanie)
    {
        SrodekTrwalyFirmy? srodek = await baza.SrodkiTrwale
            .FirstOrDefaultAsync(s => s.Id == id, anulowanie);

        if (srodek is null)
        {
            TempData["Ostrzezenie"] = "Nie znaleziono wskazanego środka trwałego.";
            return RedirectToPage();
        }

        srodek.DataLikwidacji = dzien == default
            ? DateOnly.FromDateTime(DateTime.Today)
            : dzien;

        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] =
            $"Zamknięto amortyzację {srodek.Nazwa} z dniem "
            + srodek.DataLikwidacji.Value.ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture) + ".";

        return RedirectToPage();
    }

    private async Task WczytajAsync(Guid? plan, CancellationToken anulowanie)
    {
        Srodki = await baza.SrodkiTrwale
            .AsNoTracking()
            .OrderBy(s => s.DataPrzyjecia)
            .ThenBy(s => s.NumerInwentarzowy)
            .ToListAsync(anulowanie);

        if (plan is { } id)
        {
            Wybrany = Srodki.FirstOrDefault(s => s.Id == id);

            if (Wybrany is not null)
            {
                Plan = PlanAmortyzacji.Zbuduj(Wybrany.NaModel());
            }
        }
    }
}
