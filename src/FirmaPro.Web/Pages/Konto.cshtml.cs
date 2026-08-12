using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Własne konto: dane, hasło i firmy, w których się pracuje.
/// </summary>
/// <remarks>
/// Ekran dostępny dla każdego zalogowanego, niezależnie od roli. Rola mówi,
/// co wolno robić w firmie - nie odbiera prawa do własnego hasła.
/// </remarks>
public sealed class KontoModel(UslugaKont uslugaKont) : PageModel
{
    [BindProperty] public string ImieINazwisko { get; set; } = string.Empty;
    [BindProperty] public string ObecneHaslo { get; set; } = string.Empty;
    [BindProperty] public string NoweHaslo { get; set; } = string.Empty;
    [BindProperty] public string PowtorzHaslo { get; set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public IReadOnlyList<CzlonkostwoWFirmie> MojeFirmy { get; private set; } = [];

    public WynikWalidacji Walidacja { get; private set; } = new();

    public int MinimalnaDlugoscHasla => UstawieniaStartu.MinimalnaDlugoscHasla;

    public static string OpisRoli(RolaWFirmie rola) => rola switch
    {
        RolaWFirmie.Wlasciciel => "właściciel",
        RolaWFirmie.Ksiegowy => "księgowy",
        _ => "podgląd"
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken anulowanie)
    {
        if (Tozsamosc.UzytkownikId(User) is not Guid kto)
        {
            return RedirectToPage("/Logowanie");
        }

        await WczytajAsync(kto, anulowanie);
        return Page();
    }

    public async Task<IActionResult> OnPostDaneAsync(CancellationToken anulowanie)
    {
        if (Tozsamosc.UzytkownikId(User) is not Guid kto)
        {
            return RedirectToPage("/Logowanie");
        }

        await uslugaKont.ZmienDaneAsync(kto, ImieINazwisko, anulowanie);

        TempData["Komunikat"] = "Zapisano dane konta.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostHasloAsync(CancellationToken anulowanie)
    {
        if (Tozsamosc.UzytkownikId(User) is not Guid kto)
        {
            return RedirectToPage("/Logowanie");
        }

        WynikKonta<Uzytkownik> wynik = await uslugaKont.ZmienHasloAsync(
            kto, ObecneHaslo, NoweHaslo, PowtorzHaslo, anulowanie);

        if (!wynik.Udalo)
        {
            Walidacja = wynik.Walidacja;
            await WczytajAsync(kto, anulowanie);
            return Page();
        }

        // Zmiana hasła unieważnia wszystkie sesje - łącznie z bieżącą.
        // Zamiast pokazywać stronę, która zaraz przestanie działać,
        // odsyłamy do logowania.
        TempData["Komunikat"] =
            "Hasło zmienione. Zaloguj się nowym hasłem - pozostałe sesje zostały zamknięte.";

        return RedirectToPage("/Logowanie");
    }

    public async Task<IActionResult> OnPostOpuscAsync(CancellationToken anulowanie)
    {
        if (Tozsamosc.UzytkownikId(User) is not Guid kto)
        {
            return RedirectToPage("/Logowanie");
        }

        WynikKonta<CzlonkostwoWFirmie> wynik = await uslugaKont.OpuscFirmeAsync(kto, anulowanie);

        if (!wynik.Udalo)
        {
            TempData["Ostrzezenie"] = wynik.Walidacja.Problemy
                .FirstOrDefault(p => p.Poziom == PoziomProblemu.Blad)?.Komunikat
                ?? "Nie udało się opuścić firmy.";

            return RedirectToPage();
        }

        // Ciasteczko wskazuje firmę, w której nas już nie ma, więc dalsze
        // klikanie kończyłoby się pustymi ekranami. Wylogowujemy od razu.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        TempData["Komunikat"] = "Opuszczono firmę. Zaloguj się ponownie.";
        return RedirectToPage("/Logowanie");
    }

    private async Task WczytajAsync(Guid kto, CancellationToken anulowanie)
    {
        Email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;
        ImieINazwisko = User.Identity?.Name ?? string.Empty;
        MojeFirmy = await uslugaKont.FirmyUzytkownikaAsync(kto, anulowanie);
    }
}
