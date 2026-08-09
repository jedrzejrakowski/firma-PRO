using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Kontrahenci;

/// <summary>Lista kontrahentów bieżącej firmy.</summary>
/// <remarks>
/// Zapytanie nie zawiera warunku na firmę - zawęża je globalny filtr
/// kontekstu bazy. Dzięki temu nie da się przypadkowo pokazać cudzych danych
/// przez zapomniany warunek.
/// </remarks>
public sealed class IndexModel(FirmaProDbContext baza) : PageModel
{
    public IReadOnlyList<Kontrahent> Kontrahenci { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken anulowanie)
    {
        Kontrahenci = await baza.Kontrahenci
            .Where(k => k.Aktywny)
            .OrderBy(k => k.Nazwa)
            .ToListAsync(anulowanie);
    }
}
