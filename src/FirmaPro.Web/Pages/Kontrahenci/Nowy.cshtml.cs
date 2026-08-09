using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Kontrahenci;

/// <summary>Dodawanie kontrahenta do kartoteki.</summary>
public sealed class NowyModel(FirmaProDbContext baza) : PageModel
{
    [BindProperty] public string Nazwa { get; set; } = string.Empty;
    [BindProperty] public string Nip { get; set; } = string.Empty;
    [BindProperty] public string KodKraju { get; set; } = "PL";
    [BindProperty] public string AdresLinia1 { get; set; } = string.Empty;
    [BindProperty] public string AdresLinia2 { get; set; } = string.Empty;
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Telefon { get; set; } = string.Empty;

    public List<string> Bledy { get; } = [];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        string nip = new((Nip ?? string.Empty).Where(char.IsDigit).ToArray());

        if (string.IsNullOrWhiteSpace(Nazwa))
        {
            Bledy.Add("Nazwa jest wymagana.");
        }

        // NIP jest nieobowiązkowy (osoba prywatna), ale gdy jest podany,
        // musi mieć poprawną sumę kontrolną - literówkę lepiej wychwycić
        // teraz niż przy odrzuceniu faktury przez KSeF.
        if (nip.Length > 0 && !Walidator.NipPoprawny(nip))
        {
            Bledy.Add($"Numer NIP „{Nip}” ma błędną sumę kontrolną.");
        }

        if ((KodKraju ?? string.Empty).Length != 2)
        {
            Bledy.Add("Kod kraju musi mieć dokładnie dwie litery, np. PL.");
        }

        if (Bledy.Count > 0)
        {
            return Page();
        }

        baza.Kontrahenci.Add(new Kontrahent
        {
            Nazwa = Nazwa.Trim(),
            Nip = nip,
            KodKraju = KodKraju!.ToUpperInvariant(),
            AdresLinia1 = (AdresLinia1 ?? string.Empty).Trim(),
            AdresLinia2 = string.IsNullOrWhiteSpace(AdresLinia2) ? null : AdresLinia2.Trim(),
            Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
            Telefon = string.IsNullOrWhiteSpace(Telefon) ? null : Telefon.Trim()
        });

        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] = $"Dodano kontrahenta „{Nazwa.Trim()}”.";
        return RedirectToPage("Index");
    }
}
