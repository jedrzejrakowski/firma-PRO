using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>
/// Faktury firmy - sprzedaż i zakupy na jednym ekranie.
/// </summary>
/// <remarks>
/// Wcześniej były to dwie osobne pozycje w menu, choć użytkownik ma z nimi do
/// czynienia w tej samej chwili: wystawia faktury i wprowadza koszty tego
/// samego miesiąca. Dwie zakładki na jednym ekranie zamiast dwóch ekranów
/// skracają drogę i zwalniają miejsce w menu.
/// </remarks>
public sealed class IndexModel(
    FirmaProDbContext baza,
    UslugaZakupow uslugaZakupow,
    UslugaRejestruVat uslugaRejestru) : PageModel
{
    /// <summary>Nazwa zakładki z fakturami zakupu.</summary>
    public const string WidokKoszty = "koszty";

    public IReadOnlyList<FakturaSprzedazy> Faktury { get; private set; } = [];

    public IReadOnlyList<FakturaZakupu> Zakupy { get; private set; } = [];

    /// <summary>Która zakładka ma być otwarta po wejściu na ekran.</summary>
    /// <remarks>
    /// Wybór zakładki siedzi w adresie, więc odnośnik „pokaż koszty” działa
    /// także bez skryptów, a powrót ze szczegółów dokumentu trafia tam, skąd
    /// użytkownik wyszedł.
    /// </remarks>
    [BindProperty(SupportsGet = true, Name = "widok")]
    public string Widok { get; set; } = string.Empty;

    public bool KosztyNaWierzchu =>
        string.Equals(Widok, WidokKoszty, StringComparison.OrdinalIgnoreCase);

    private TypOkresu _typOkresu = TypOkresu.Miesieczny;

    /// <summary>
    /// Nazwa okresu, w którym odliczany jest podatek z faktury zakupu.
    /// </summary>
    /// <remarks>
    /// W bazie trzymamy datę, ale znaczenie ma wyłącznie okres, w który ta
    /// data wpada. Pokazanie samej daty sugerowałoby, że wydarzyło się coś
    /// pierwszego dnia miesiąca - a to tylko sposób zapisu.
    /// </remarks>
    public string OpisOkresu(DateOnly data) =>
        OkresRozliczeniowy.Dla(data, _typOkresu).Nazwa;

    public async Task OnGetAsync(CancellationToken anulowanie) =>
        await WczytajAsync(anulowanie);

    public async Task<IActionResult> OnPostUsunZakupAsync(Guid id, CancellationToken anulowanie)
    {
        if (await uslugaZakupow.UsunAsync(id, anulowanie))
        {
            TempData["Komunikat"] = "Usunięto fakturę zakupu.";
        }
        else
        {
            // Filtr firmy nie przepuścił dokumentu - albo już go nie ma, albo
            // należy do innej firmy. Dla użytkownika to ta sama sytuacja.
            TempData["Ostrzezenie"] = "Nie znaleziono wskazanej faktury zakupu.";
        }

        return RedirectToPage(new { widok = WidokKoszty });
    }

    /// <summary>Opis statusu w języku, którym posługuje się użytkownik.</summary>
    public static string OpisStatusu(StatusKsef status) => status switch
    {
        StatusKsef.Robocza => "Robocza",
        StatusKsef.Wyslana => "Wysłana",
        StatusKsef.Przyjeta => "Przyjęta",
        StatusKsef.Odrzucona => "Odrzucona",
        _ => status.ToString()
    };

    private async Task WczytajAsync(CancellationToken anulowanie)
    {
        Faktury = await baza.FakturySprzedazy
            .OrderByDescending(f => f.DataWystawienia)
            .ThenByDescending(f => f.UtworzonoUtc)
            .Take(200)
            .ToListAsync(anulowanie);

        _typOkresu = await uslugaRejestru.TypOkresuAsync(anulowanie);
        Zakupy = await uslugaZakupow.ListaAsync(anulowanie);
    }
}
