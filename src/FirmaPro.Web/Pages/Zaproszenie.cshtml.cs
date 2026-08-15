using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Przyjęcie zaproszenia do firmy.
/// </summary>
/// <remarks>
/// Strona dostępna bez logowania - zapraszany zwykle nie ma jeszcze konta.
/// Kod z odnośnika jest jedynym dowodem uprawnienia, dlatego strona nie
/// zdradza niczego o firmie, dopóki kod się nie zgadza.
/// </remarks>
[AllowAnonymous]
public sealed class ZaproszenieModel(UslugaKont uslugaKont) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Kod { get; set; } = string.Empty;

    [BindProperty] public string ImieINazwisko { get; set; } = string.Empty;
    [BindProperty] public string Haslo { get; set; } = string.Empty;

    public Zaproszenie? Zapraszane { get; private set; }

    /// <summary>Czy adres z zaproszenia ma już konto w programie.</summary>
    public bool KontoIstnieje { get; private set; }

    public WynikWalidacji Walidacja { get; private set; } = new();

    public int MinimalnaDlugoscHasla => UstawieniaStartu.MinimalnaDlugoscHasla;

    public string OpisRoli => Zapraszane?.Rola switch
    {
        RolaWFirmie.Wlasciciel => "właściciel - pełny dostęp, w tym ustawienia firmy",
        RolaWFirmie.Ksiegowy => "księgowy - wystawianie faktur i prowadzenie kartotek",
        _ => "podgląd - odczyt bez prawa zmiany"
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken anulowanie)
    {
        await WczytajAsync(anulowanie);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        WynikKonta<CzlonkostwoWFirmie> wynik = await uslugaKont.PrzyjmijZaproszenieAsync(
            Kod, ImieINazwisko, Haslo, anulowanie);

        if (!wynik.Udalo)
        {
            Walidacja = wynik.Walidacja;
            await WczytajAsync(anulowanie);
            return Page();
        }

        await Tozsamosc.ZalogujAsync(HttpContext, wynik.Dane!.Uzytkownik!, wynik.Dane);

        TempData["Komunikat"] = $"Dołączono do firmy {wynik.Dane.Firma?.Nazwa}.";
        return RedirectToPage("/Pulpit");
    }

    private async Task WczytajAsync(CancellationToken anulowanie)
    {
        Zapraszane = await uslugaKont.ZnajdzZaproszenieAsync(Kod, anulowanie);

        if (Zapraszane is not null)
        {
            KontoIstnieje = await uslugaKont.CzyKontoIstniejeAsync(Zapraszane.Email, anulowanie);
        }
    }
}
