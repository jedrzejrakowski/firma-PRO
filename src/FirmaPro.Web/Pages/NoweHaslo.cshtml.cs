using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Ustawienie nowego hasła na podstawie jednorazowego odnośnika.
/// </summary>
/// <remarks>
/// Strona nie wymaga zalogowania - z założenia trafia tu ktoś, kto nie może
/// się zalogować. Kod z odnośnika jest jedynym dowodem uprawnienia.
/// </remarks>
[AllowAnonymous]
public sealed class NoweHasloModel(UslugaKont uslugaKont) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Kod { get; set; } = string.Empty;

    [BindProperty] public string Haslo { get; set; } = string.Empty;
    [BindProperty] public string PowtorzHaslo { get; set; } = string.Empty;

    public bool OdnosnikWazny { get; private set; }

    public WynikWalidacji Walidacja { get; private set; } = new();

    public int MinimalnaDlugoscHasla => UstawieniaStartu.MinimalnaDlugoscHasla;

    public async Task OnGetAsync(CancellationToken anulowanie) =>
        OdnosnikWazny = await uslugaKont.ZnajdzResetAsync(Kod, anulowanie) is not null;

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        WynikKonta<Uzytkownik> wynik = await uslugaKont.UstawNoweHasloAsync(
            Kod, Haslo, PowtorzHaslo, anulowanie);

        if (!wynik.Udalo)
        {
            Walidacja = wynik.Walidacja;
            OdnosnikWazny = await uslugaKont.ZnajdzResetAsync(Kod, anulowanie) is not null;
            return Page();
        }

        TempData["Komunikat"] = "Hasło ustawione. Możesz się zalogować.";
        return RedirectToPage("/Logowanie");
    }
}
