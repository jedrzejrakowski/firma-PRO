using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Wyniki szukania po całym programie.
/// </summary>
/// <remarks>
/// Ta sama usługa obsługuje dwa wejścia: pełny ekran wyników (adres da się
/// wysłać albo zapisać) oraz listę podpowiedzi doczytywaną pod polem bez
/// przeładowania strony. Podpowiedzi są skrótem, a nie osobnym mechanizmem -
/// dzięki temu nie da się dostać dwóch różnych odpowiedzi na to samo pytanie.
/// </remarks>
public sealed class SzukajModel(UslugaSzukania uslugaSzukania) : PageModel
{
    /// <summary>Ile trafień w grupie pokazuje lista podpowiedzi.</summary>
    private const int IleWPodpowiedziach = 4;

    /// <summary>Ile trafień w grupie pokazuje pełny ekran wyników.</summary>
    private const int IleNaEkranie = 25;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string Szukane { get; set; } = string.Empty;

    public WynikiSzukania Wyniki { get; private set; } = WynikiSzukania.Puste;

    public bool ZaKrotkie =>
        Szukane.Trim().Length is > 0 and < UslugaSzukania.NajkrotszeSzukane;

    public async Task OnGetAsync(CancellationToken anulowanie) =>
        Wyniki = await uslugaSzukania.SzukajAsync(Szukane, IleNaEkranie, anulowanie);

    /// <summary>Lista podpowiedzi jako gotowy kawałek strony.</summary>
    /// <remarks>
    /// Zwracamy gotowy HTML, a nie dane w JSON: układ listy jest już opisany
    /// w widoku, więc przeglądarka nie musi go składać drugi raz w skrypcie,
    /// a jedyna wersja prawdy o wyglądzie zostaje w Razorze.
    /// </remarks>
    public async Task<IActionResult> OnGetPodpowiedziAsync(CancellationToken anulowanie)
    {
        Wyniki = await uslugaSzukania.SzukajAsync(Szukane, IleWPodpowiedziach, anulowanie);

        return Partial("_Podpowiedzi", Wyniki);
    }
}
