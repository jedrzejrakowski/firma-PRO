using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>
/// Wystawianie faktury korygującej.
/// </summary>
/// <remarks>
/// Formularz pokazuje pozycje faktury pierwotnej i pozwala je poprawić.
/// Zapisany dokument niesie obie wersje, a różnica między nimi trafia do
/// rejestru VAT - tak samo jak w pliku wysyłanym do KSeF.
/// </remarks>
public sealed class KorektaModel(
    FirmaProDbContext baza,
    UslugaFaktur uslugaFaktur) : PageModel
{
    public FakturaSprzedazy Korygowana { get; private set; } = null!;

    [BindProperty] public Guid KorygowanaId { get; set; }
    [BindProperty] public DateOnly DataWystawienia { get; set; }
    [BindProperty] public string PrzyczynaKorekty { get; set; } = string.Empty;

    [BindProperty]
    public TypKorektyVat TypKorekty { get; set; } = TypKorektyVat.WDacieKorekty;

    [BindProperty] public List<WierszPozycji> Pozycje { get; set; } = [];

    public List<string> Bledy { get; } = [];

    public static IReadOnlyList<string> KodyGtu => NowaModel.KodyGtu;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        Korygowana = faktura;
        KorygowanaId = faktura.Id;
        DataWystawienia = DateOnly.FromDateTime(DateTime.Today);

        // Formularz startuje od stanu pierwotnego - użytkownik poprawia to,
        // co się zmieniło, zamiast wpisywać całą fakturę od nowa.
        Pozycje = faktura.Pozycje
            .Where(p => !p.StanPrzed)
            .OrderBy(p => p.NrWiersza)
            .Select(p => new WierszPozycji
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                KodStawki = p.KodStawki,
                Gtu = p.Gtu
            })
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(KorygowanaId, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        Korygowana = faktura;

        List<WierszPozycji> wypelnione = Pozycje.Where(p => !p.CzyPusty).ToList();
        if (wypelnione.Count == 0)
        {
            Bledy.Add("Korekta musi mieć przynajmniej jedną pozycję z nazwą.");
            return Page();
        }

        WynikWystawienia wynik = await uslugaFaktur.WystawKorekteAsync(
            KorygowanaId, DataWystawienia, PrzyczynaKorekty, TypKorekty,
            wypelnione.Select(p => (p.Nazwa, p.Jednostka, p.Ilosc, p.CenaNetto,
                                    p.KodStawki, p.Gtu)).ToList(),
            anulowanie);

        if (!wynik.Udalo)
        {
            Bledy.AddRange(wynik.Walidacja.Problemy
                .Where(p => p.Poziom == PoziomProblemu.Blad)
                .Select(p => $"{p.Pole}: {p.Komunikat}"));

            return Page();
        }

        TempData["Komunikat"] =
            $"Wystawiono korektę {wynik.Faktura!.Numer} na kwotę " +
            $"{Kwoty.NaTekst(wynik.Faktura.RazemBrutto)} {wynik.Faktura.Waluta}.";

        return RedirectToPage("Szczegoly", new { id = wynik.Faktura.Id });
    }

    private Task<FakturaSprzedazy?> WczytajAsync(Guid id, CancellationToken anulowanie) =>
        baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .FirstOrDefaultAsync(f => f.Id == id, anulowanie);
}
