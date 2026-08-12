using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Uzytkownicy;

/// <summary>
/// Kto ma dostęp do firmy i z jakimi uprawnieniami.
/// </summary>
/// <remarks>
/// Ekran dostępny wyłącznie dla właściciela - zasada zapisana jest przy
/// rejestracji stron w <c>Program.cs</c>, więc nie da się jej ominąć
/// wpisaniem adresu.
/// </remarks>
public sealed class IndexModel(UslugaKont uslugaKont) : PageModel
{
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public RolaWFirmie Rola { get; set; } = RolaWFirmie.Ksiegowy;

    public IReadOnlyList<CzlonkostwoWFirmie> Czlonkowie { get; private set; } = [];
    public IReadOnlyList<Zaproszenie> Zaproszenia { get; private set; } = [];

    public WynikWalidacji Walidacja { get; private set; } = new();

    /// <summary>Odnośnik wystawiony w tym żądaniu - pokazywany tylko raz.</summary>
    public string? NowyOdnosnik { get; private set; }

    public Guid? MojeCzlonkostwo { get; private set; }

    public static string OpisRoli(RolaWFirmie rola) => rola switch
    {
        RolaWFirmie.Wlasciciel => "Właściciel",
        RolaWFirmie.Ksiegowy => "Księgowy",
        _ => "Podgląd"
    };

    public async Task OnGetAsync(CancellationToken anulowanie) => await WczytajAsync(anulowanie);

    public async Task<IActionResult> OnPostZaproscAsync(CancellationToken anulowanie)
    {
        Guid? kto = Tozsamosc.UzytkownikId(User);

        WynikKonta<Zaproszenie> wynik = await uslugaKont.ZaproscAsync(
            Email, Rola, kto ?? Guid.Empty, anulowanie);

        if (!wynik.Udalo)
        {
            Walidacja = wynik.Walidacja;
            await WczytajAsync(anulowanie);
            return Page();
        }

        // Program nie wysyła poczty, więc odnośnik pokazujemy tutaj -
        // właściciel przekazuje go, jak mu wygodnie.
        NowyOdnosnik = OdnosnikZaproszenia(wynik.Dane!.Kod);

        // Formularz czyścimy, żeby kolejne zaproszenie nie poszło przez
        // przeoczenie pod ten sam adres.
        Email = string.Empty;

        await WczytajAsync(anulowanie);

        return Page();
    }

    public async Task<IActionResult> OnPostZmienRoleAsync(
        Guid id, RolaWFirmie rola, CancellationToken anulowanie)
    {
        WynikKonta<CzlonkostwoWFirmie> wynik = await uslugaKont.ZmienRoleAsync(id, rola, anulowanie);

        if (wynik.Udalo)
        {
            TempData["Komunikat"] =
                $"Zmieniono rolę: {wynik.Dane!.Uzytkownik?.Email} - {OpisRoli(rola).ToLowerInvariant()}.";
        }
        else
        {
            TempData["Ostrzezenie"] = PierwszyBlad(wynik.Walidacja);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostOdbierzAsync(Guid id, CancellationToken anulowanie)
    {
        WynikKonta<CzlonkostwoWFirmie> wynik = await uslugaKont.OdbierzDostepAsync(id, anulowanie);

        if (wynik.Udalo)
        {
            TempData["Komunikat"] = $"Odebrano dostęp: {wynik.Dane!.Uzytkownik?.Email}.";
        }
        else
        {
            TempData["Ostrzezenie"] = PierwszyBlad(wynik.Walidacja);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostOdwolajAsync(Guid id, CancellationToken anulowanie)
    {
        TempData[await uslugaKont.OdwolajZaproszenieAsync(id, anulowanie)
            ? "Komunikat"
            : "Ostrzezenie"] = "Zaproszenie odwołane.";

        return RedirectToPage();
    }

    public string OdnosnikZaproszenia(string kod) =>
        $"{Request.Scheme}://{Request.Host}{Url.Page("/Zaproszenie", new { kod })}";

    private static string PierwszyBlad(WynikWalidacji walidacja) =>
        walidacja.Problemy.FirstOrDefault(p => p.Poziom == PoziomProblemu.Blad)?.Komunikat
        ?? "Nie udało się wykonać operacji.";

    private async Task WczytajAsync(CancellationToken anulowanie)
    {
        Czlonkowie = await uslugaKont.CzlonkowieAsync(anulowanie);
        Zaproszenia = await uslugaKont.ZaproszeniaAsync(anulowanie);

        if (Tozsamosc.UzytkownikId(User) is Guid kto)
        {
            MojeCzlonkostwo = Czlonkowie.FirstOrDefault(c => c.UzytkownikId == kto)?.Id;
        }
    }
}
