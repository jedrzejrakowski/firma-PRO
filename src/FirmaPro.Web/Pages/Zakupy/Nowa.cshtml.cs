using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Zakupy;

/// <summary>Wiersz formularza z kwotami w jednej stawce.</summary>
public sealed class WierszKwot
{
    public string KodStawki { get; set; } = StawkaVat.Vat23.Kod;
    public decimal Netto { get; set; }
    public decimal Vat { get; set; }

    public bool CzyPusty => Netto == 0 && Vat == 0;
}

/// <summary>
/// Wprowadzanie faktury otrzymanej od dostawcy.
/// </summary>
/// <remarks>
/// Formularz podpowiada kwotę podatku i datę ujęcia, ale niczego nie
/// narzuca - na cudzej fakturze zdarzają się zaokrąglenia i przypadki
/// szczególne, których program nie ma prawa nadpisywać.
/// </remarks>
public sealed class NowaModel(
    FirmaProDbContext baza,
    UslugaZakupow uslugaZakupow) : PageModel
{
    [BindProperty] public string Numer { get; set; } = string.Empty;
    [BindProperty] public DateOnly DataWystawienia { get; set; }
    [BindProperty] public DateOnly DataWplywu { get; set; }
    [BindProperty] public DateOnly? DataObowiazkuPodatkowego { get; set; }
    [BindProperty] public DateOnly? DataUjecia { get; set; }

    [BindProperty] public Guid? KontrahentId { get; set; }
    [BindProperty] public string SprzedawcaNazwa { get; set; } = string.Empty;
    [BindProperty] public string SprzedawcaNip { get; set; } = string.Empty;

    [BindProperty] public RodzajZakupu Rodzaj { get; set; } = RodzajZakupu.TowaryIUslugi;
    [BindProperty] public bool Odliczany { get; set; } = true;
    [BindProperty] public string Uwagi { get; set; } = string.Empty;

    [BindProperty] public List<WierszKwot> Kwoty { get; set; } = [];

    public IReadOnlyList<Kontrahent> Kontrahenci { get; private set; } = [];
    public List<string> Bledy { get; } = [];
    public List<string> Ostrzezenia { get; } = [];

    /// <summary>Stawki wybierane z listy - bez tych, które nie wystąpią na zakupie.</summary>
    public static IReadOnlyList<StawkaVat> DostepneStawki { get; } =
        StawkaVat.Wszystkie
            .Where(s => s.Kod is not ("0 WDT" or "0 EX" or "np II"))
            .ToList();

    public async Task OnGetAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        DateOnly dzisiaj = DateOnly.FromDateTime(DateTime.Today);
        DataWystawienia = dzisiaj;
        DataWplywu = dzisiaj;

        Kwoty = [new WierszKwot()];
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        // Gdy sprzedawcę wybrano z kartoteki, jego dane przepisujemy stamtąd -
        // ręczne wpisywanie ich drugi raz to tylko okazja do literówki.
        if (KontrahentId is Guid id)
        {
            Kontrahent? kontrahent = Kontrahenci.FirstOrDefault(k => k.Id == id);
            if (kontrahent is not null)
            {
                SprzedawcaNazwa = kontrahent.Nazwa;
                SprzedawcaNip = kontrahent.Nip;
            }
        }

        WynikZapisuZakupu wynik = await uslugaZakupow.ZapiszAsync(
            Numer, DataWystawienia, DataWplywu, DataObowiazkuPodatkowego, DataUjecia,
            KontrahentId, SprzedawcaNazwa, SprzedawcaNip, Rodzaj, Odliczany,
            Kwoty.Where(k => !k.CzyPusty)
                 .Select(k => new KwotaZakupu(k.KodStawki, k.Netto, k.Vat))
                 .ToList(),
            Uwagi, anulowanie);

        if (!wynik.Udalo)
        {
            Bledy.AddRange(wynik.Walidacja.Problemy
                .Where(p => p.Poziom == PoziomProblemu.Blad)
                .Select(p => p.Komunikat));

            Ostrzezenia.AddRange(wynik.Walidacja.Problemy
                .Where(p => p.Poziom == PoziomProblemu.Ostrzezenie)
                .Select(p => p.Komunikat));

            ZapewnijWiersz();
            return Page();
        }

        string komunikat = $"Dodano fakturę {wynik.Faktura!.Numer} do rejestru zakupów.";

        // Ostrzeżenia nie blokują zapisu, ale użytkownik ma prawo je zobaczyć.
        if (wynik.Walidacja.Problemy.Count > 0)
        {
            komunikat += " Zwróć uwagę: " + string.Join("; ",
                wynik.Walidacja.Problemy.Select(p => p.Komunikat)) + ".";
        }

        TempData["Komunikat"] = komunikat;
        return RedirectToPage("Index");
    }

    private async Task WczytajListyAsync(CancellationToken anulowanie) =>
        Kontrahenci = await baza.Kontrahenci
            .Where(k => k.Aktywny)
            .OrderBy(k => k.Nazwa)
            .ToListAsync(anulowanie);

    private void ZapewnijWiersz()
    {
        if (Kwoty.Count == 0)
        {
            Kwoty.Add(new WierszKwot());
        }
    }
}
