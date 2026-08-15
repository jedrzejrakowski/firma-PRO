using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Cennik;

/// <summary>
/// Kartoteka towarów i usług.
/// </summary>
/// <remarks>
/// Ekran łączy listę z formularzem, bo pozycje cennika zakłada się seriami -
/// po zapisaniu jednej od razu chce się wpisać następną. Osobna strona na
/// każdą pozycję kazałaby przy tym klikać dwa razy więcej.
/// </remarks>
public sealed class IndexModel(UslugaCennika uslugaCennika) : PageModel
{
    public IReadOnlyList<PozycjaCennika> Pozycje { get; private set; } = [];

    /// <summary>Pozycja otwarta do poprawienia; pusta oznacza nową.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty] public string Nazwa { get; set; } = string.Empty;
    [BindProperty] public string Jednostka { get; set; } = "szt.";
    [BindProperty] public decimal CenaNetto { get; set; }
    [BindProperty] public string KodStawki { get; set; } = StawkaVat.Vat23.Kod;
    [BindProperty] public string Gtu { get; set; } = string.Empty;
    [BindProperty] public string Pkwiu { get; set; } = string.Empty;
    [BindProperty] public string Cn { get; set; } = string.Empty;
    [BindProperty] public string Indeks { get; set; } = string.Empty;
    [BindProperty] public bool Aktywna { get; set; } = true;

    public WynikWalidacji Walidacja { get; private set; } = new();

    public bool Poprawianie => Id is not null;

    public static IReadOnlyList<StawkaVat> Stawki => StawkaVat.Wszystkie;

    public async Task OnGetAsync(CancellationToken anulowanie)
    {
        if (Id is Guid id && await uslugaCennika.ZnajdzAsync(id, anulowanie) is { } pozycja)
        {
            Wypelnij(pozycja);
        }

        Pozycje = await uslugaCennika.ListaAsync(anulowanie);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        WynikZapisuCennika wynik = await uslugaCennika.ZapiszAsync(
            Id, Nazwa, Jednostka, CenaNetto, KodStawki,
            Gtu, Pkwiu, Cn, Indeks, Aktywna, anulowanie);

        if (!wynik.Udalo)
        {
            Walidacja = wynik.Walidacja;
            Pozycje = await uslugaCennika.ListaAsync(anulowanie);
            return Page();
        }

        TempData["Komunikat"] = Poprawianie
            ? $"Zapisano pozycję „{wynik.Pozycja!.Nazwa}”."
            : $"Dodano pozycję „{wynik.Pozycja!.Nazwa}” do cennika.";

        // Po zapisie wracamy do pustego formularza - następną pozycję wpisuje
        // się od razu, bez czyszczenia pól po poprzedniej.
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPrzelaczAsync(Guid id, CancellationToken anulowanie)
    {
        if (!await uslugaCennika.PrzelaczAktywnoscAsync(id, anulowanie))
        {
            TempData["Ostrzezenie"] = "Nie znaleziono wskazanej pozycji.";
        }

        return RedirectToPage();
    }

    private void Wypelnij(PozycjaCennika pozycja)
    {
        Nazwa = pozycja.Nazwa;
        Jednostka = pozycja.Jednostka;
        CenaNetto = pozycja.CenaNetto;
        KodStawki = pozycja.KodStawki;
        Gtu = pozycja.Gtu ?? string.Empty;
        Pkwiu = pozycja.Pkwiu ?? string.Empty;
        Cn = pozycja.Cn ?? string.Empty;
        Indeks = pozycja.Indeks ?? string.Empty;
        Aktywna = pozycja.Aktywna;
    }
}
