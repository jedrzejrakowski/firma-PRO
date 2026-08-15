using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>Strona startowa - kieruje tam, gdzie użytkownik ma coś do zrobienia.</summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true
            ? RedirectToPage("/Pulpit")
            : RedirectToPage("/Logowanie");
}
