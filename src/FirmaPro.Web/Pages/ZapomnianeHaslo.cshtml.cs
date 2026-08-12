using FirmaPro.Dane.Encje;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Prośba o odnośnik do ustawienia nowego hasła.
/// </summary>
/// <remarks>
/// Odnośnik wysyłany jest pocztą, więc bez skonfigurowanej poczty ekran
/// mówi wprost, że tędy droga nie prowadzi - zamiast obiecywać wiadomość,
/// która nigdy nie przyjdzie. Hasło można wtedy zmienić przez właściciela
/// firmy, który wystawi odnośnik na ekranie „Dostęp".
/// </remarks>
[AllowAnonymous]
public sealed class ZapomnianeHasloModel(
    UslugaKont uslugaKont, INadawcaPoczty poczta) : PageModel
{
    [BindProperty] public string Email { get; set; } = string.Empty;

    public bool Wyslano { get; private set; }

    public bool PocztaDziala => poczta.Dziala;

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        if (!poczta.Dziala)
        {
            return Page();
        }

        Uzytkownik? uzytkownik = await uslugaKont.ZnajdzPoAdresieAsync(Email, anulowanie);

        if (uzytkownik is not null)
        {
            ResetHasla reset = await uslugaKont.WystawResetAsync(uzytkownik.Id, anulowanie);
            string odnosnik = $"{Request.Scheme}://{Request.Host}" +
                              Url.Page("/NoweHaslo", new { kod = reset.Kod });

            await poczta.WyslijAsync(uzytkownik.Email, "Zmiana hasła w programie Firma PRO",
                $"""
                 Ktoś poprosił o zmianę hasła do konta {uzytkownik.Email}.

                 Aby ustawić nowe hasło, otwórz ten odnośnik:
                 {odnosnik}

                 Odnośnik działa raz i traci ważność po {UslugaKont.GodzinWaznosciResetu} godzinach.

                 Jeśli to nie Ty prosiłeś o zmianę, nie rób nic - hasło pozostanie
                 bez zmian.
                 """,
                anulowanie);
        }

        // Ta sama odpowiedź niezależnie od tego, czy konto istnieje. Inaczej
        // ekran stałby się sposobem na sprawdzanie, kto ma tu konto.
        Wyslano = true;
        return Page();
    }
}
