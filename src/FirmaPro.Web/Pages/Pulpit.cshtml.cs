using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Pierwszy ekran po zalogowaniu.
/// </summary>
/// <remarks>
/// Program otwierał się dotąd na liście faktur, czyli na archiwum. Właściciel
/// firmy siada do programu z innym pytaniem: ile mi wiszą, co sprzedałem i co
/// muszę dziś zrobić. Pulpit odpowiada na te trzy pytania bez jednego
/// kliknięcia.
/// </remarks>
public sealed class PulpitModel(UslugaPulpitu uslugaPulpitu) : PageModel
{
    public DanePulpitu Dane { get; private set; } =
        new(0, 0, 0, 0, 0, [], [], []);

    public async Task OnGetAsync(CancellationToken anulowanie) =>
        Dane = await uslugaPulpitu.ZbudujAsync(anulowanie);
}
