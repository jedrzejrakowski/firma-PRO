using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Przejście do innej firmy tego samego użytkownika.
/// </summary>
/// <remarks>
/// Biuro rachunkowe obsługuje wiele firm jednym kontem. Przełączenie polega
/// na wystawieniu nowego ciasteczka - przynależność sprawdzana jest w bazie,
/// więc podanie cudzego identyfikatora niczego nie otwiera.
/// </remarks>
public sealed class PrzelaczFirmeModel(FirmaProDbContext baza) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Faktury/Index");

    public async Task<IActionResult> OnPostAsync(Guid firmaId, CancellationToken anulowanie)
    {
        if (Tozsamosc.UzytkownikId(User) is not Guid uzytkownikId)
        {
            return RedirectToPage("/Logowanie");
        }

        // Zapytanie o członkostwo z jawnym warunkiem na użytkownika: to ono,
        // a nie treść formularza, rozstrzyga o dostępie do firmy.
        CzlonkostwoWFirmie? czlonkostwo = await baza.Czlonkostwa
            .Include(c => c.Firma)
            .Include(c => c.Uzytkownik)
            .FirstOrDefaultAsync(
                c => c.UzytkownikId == uzytkownikId && c.FirmaId == firmaId, anulowanie);

        if (czlonkostwo?.Uzytkownik is null)
        {
            TempData["Ostrzezenie"] = "Nie masz dostępu do wskazanej firmy.";
            return RedirectToPage("/Faktury/Index");
        }

        await Tozsamosc.ZalogujAsync(HttpContext, czlonkostwo.Uzytkownik, czlonkostwo);

        TempData["Komunikat"] = $"Pracujesz teraz w firmie {czlonkostwo.Firma?.Nazwa}.";
        return RedirectToPage("/Faktury/Index");
    }
}
