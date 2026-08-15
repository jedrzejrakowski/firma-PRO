using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Zakupy;

/// <summary>
/// Dawny osobny ekran faktur zakupu.
/// </summary>
/// <remarks>
/// Lista kosztów jest teraz zakładką na ekranie faktur. Adres zostaje, bo
/// mógł trafić do zakładek przeglądarki albo do wysłanej komuś wiadomości -
/// kieruje wprost na właściwą zakładkę.
/// </remarks>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() =>
        RedirectToPage("/Faktury/Index",
            new { widok = Pages.Faktury.IndexModel.WidokKoszty });
}
