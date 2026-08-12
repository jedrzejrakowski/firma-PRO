using System.Security.Claims;
using FirmaPro.Dane.Encje;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Ciasteczko logowania - kto pracuje, w jakiej firmie i z jaką rolą.
/// </summary>
/// <remarks>
/// Firma i rola pochodzą stąd, a nie z adresu strony ani z formularza.
/// Wszystkie miejsca, które logują użytkownika (logowanie, rejestracja,
/// przyjęcie zaproszenia, przełączenie firmy), muszą budować tożsamość tak
/// samo - inaczej gdzieś zabrakłoby oświadczenia i program przestałby
/// widzieć rolę albo firmę.
/// </remarks>
public static class Tozsamosc
{
    /// <summary>Nazwa oświadczenia z nazwą firmy - do pokazania w pasku.</summary>
    public const string NazwaFirmy = "nazwaFirmy";

    public static ClaimsPrincipal Zbuduj(Uzytkownik uzytkownik, CzlonkostwoWFirmie czlonkostwo)
    {
        ArgumentNullException.ThrowIfNull(uzytkownik);
        ArgumentNullException.ThrowIfNull(czlonkostwo);

        var oswiadczenia = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uzytkownik.Id.ToString()),
            new(ClaimTypes.Name, uzytkownik.ImieINazwisko ?? uzytkownik.Email),
            new(ClaimTypes.Email, uzytkownik.Email),
            new(KontekstFirmyZZadania.NazwaOswiadczenia, czlonkostwo.FirmaId.ToString()),
            new(NazwaFirmy, czlonkostwo.Firma?.Nazwa ?? string.Empty),
            new(ClaimTypes.Role, czlonkostwo.Rola.ToString())
        };

        return new ClaimsPrincipal(new ClaimsIdentity(
            oswiadczenia, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static Task ZalogujAsync(HttpContext kontekst, Uzytkownik uzytkownik,
                                    CzlonkostwoWFirmie czlonkostwo)
    {
        ArgumentNullException.ThrowIfNull(kontekst);

        return kontekst.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            Zbuduj(uzytkownik, czlonkostwo));
    }

    /// <summary>Identyfikator zalogowanego użytkownika.</summary>
    public static Guid? UzytkownikId(ClaimsPrincipal uzytkownik)
    {
        ArgumentNullException.ThrowIfNull(uzytkownik);

        return Guid.TryParse(uzytkownik.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id)
            ? id
            : null;
    }
}
