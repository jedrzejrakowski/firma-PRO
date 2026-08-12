using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Założenie konta wraz z nową firmą.
/// </summary>
/// <remarks>
/// Strona bywa wyłączona. Instalacja postawiona dla jednej firmy nie powinna
/// pozwalać obcym zakładać u siebie kont, więc rejestracja jest domyślnie
/// zamknięta i włącza się ją ustawieniem wdrożenia.
/// </remarks>
[AllowAnonymous]
public sealed class RejestracjaModel(UslugaKont uslugaKont, IConfiguration ustawienia,
                                     IWebHostEnvironment srodowisko) : PageModel
{
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Haslo { get; set; } = string.Empty;
    [BindProperty] public string PowtorzHaslo { get; set; } = string.Empty;
    [BindProperty] public string NazwaFirmy { get; set; } = string.Empty;
    [BindProperty] public string Nip { get; set; } = string.Empty;

    public WynikWalidacji Walidacja { get; private set; } = new();

    public int MinimalnaDlugoscHasla => UstawieniaStartu.MinimalnaDlugoscHasla;

    private bool Otwarta => UstawieniaStartu.CzyRejestracjaOtwarta(
        ustawienia, srodowisko.IsDevelopment());

    public IActionResult OnGet() => Otwarta ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        if (!Otwarta)
        {
            return NotFound();
        }

        WynikKonta<CzlonkostwoWFirmie> wynik = await uslugaKont.ZarejestrujAsync(
            Email, Haslo, PowtorzHaslo, NazwaFirmy, Nip, anulowanie);

        if (!wynik.Udalo)
        {
            Walidacja = wynik.Walidacja;
            return Page();
        }

        await Tozsamosc.ZalogujAsync(HttpContext, wynik.Dane!.Uzytkownik!, wynik.Dane);

        // Adres firmy trafia na każdą fakturę, a przy zakładaniu konta o niego
        // nie pytamy - dlatego od razu prowadzimy do ustawień.
        TempData["Komunikat"] =
            "Konto założone. Uzupełnij dane firmy - trafiają na każdą fakturę.";

        return RedirectToPage("/Ustawienia");
    }
}
