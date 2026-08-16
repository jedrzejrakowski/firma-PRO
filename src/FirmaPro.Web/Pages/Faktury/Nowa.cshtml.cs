using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Faktury;

/// <summary>Pojedynczy wiersz formularza pozycji faktury.</summary>
public sealed class WierszPozycji
{
    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal Ilosc { get; set; } = 1m;
    public decimal CenaNetto { get; set; }
    public string KodStawki { get; set; } = StawkaVat.Vat23.Kod;
    public string? Gtu { get; set; }

    /// <summary>Wiersz uznajemy za pusty, gdy nie ma nazwy - taki pomijamy.</summary>
    public bool CzyPusty => string.IsNullOrWhiteSpace(Nazwa);
}

/// <summary>
/// Pojedynczy wiersz formularza podmiotów trzecich.
/// </summary>
/// <remarks>
/// Podmiot trzeci to odbiorca będący oddziałem nabywcy, faktor, dodatkowy
/// nabywca albo jednostka podrzędna samorządu. Struktura FA(3) przewiduje na
/// nie osobną sekcję, a KSeF właśnie po nich udostępnia fakturę komuś innemu
/// niż nabywca.
/// </remarks>
public sealed class WierszPodmiotu
{
    public string Nazwa { get; set; } = string.Empty;
    public string? Nip { get; set; }
    public string? AdresLinia1 { get; set; }
    public string? AdresLinia2 { get; set; }

    /// <summary>Rola z listy; puste, gdy opisywana jest własnymi słowami.</summary>
    public RolaPodmiotu? Rola { get; set; }

    public string? OpisRoli { get; set; }
    public decimal? Udzial { get; set; }
    public string? NrKlienta { get; set; }

    /// <summary>Wiersz bez nazwy uznajemy za pusty - taki pomijamy.</summary>
    public bool CzyPusty => string.IsNullOrWhiteSpace(Nazwa);

    public PodmiotInny NaModel() => new()
    {
        Dane = new Podmiot
        {
            Nazwa = Nazwa.Trim(),
            Nip = Nip?.Trim() ?? string.Empty,
            Adres = new Adres
            {
                Linia1 = AdresLinia1?.Trim() ?? string.Empty,
                Linia2 = string.IsNullOrWhiteSpace(AdresLinia2) ? null : AdresLinia2.Trim()
            }
        },
        Rola = Rola,
        OpisRoli = string.IsNullOrWhiteSpace(OpisRoli) ? null : OpisRoli.Trim(),
        Udzial = Udzial,
        NrKlienta = string.IsNullOrWhiteSpace(NrKlienta) ? null : NrKlienta.Trim()
    };
}

/// <summary>Wystawianie nowej faktury sprzedaży.</summary>
public sealed class NowaModel(
    FirmaProDbContext baza,
    UslugaFaktur uslugaFaktur,
    UslugaCennika uslugaCennika,
    IKursyWalut kursyWalut) : PageModel
{
    /// <summary>
    /// Waluty do wyboru na fakturze.
    /// </summary>
    /// <remarks>
    /// Lista krótka i zamknięta. Kod waluty wpisywany ręcznie prędzej czy
    /// później byłby literówką, a faktura w nieistniejącej walucie nie da się
    /// przeliczyć na złote.
    /// </remarks>
    public static IReadOnlyList<string> Waluty { get; } =
        ["PLN", "EUR", "USD", "GBP", "CHF", "CZK", "SEK", "NOK", "DKK"];

    /// <summary>Kody GTU do wyboru na liście.</summary>
    public static IReadOnlyList<string> KodyGtu { get; } =
        Enumerable.Range(1, 13).Select(n => $"GTU_{n:00}").ToList();

    [BindProperty] public Guid KontrahentId { get; set; }
    [BindProperty] public DateOnly DataWystawienia { get; set; }
    [BindProperty] public DateOnly? DataSprzedazy { get; set; }
    [BindProperty] public DateOnly? TerminPlatnosci { get; set; }
    [BindProperty] public FormaPlatnosci? FormaPlatnosci { get; set; }
    [BindProperty] public string? PodstawaZwolnienia { get; set; }
    [BindProperty] public string Waluta { get; set; } = "PLN";
    [BindProperty] public List<WierszPozycji> Pozycje { get; set; } = [];

    /// <summary>Podmioty trzecie - w zwykłej fakturze pusta lista.</summary>
    [BindProperty] public List<WierszPodmiotu> PodmiotyInne { get; set; } = [];

    // --- podmiot upoważniony ------------------------------------------------

    /// <summary>
    /// Rola podmiotu, który wystawia fakturę w imieniu podatnika.
    /// </summary>
    /// <remarks>
    /// Pusta przy zwykłej fakturze. Wypełniona włącza całą sekcję - komornik,
    /// organ egzekucyjny albo przedstawiciel podatkowy (art. 106c, 18a-18d).
    /// </remarks>
    [BindProperty] public RolaUpowaznionego? UpowaznionyRola { get; set; }

    [BindProperty] public string? UpowaznionyNazwa { get; set; }
    [BindProperty] public string? UpowaznionyNip { get; set; }
    [BindProperty] public string? UpowaznionyAdres { get; set; }

    /// <summary>Role podmiotu upoważnionego do wyboru.</summary>
    public static IReadOnlyList<RolaUpowaznionego> RoleUpowaznionych { get; } =
        Domena.Role.Upowaznionych;

    public static string NazwaRoli(RolaUpowaznionego rola) => Domena.Role.Nazwa(rola);

    /// <summary>Podmiot upoważniony zbudowany z pól formularza.</summary>
    private PodmiotUpowazniony? Upowazniony() =>
        UpowaznionyRola is { } rola && !string.IsNullOrWhiteSpace(UpowaznionyNazwa)
            ? new PodmiotUpowazniony
            {
                Rola = rola,
                Dane = new Podmiot
                {
                    Nazwa = UpowaznionyNazwa.Trim(),
                    Nip = UpowaznionyNip?.Trim() ?? string.Empty,
                    Adres = new Adres { Linia1 = UpowaznionyAdres?.Trim() ?? string.Empty }
                }
            }
            : null;

    /// <summary>Role do wyboru na liście.</summary>
    public static IReadOnlyList<RolaPodmiotu> Role { get; } = Domena.Role.Wszystkie;

    /// <summary>Nazwa roli w brzmieniu ze schematu.</summary>
    public static string NazwaRoli(RolaPodmiotu rola) => Domena.Role.Nazwa(rola);

    public IReadOnlyList<Kontrahent> Kontrahenci { get; private set; } = [];

    /// <summary>
    /// Pozycje cennika podpowiadane przy wypełnianiu wierszy.
    /// </summary>
    /// <remarks>
    /// Cennik podaje wartości początkowe, a nie wiążące - po wybraniu pozycji
    /// wszystkie pola wiersza dalej można poprawić. Rabat dla stałego klienta
    /// nie może wymagać zakładania drugiej pozycji w kartotece.
    /// </remarks>
    public IReadOnlyList<PozycjaCennika> Cennik { get; private set; } = [];

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

        DateOnly dzisiaj = DateOnly.FromDateTime(DateTime.Today);
        DataWystawienia = dzisiaj;
        DataSprzedazy = dzisiaj;

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);
        TerminPlatnosci = dzisiaj.AddDays(firma.DomyslnyTerminPlatnosciDni);
        FormaPlatnosci = Domena.FormaPlatnosci.Przelew;

        // Jeden pusty wiersz na start - użytkownik od razu ma gdzie pisać.
        Pozycje = [new WierszPozycji()];
        PodmiotyInne = [new WierszPodmiotu()];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        List<WierszPozycji> wypelnione = Pozycje.Where(p => !p.CzyPusty).ToList();
        if (wypelnione.Count == 0)
        {
            Bledy.Add("Faktura musi mieć przynajmniej jedną pozycję z nazwą.");
            ZapewnijWiersz();
            return Page();
        }

        KursWaluty? kurs;

        try
        {
            kurs = await PobierzKursAsync(anulowanie);
        }
        catch (BladKursuException blad)
        {
            // Bez kursu nie da się policzyć podatku w złotych, więc faktura
            // w ogóle nie powstaje. Lepiej zatrzymać się tutaj niż wystawić
            // dokument, z którego nie wynika zobowiązanie wobec urzędu.
            Bledy.Add(blad.Message);
            await WczytajListyAsync(anulowanie);
            ZapewnijWiersz();
            return Page();
        }

        WynikWystawienia wynik = await uslugaFaktur.WystawAsync(
            KontrahentId,
            DataWystawienia,
            DataSprzedazy,
            TerminPlatnosci,
            FormaPlatnosci,
            PodstawaZwolnienia,
            wypelnione.Select(p => (p.Nazwa, p.Jednostka, p.Ilosc, p.CenaNetto,
                                    p.KodStawki, p.Gtu)).ToList(),
            kurs: kurs,
            podmiotyInne: PodmiotyInne
                .Where(p => !p.CzyPusty)
                .Select(p => p.NaModel())
                .ToList(),
            upowazniony: Upowazniony(),
            anulowanie: anulowanie);

        if (!wynik.Udalo)
        {
            Bledy.AddRange(wynik.Walidacja.Problemy
                .Where(p => p.Poziom == PoziomProblemu.Blad)
                .Select(p => $"{p.Pole}: {p.Komunikat}"));
            ZapewnijWiersz();
            return Page();
        }

        TempData["Komunikat"] =
            $"Wystawiono fakturę {wynik.Faktura!.Numer} na kwotę " +
            $"{Kwoty.NaTekst(wynik.Faktura.RazemBrutto)} {wynik.Faktura.Waluta}.";

        return RedirectToPage("Szczegoly", new { id = wynik.Faktura.Id });
    }

    /// <summary>
    /// Kurs waluty na dzień wynikający z ustawy.
    /// </summary>
    /// <remarks>
    /// Kurs pobierany jest raz, przy wystawieniu, i zapisywany przy fakturze.
    /// Odczytanie go ponownie tydzień później dałoby inną kwotę podatku niż
    /// ta, którą pokazuje wystawiony już dokument.
    /// </remarks>
    private async Task<KursWaluty?> PobierzKursAsync(CancellationToken anulowanie)
    {
        if (string.IsNullOrWhiteSpace(Waluta)
            || string.Equals(Waluta, "PLN", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        DateOnly dzien = Przeliczenie.DzienKursu(
            DataWystawienia, DataSprzedazy ?? DataWystawienia);

        return await kursyWalut.KursAsync(Waluta, dzien, anulowanie);
    }

    private async Task WczytajListyAsync(CancellationToken anulowanie)
    {
        Kontrahenci = await baza.Kontrahenci
            .Where(k => k.Aktywny)
            .OrderBy(k => k.Nazwa)
            .ToListAsync(anulowanie);

        Cennik = await uslugaCennika.DoWyboruAsync(anulowanie);
    }

    private void ZapewnijWiersz()
    {
        if (Pozycje.Count == 0)
        {
            Pozycje.Add(new WierszPozycji());
        }

        // Formularz klonuje pierwszy wiersz, więc jeden musi tam zostać
        // nawet wtedy, gdy użytkownik żadnego podmiotu nie wpisał.
        if (PodmiotyInne.Count == 0)
        {
            PodmiotyInne.Add(new WierszPodmiotu());
        }
    }

    /// <summary>Nazwa formy płatności pokazywana użytkownikowi.</summary>
    public static string OpisFormy(FormaPlatnosci forma) => forma switch
    {
        Domena.FormaPlatnosci.Gotowka => "Gotówka",
        Domena.FormaPlatnosci.Karta => "Karta",
        Domena.FormaPlatnosci.Bon => "Bon",
        Domena.FormaPlatnosci.Czek => "Czek",
        Domena.FormaPlatnosci.Kredyt => "Kredyt",
        Domena.FormaPlatnosci.Przelew => "Przelew",
        Domena.FormaPlatnosci.Mobilna => "Płatność mobilna",
        _ => forma.ToString()
    };
}
