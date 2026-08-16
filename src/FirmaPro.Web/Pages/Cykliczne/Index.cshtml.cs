using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Cykliczne;

/// <summary>
/// Wzorce faktur wystawianych cyklicznie.
/// </summary>
/// <remarks>
/// Na górze ekranu stoi to, co czeka na wystawienie - bo po to się tu
/// zagląda. Lista wszystkich wzorców jest niżej, bo ogląda się ją rzadko:
/// raz przy zakładaniu i potem przy zmianie ceny.
/// </remarks>
public sealed class IndexModel(UslugaFakturCyklicznych usluga) : PageModel
{
    public IReadOnlyList<DoWystawienia> Czeka { get; private set; } = [];

    public IReadOnlyList<WzorzecCykliczny> Wzorce { get; private set; } = [];

    public static string OpisRytmu(RytmFaktury rytm) => Cyklicznosc.Opis(rytm);

    public static string OpisDnia(int dzien) => dzien == Cyklicznosc.OstatniDzien
        ? "ostatni dzień miesiąca"
        : $"{dzien}. dzień miesiąca";

    public async Task OnGetAsync(CancellationToken anulowanie)
    {
        Czeka = await usluga.DoWystawieniaAsync(anulowanie);
        Wzorce = await usluga.ListaAsync(anulowanie);
    }

    public async Task<IActionResult> OnPostWystawAsync(Guid id, CancellationToken anulowanie)
    {
        WynikWystawienia wynik = await usluga.WystawAsync(id, anulowanie);

        if (wynik.Udalo)
        {
            TempData["Komunikat"] =
                $"Wystawiono fakturę {wynik.Faktura!.Numer} na kwotę " +
                $"{Kwoty.NaTekst(wynik.Faktura.RazemBrutto)} zł.";
        }
        else
        {
            TempData["Ostrzezenie"] = string.Join("; ",
                wynik.Walidacja.Problemy
                    .Where(p => p.Poziom == PoziomProblemu.Blad)
                    .Select(p => p.Komunikat));
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPrzelaczAsync(Guid id, CancellationToken anulowanie)
    {
        if (!await usluga.PrzelaczAktywnoscAsync(id, anulowanie))
        {
            TempData["Ostrzezenie"] = "Nie znaleziono wskazanego wzorca.";
        }

        return RedirectToPage();
    }
}
