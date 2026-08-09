using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Zakupy;

/// <summary>Lista faktur zakupu wprowadzonych do rejestru.</summary>
public sealed class IndexModel(
    UslugaZakupow uslugaZakupow,
    UslugaRejestruVat uslugaRejestru) : PageModel
{
    public IReadOnlyList<FakturaZakupu> Faktury { get; private set; } = [];

    private TypOkresu _typOkresu = TypOkresu.Miesieczny;

    /// <summary>
    /// Nazwa okresu, w którym odliczany jest podatek z faktury.
    /// </summary>
    /// <remarks>
    /// W bazie trzymamy datę, ale znaczenie ma wyłącznie okres, w który ta
    /// data wpada. Pokazanie samej daty sugerowałoby, że wydarzyło się coś
    /// pierwszego dnia miesiąca - a to tylko sposób zapisu.
    /// </remarks>
    public string OpisOkresu(DateOnly data) =>
        OkresRozliczeniowy.Dla(data, _typOkresu).Nazwa;

    public async Task OnGetAsync(CancellationToken anulowanie)
    {
        _typOkresu = await uslugaRejestru.TypOkresuAsync(anulowanie);
        Faktury = await uslugaZakupow.ListaAsync(anulowanie);
    }

    public async Task<IActionResult> OnPostUsunAsync(Guid id, CancellationToken anulowanie)
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

        return RedirectToPage();
    }
}
