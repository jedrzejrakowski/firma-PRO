using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Kto jest firmie winien pieniądze.
/// </summary>
/// <remarks>
/// Osobny ekran, a nie filtr na liście faktur: lista faktur odpowiada na
/// pytanie „co wystawiliśmy", a to jest pytanie „na co czekamy". Kolejność
/// jest tu inna - według terminu zapłaty, a nie daty wystawienia.
/// </remarks>
public sealed class NaleznosciModel(UslugaPlatnosci uslugaPlatnosci) : PageModel
{
    public PodsumowanieNaleznosci Podsumowanie { get; private set; } =
        new([], 0, 0);

    public async Task OnGetAsync(CancellationToken anulowanie) =>
        Podsumowanie = await uslugaPlatnosci.NaleznosciAsync(anulowanie);
}
