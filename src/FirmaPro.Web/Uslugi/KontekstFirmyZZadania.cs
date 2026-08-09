using System.Security.Claims;
using FirmaPro.Dane;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Ustala firmę, w której kontekście działa bieżące żądanie.
/// </summary>
/// <remarks>
/// Identyfikator firmy pochodzi z oświadczenia zapisanego w ciasteczku
/// logowania, a nie z parametru adresu. To istotne dla bezpieczeństwa:
/// gdyby firma była brana z adresu, wystarczyłoby podmienić w nim
/// identyfikator, żeby zajrzeć do cudzych dokumentów.
/// </remarks>
public sealed class KontekstFirmyZZadania(IHttpContextAccessor dostepDoZadania) : IKontekstFirmy
{
    /// <summary>Nazwa oświadczenia przechowującego wybraną firmę.</summary>
    public const string NazwaOswiadczenia = "firma";

    public Guid? FirmaId
    {
        get
        {
            string? wartosc = dostepDoZadania.HttpContext?.User
                .FindFirstValue(NazwaOswiadczenia);

            return Guid.TryParse(wartosc, out Guid identyfikator) ? identyfikator : null;
        }
    }
}
