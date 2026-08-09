using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>Wylogowanie - dostępne wyłącznie metodą POST.</summary>
/// <remarks>
/// Wylogowanie zmienia stan, więc nie może być zwykłym odnośnikiem: inaczej
/// wystarczyłby obrazek wskazujący na ten adres, żeby wylogować użytkownika
/// bez jego wiedzy.
/// </remarks>
public sealed class WylogujModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Logowanie");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Logowanie");
    }
}
