using System.Security.Claims;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Logowanie do systemu.
/// </summary>
/// <remarks>
/// Po zalogowaniu do ciasteczka trafia identyfikator firmy, w której
/// użytkownik pracuje. To z niego warstwa danych odczytuje kontekst
/// wielofirmowości - dlatego firma nigdy nie jest brana z adresu strony.
/// </remarks>
public sealed class LogowanieModel(
    FirmaProDbContext baza,
    IPasswordHasher<object> haszowanie,
    IWebHostEnvironment srodowisko) : PageModel
{
    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Haslo { get; set; } = string.Empty;

    public string? Blad { get; private set; }

    /// <summary>
    /// Podpowiedź z danymi konta demonstracyjnego - tylko poza produkcją.
    /// </summary>
    public bool PokazDaneDemonstracyjne => !srodowisko.IsProduction();

    public IActionResult OnGet()
    {
        return User.Identity?.IsAuthenticated == true
            ? RedirectToPage("/Faktury/Index")
            : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        string email = (Email ?? string.Empty).Trim();

        // ILike to porównanie bez rozróżniania wielkości liter po stronie
        // PostgreSQL - adresy e-mail nie są wrażliwe na wielkość znaków,
        // a porównanie w pamięci wymagałoby pobrania wszystkich kont.
        Uzytkownik? uzytkownik = await baza.Uzytkownicy
            .FirstOrDefaultAsync(u => EF.Functions.ILike(u.Email, email) && u.Aktywny,
                                 anulowanie);

        // Hasło sprawdzamy nawet dla nieistniejącego konta, żeby czas
        // odpowiedzi nie zdradzał, które adresy są zarejestrowane.
        PasswordVerificationResult wynik = uzytkownik is null
            ? PasswordVerificationResult.Failed
            : haszowanie.VerifyHashedPassword(new object(), uzytkownik.HaszHasla, Haslo ?? string.Empty);

        if (uzytkownik is null || wynik == PasswordVerificationResult.Failed)
        {
            Blad = "Nieprawidłowy adres e-mail lub hasło.";
            return Page();
        }

        CzlonkostwoWFirmie? czlonkostwo = await baza.Czlonkostwa
            .Include(c => c.Firma)
            .Where(c => c.UzytkownikId == uzytkownik.Id)
            .OrderBy(c => c.Firma!.Nazwa)
            .FirstOrDefaultAsync(anulowanie);

        if (czlonkostwo?.Firma is null)
        {
            Blad = "To konto nie jest przypisane do żadnej firmy.";
            return Page();
        }

        var oswiadczenia = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uzytkownik.Id.ToString()),
            new(ClaimTypes.Name, uzytkownik.ImieINazwisko ?? uzytkownik.Email),
            new(ClaimTypes.Email, uzytkownik.Email),
            new(KontekstFirmyZZadania.NazwaOswiadczenia, czlonkostwo.FirmaId.ToString()),
            new("nazwaFirmy", czlonkostwo.Firma.Nazwa),
            new(ClaimTypes.Role, czlonkostwo.Rola.ToString())
        };

        var tozsamosc = new ClaimsIdentity(oswiadczenia,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(tozsamosc));

        return RedirectToPage("/Faktury/Index");
    }
}
