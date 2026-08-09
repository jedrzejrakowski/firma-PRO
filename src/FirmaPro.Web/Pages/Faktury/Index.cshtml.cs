using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>Lista faktur sprzedaży bieżącej firmy.</summary>
public sealed class IndexModel(FirmaProDbContext baza) : PageModel
{
    public IReadOnlyList<FakturaSprzedazy> Faktury { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken anulowanie)
    {
        Faktury = await baza.FakturySprzedazy
            .OrderByDescending(f => f.DataWystawienia)
            .ThenByDescending(f => f.UtworzonoUtc)
            .Take(200)
            .ToListAsync(anulowanie);
    }

    /// <summary>Opis statusu w języku, którym posługuje się użytkownik.</summary>
    public static string OpisStatusu(StatusKsef status) => status switch
    {
        StatusKsef.Robocza => "Robocza",
        StatusKsef.Wyslana => "Wysłana",
        StatusKsef.Przyjeta => "Przyjęta",
        StatusKsef.Odrzucona => "Odrzucona",
        _ => status.ToString()
    };
}
