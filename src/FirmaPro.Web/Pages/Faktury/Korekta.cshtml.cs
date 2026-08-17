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

    // --- korekta danych sprzedawcy -----------------------------------------

    /// <summary>
    /// Czy korekta poprawia dane samego sprzedawcy.
    /// </summary>
    /// <remarks>
    /// Osobny znacznik, bo to inny przypadek niż korekta kwot: art. 106j
    /// ust. 2 pkt 3 wymaga wtedy podania pełnych danych w brzmieniu z faktury
    /// korygowanej, żeby widać było, co właściwie zostało poprawione.
    /// </remarks>
    [BindProperty] public bool KorygujDaneSprzedawcy { get; set; }

    [BindProperty] public string? SprzedawcaPrzedNazwa { get; set; }
    [BindProperty] public string? SprzedawcaPrzedAdres { get; set; }

    /// <summary>Czy korekta poprawia dane nabywcy.</summary>
    [BindProperty] public bool KorygujDaneNabywcy { get; set; }

    [BindProperty] public string? NabywcaPrzedNazwa { get; set; }
    [BindProperty] public string? NabywcaPrzedAdres { get; set; }

    /// <summary>Dane nabywcy sprzed korekty zbudowane z pól formularza.</summary>
    /// <remarks>
    /// Numer NIP bierzemy z faktury korygowanej - tego numeru korekta danych
    /// nie zmienia, a pole do jego wpisania byłoby zaproszeniem do pomyłki
    /// nie do naprawienia inaczej niż korektą do zera.
    /// </remarks>
    private Podmiot? NabywcaPrzedKorekta(FakturaSprzedazy korygowana) =>
        KorygujDaneNabywcy && !string.IsNullOrWhiteSpace(NabywcaPrzedNazwa)
            ? new Podmiot
            {
                Nazwa = NabywcaPrzedNazwa.Trim(),
                Nip = korygowana.NabywcaNip,
                Adres = new Adres { Linia1 = NabywcaPrzedAdres?.Trim() ?? string.Empty }
            }
            : null;

    /// <summary>Dane sprzedawcy sprzed korekty zbudowane z pól formularza.</summary>
    /// <remarks>
    /// Numer NIP bierzemy z faktury korygowanej, a nie z formularza. Błędnego
    /// numeru nie poprawia się korektą danych - trzeba wystawić korektę do zera
    /// i nową fakturę - więc pole do jego wpisania byłoby zaproszeniem
    /// do pomyłki nie do naprawienia.
    /// </remarks>
    private Podmiot? SprzedawcaPrzedKorekta(FakturaSprzedazy korygowana) =>
        KorygujDaneSprzedawcy && !string.IsNullOrWhiteSpace(SprzedawcaPrzedNazwa)
            ? new Podmiot
            {
                Nazwa = SprzedawcaPrzedNazwa.Trim(),
                Nip = korygowana.Firma?.Nip ?? string.Empty,
                Adres = new Adres { Linia1 = SprzedawcaPrzedAdres?.Trim() ?? string.Empty }
            }
            : null;

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
            SprzedawcaPrzedKorekta(faktura),
            NabywcaPrzedKorekta(faktura),
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
            .Include(f => f.Firma)
            .FirstOrDefaultAsync(f => f.Id == id, anulowanie);
}
