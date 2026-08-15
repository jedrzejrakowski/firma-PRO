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
    UslugaWysylkiFaktur uslugaWysylki,
    UslugaPlatnosci uslugaPlatnosci) : PageModel
{
    public FakturaSprzedazy Faktura { get; private set; } = null!;

    /// <summary>Adres, pod który poleci wiadomość - domyślnie z kartoteki.</summary>
    [BindProperty] public string Adres { get; set; } = string.Empty;

    [BindProperty] public string? Wiadomosc { get; set; }

    [BindProperty] public bool DolaczXml { get; set; }

    public IReadOnlyList<WyslanieFaktury> Wysylki { get; private set; } = [];

    public bool PocztaDziala => uslugaWysylki.PocztaDziala;

    public WynikWalidacji WalidacjaWysylki { get; private set; } = new();

    [BindProperty] public decimal KwotaWplaty { get; set; }
    [BindProperty] public DateOnly DataWplaty { get; set; }
    [BindProperty] public string? UwagiWplaty { get; set; }

    public IReadOnlyList<Platnosc> Wplaty { get; private set; } = [];
    public Rozliczenie Rozliczenie { get; private set; } = new(0, 0, null, default);

    public WynikWalidacji WalidacjaWplaty { get; private set; } = new();

    /// <summary>Czy ta faktura zaliczkowa została już rozliczona końcową.</summary>
    public bool ZaliczkaRozliczona { get; private set; }

    /// <summary>Suma zaliczek zafakturowanych przed tą fakturą końcową.</summary>
    public decimal SumaZaliczek =>
        Kwoty.Zaokraglij(Faktura.RozliczoneZaliczki.Sum(z => z.Brutto));

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

    /// <summary>Zapisuje wpłatę do faktury.</summary>
    public async Task<IActionResult> OnPostWplataAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        WynikKonta<Platnosc> wynik = await uslugaPlatnosci.DodajWplateAsync(
            id, KwotaWplaty, DataWplaty, UwagiWplaty, anulowanie);

        if (!wynik.Udalo)
        {
            Faktura = faktura;
            WalidacjaWplaty = wynik.Walidacja;
            await WczytajDodatkiAsync(id, anulowanie);
            return Page();
        }

        TempData["Komunikat"] = $"Zapisano wpłatę {Kwoty.NaTekst(wynik.Dane!.Kwota)}.";
        return RedirectToPage("Szczegoly", new { id });
    }

    public async Task<IActionResult> OnPostUsunWplateAsync(
        Guid id, Guid wplataId, CancellationToken anulowanie)
    {
        TempData[await uslugaPlatnosci.UsunWplateAsync(wplataId, anulowanie)
            ? "Komunikat"
            : "Ostrzezenie"] = "Usunięto wpłatę.";

        return RedirectToPage("Szczegoly", new { id });
    }

    /// <summary>Wysyła kontrahentowi przypomnienie o zapłacie.</summary>
    public async Task<IActionResult> OnPostPrzypomnijAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        WynikKonta<WyslanieFaktury> wynik = await uslugaPlatnosci.WyslijPrzypomnienieAsync(
            id, Adres, Tozsamosc.UzytkownikId(User), anulowanie);

        if (!wynik.Udalo)
        {
            Faktura = faktura;
            WalidacjaWysylki = wynik.Walidacja;
            await WczytajDodatkiAsync(id, anulowanie);
            return Page();
        }

        TempData["Komunikat"] = $"Przypomnienie wysłane na adres {wynik.Dane!.Adres}.";
        return RedirectToPage("Szczegoly", new { id });
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
        await WczytajDodatkiAsync(id, anulowanie);

    private async Task WczytajDodatkiAsync(Guid id, CancellationToken anulowanie)
    {
        Wysylki = await uslugaWysylki.HistoriaAsync(id, anulowanie);
        Wplaty = await uslugaPlatnosci.WplatyAsync(id, anulowanie);
        Rozliczenie = await uslugaPlatnosci.RozliczenieAsync(id, anulowanie);

        ZaliczkaRozliczona = Faktura.Rodzaj == RodzajFaktury.Zaliczkowa
            && await baza.RozliczoneZaliczki.AnyAsync(z => z.ZaliczkowaId == id, anulowanie);

        // Pola formularza podpowiadają najczęstszy przypadek: całą resztę
        // należności wpłaconą dzisiaj.
        if (KwotaWplaty == 0)
        {
            KwotaWplaty = Rozliczenie.Pozostalo > 0 ? Rozliczenie.Pozostalo : 0;
        }

        if (DataWplaty == default)
        {
            DataWplaty = DateOnly.FromDateTime(DateTime.Today);
        }
    }

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

    /// <summary>
    /// Pobiera urzędowe poświadczenie odbioru, gdy nie udało się przy wysyłce.
    /// </summary>
    /// <remarks>
    /// Poświadczenie powstaje z opóźnieniem po zamknięciu sesji, więc pierwsza
    /// próba - ta zaraz po wysłaniu faktury - bywa za wczesna. Przycisk pozwala
    /// spróbować ponownie, zamiast zostawiać użytkownika bez dowodu doręczenia.
    /// </remarks>
    public async Task<IActionResult> OnPostUpoAsync(Guid id, CancellationToken anulowanie)
    {
        WynikUpo wynik = await uslugaFaktur.PobierzUpoAsync(id, anulowanie);

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

    /// <summary>
    /// Udostępnia zapisane poświadczenie odbioru jako plik.
    /// </summary>
    /// <remarks>
    /// Oddajemy dokładnie to, co wydał KSeF. Poświadczenie jest podpisane
    /// elektronicznie, więc każda zmiana treści - choćby zmiana wcięć -
    /// unieważniłaby podpis i dokument przestałby być dowodem.
    /// </remarks>
    public async Task<IActionResult> OnGetUpoAsync(Guid id, CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);

        if (faktura is null)
        {
            return NotFound();
        }

        if (faktura.UpoXml is not { Length: > 0 } upo)
        {
            return NotFound();
        }

        return File(System.Text.Encoding.UTF8.GetBytes(upo), "application/xml",
            "UPO_" + BezpiecznaNazwa(faktura.Numer) + ".xml");
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
    public async Task<IActionResult> OnGetPdfAsync(Guid id, bool duplikat,
                                                   CancellationToken anulowanie)
    {
        FakturaSprzedazy? faktura = await WczytajAsync(id, anulowanie);
        if (faktura is null)
        {
            return NotFound();
        }

        byte[] pdf = await uslugaFaktur.ZbudujPdfAsync(id, duplikat, anulowanie);

        string nazwa = BezpiecznaNazwa(faktura.Numer)
                       + (duplikat ? "_duplikat" : string.Empty) + ".pdf";

        return File(pdf, "application/pdf", nazwa);
    }

    private Task<FakturaSprzedazy?> WczytajAsync(Guid id, CancellationToken anulowanie) =>
        baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .Include(f => f.RozliczoneZaliczki)
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
