using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>Podgląd faktury wraz z wysyłką do KSeF.</summary>
public sealed class SzczegolyModel(
    FirmaProDbContext baza,
    UslugaFaktur uslugaFaktur,
    UslugaWysylkiFaktur uslugaWysylki) : PageModel
{
    public FakturaSprzedazy Faktura { get; private set; } = null!;

    /// <summary>Adres, pod który poleci wiadomość - domyślnie z kartoteki.</summary>
    [BindProperty] public string Adres { get; set; } = string.Empty;

    [BindProperty] public string? Wiadomosc { get; set; }

    [BindProperty] public bool DolaczXml { get; set; }

    public IReadOnlyList<WyslanieFaktury> Wysylki { get; private set; } = [];

    public bool PocztaDziala => uslugaWysylki.PocztaDziala;

    public WynikWalidacji WalidacjaWysylki { get; private set; } = new();

    /// <summary>
    /// Czy u góry strony widać już komunikat z ostatniej operacji.
    /// </summary>
    /// <remarks>
    /// Zaraz po wysyłce ten sam tekst jest i w pasku na górze, i w polu uwag
    /// zapisanym przy fakturze. Powtórzony dwa razy wygląda na usterkę, więc
    /// przy świeżym komunikacie pomijamy kopię przy dokumencie.
    /// </remarks>
    public bool SwiezyKomunikat { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        // Peek nie zużywa wpisu - wyświetleniem zajmuje się wspólny układ.
        SwiezyKomunikat = TempData.Peek("Komunikat") is not null
                       || TempData.Peek("Ostrzezenie") is not null;

        Faktura = faktura;
        await WczytajWysylkiAsync(id, anulowanie);

        // Adres z kartoteki kontrahenta to najczęstszy wybór, ale zostaje
        // do poprawienia: faktury bywają wysyłane do księgowości klienta,
        // a nie na adres wpisany w kartotece.
        Adres = await uslugaWysylki.AdresKontrahentaAsync(id, anulowanie) ?? string.Empty;

        return Page();
    }

    /// <summary>Wysyła fakturę kontrahentowi pocztą.</summary>
    public async Task<IActionResult> OnPostPocztaAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        WynikKonta<WyslanieFaktury> wynik = await uslugaWysylki.WyslijAsync(
            id, Adres, Wiadomosc, DolaczXml, Tozsamosc.UzytkownikId(User), anulowanie);

        if (!wynik.Udalo)
        {
            Faktura = faktura;
            WalidacjaWysylki = wynik.Walidacja;
            await WczytajWysylkiAsync(id, anulowanie);
            return Page();
        }

        TempData["Komunikat"] = $"Faktura wysłana na adres {wynik.Dane!.Adres}.";
        return RedirectToPage("Szczegoly", new { id });
    }

    private async Task WczytajWysylkiAsync(Guid id, CancellationToken anulowanie) =>
        Wysylki = await uslugaWysylki.HistoriaAsync(id, anulowanie);

    public async Task<IActionResult> OnPostWyslijAsync(Guid id, CancellationToken anulowanie)
    {
        WynikWysylki wynik = await uslugaFaktur.WyslijAsync(id, anulowanie);

        if (wynik.Udalo)
        {
            TempData["Komunikat"] = wynik.Komunikat;
        }
        else
        {
            TempData["Ostrzezenie"] = wynik.Komunikat;
        }

        return RedirectToPage("Szczegoly", new { id });
    }

    /// <summary>Udostępnia plik XML faktury - do kontroli i archiwum.</summary>
    public async Task<IActionResult> OnGetXmlAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        byte[] xml = await uslugaFaktur.ZbudujXmlAsync(id, anulowanie);
        string nazwa = BezpiecznaNazwa(faktura.Numer) + ".xml";

        return File(xml, "application/xml", nazwa);
    }

    /// <summary>Udostępnia wizualizację faktury w PDF - do wysłania nabywcy.</summary>
    public async Task<IActionResult> OnGetPdfAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        byte[] pdf = await uslugaFaktur.ZbudujPdfAsync(id, anulowanie);
        string nazwa = BezpiecznaNazwa(faktura.Numer) + ".pdf";

        return File(pdf, "application/pdf", nazwa);
    }

    private Task<FakturaSprzedazy?> WczytajAsync(Guid id, CancellationToken anulowanie) =>
        baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .FirstOrDefaultAsync(f => f.Id == id, anulowanie);

    /// <summary>Zamienia numer faktury na nazwę pliku bez znaków specjalnych.</summary>
    private static string BezpiecznaNazwa(string numer) =>
        new((numer ?? "faktura")
            .Select(z => char.IsLetterOrDigit(z) || z is '-' or '_' ? z : '_')
            .ToArray());

    /// <summary>Opis stawki podatku pokazywany na ekranie.</summary>
    public static string OpisStawki(string kod) =>
        StawkaVat.TryZKodu(kod, out StawkaVat? stawka) ? stawka.Opis : kod;
}
