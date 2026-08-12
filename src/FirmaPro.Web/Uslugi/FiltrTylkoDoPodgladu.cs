using FirmaPro.Dane.Encje;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Nie przepuszcza zmian danych, gdy użytkownik ma rolę podglądu.
/// </summary>
/// <remarks>
/// <para>
/// Ukrycie przycisków nie jest zabezpieczeniem - kto zna adres, wyśle
/// formularz i bez nich. Rola sprawdzana jest więc przy każdym żądaniu
/// zmieniającym dane, w jednym miejscu dla całego programu. Reguła jest
/// zamykająca: przepuszczamy to, co wyliczone, a nie to, co nie przyszło
/// nikomu do głowy zablokować.
/// </para>
/// <para>
/// Wyjątki to trzy czynności, które zmianą danych firmy nie są: zalogowanie,
/// wylogowanie i przejście do innej firmy.
/// </para>
/// </remarks>
public sealed class FiltrTylkoDoPodgladu : IAsyncPageFilter
{
    private static readonly string[] StronyDozwolone =
    [
        "/Logowanie",
        "/Wyloguj",
        "/PrzelaczFirme",
        "/Rejestracja",
        "/Zaproszenie"
    ];

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) =>
        Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context,
                                                  PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (CzyZablokowac(context))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }

    private static bool CzyZablokowac(PageHandlerExecutingContext context)
    {
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            return false;
        }

        if (!context.HttpContext.User.IsInRole(nameof(RolaWFirmie.Podglad)))
        {
            return false;
        }

        string? strona = (context.ActionDescriptor as
            Microsoft.AspNetCore.Mvc.RazorPages.CompiledPageActionDescriptor)?.ViewEnginePath;

        return !StronyDozwolone.Contains(strona, StringComparer.Ordinal);
    }
}
