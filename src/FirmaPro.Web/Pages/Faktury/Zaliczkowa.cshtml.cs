using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>
/// Wystawianie faktury zaliczkowej.
/// </summary>
/// <remarks>
/// Formularz różni się od zwykłej faktury tym, co opisuje: pozycje są tu
/// pozycjami <b>zamówienia</b>, a kwota to otrzymana wpłata. Wiersze samej
/// faktury program wylicza sam - jako rozbicie wpłaty na stawki podatku.
/// </remarks>
public sealed class ZaliczkowaModel(FirmaProDbContext baza, UslugaFaktur uslugaFaktur) : PageModel
{
    [BindProperty] public Guid KontrahentId { get; set; }
    [BindProperty] public DateOnly DataWystawienia { get; set; }
    [BindProperty] public decimal KwotaZaliczki { get; set; }
    [BindProperty] public FormaPlatnosci? FormaPlatnosci { get; set; }
    [BindProperty] public List<WierszPozycji> Zamowienie { get; set; } = [];

    public IReadOnlyList<Kontrahent> Kontrahenci { get; private set; } = [];
    public List<string> Bledy { get; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        if (Kontrahenci.Count == 0)
        {
            TempData["Ostrzezenie"] =
                "Zanim wystawisz fakturę, dodaj przynajmniej jednego kontrahenta.";
            return RedirectToPage("/Kontrahenci/Nowy");
        }

        DataWystawienia = DateOnly.FromDateTime(DateTime.Today);
        FormaPlatnosci = Domena.FormaPlatnosci.Przelew;
        Zamowienie = [new WierszPozycji()];

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        List<WierszPozycji> wypelnione = Zamowienie.Where(p => !p.CzyPusty).ToList();

        if (wypelnione.Count == 0)
        {
            Bledy.Add("Zamówienie musi mieć przynajmniej jedną pozycję z nazwą.");
            ZapewnijWiersz();
            return Page();
        }

        WynikWystawienia wynik = await uslugaFaktur.WystawZaliczkowaAsync(
            KontrahentId,
            DataWystawienia,
            KwotaZaliczki,
            FormaPlatnosci,
            [.. wypelnione.Select(p => (p.Nazwa, p.Jednostka, p.Ilosc, p.CenaNetto,
                                        p.KodStawki, p.Gtu))],
            anulowanie);

        if (!wynik.Udalo)
        {
            Bledy.AddRange(wynik.Walidacja.Problemy
                .Where(p => p.Poziom == PoziomProblemu.Blad)
                .Select(p => $"{p.Pole}: {p.Komunikat}"));

            ZapewnijWiersz();
            return Page();
        }

        TempData["Komunikat"] =
            $"Wystawiono fakturę zaliczkową {wynik.Faktura!.Numer} na kwotę " +
            $"{Kwoty.NaTekst(wynik.Faktura.RazemBrutto)} {wynik.Faktura.Waluta}.";

        return RedirectToPage("Szczegoly", new { id = wynik.Faktura.Id });
    }

    /// <summary>Podgląd rozbicia wpłaty na stawki - liczony na bieżąco.</summary>
    public IReadOnlyList<CzescZaliczki> Podglad()
    {
        List<PozycjaZamowienia> pozycje = [.. Zamowienie
            .Where(p => !p.CzyPusty)
            .Select(p => new PozycjaZamowienia
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                Stawka = StawkaVat.TryZKodu(p.KodStawki, out StawkaVat? stawka)
                    ? stawka
                    : StawkaVat.Vat23
            })];

        return Zaliczka.Rozbij(pozycje, KwotaZaliczki);
    }

    private async Task WczytajListyAsync(CancellationToken anulowanie) =>
        Kontrahenci = await baza.Kontrahenci
            .Where(k => k.Aktywny)
            .OrderBy(k => k.Nazwa)
            .ToListAsync(anulowanie);

    private void ZapewnijWiersz()
    {
        if (Zamowienie.Count == 0)
        {
            Zamowienie.Add(new WierszPozycji());
        }
    }
}
