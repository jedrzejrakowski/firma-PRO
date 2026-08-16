using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Cykliczne;

/// <summary>Zakładanie i poprawianie wzorca faktury cyklicznej.</summary>
public sealed class WzorzecModel(
    FirmaProDbContext baza,
    UslugaFakturCyklicznych usluga,
    UslugaCennika uslugaCennika,
    TimeProvider czas) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }

    [BindProperty] public string Nazwa { get; set; } = string.Empty;
    [BindProperty] public Guid KontrahentId { get; set; }
    [BindProperty] public RytmFaktury Rytm { get; set; } = RytmFaktury.Miesiecznie;
    [BindProperty] public int DzienMiesiaca { get; set; } = 1;
    [BindProperty] public int TerminPlatnosciDni { get; set; } = 14;
    [BindProperty] public FormaPlatnosci? FormaPlatnosci { get; set; } = Domena.FormaPlatnosci.Przelew;
    [BindProperty] public DateOnly Od { get; set; }
    [BindProperty] public DateOnly? Do { get; set; }
    [BindProperty] public bool Aktywny { get; set; } = true;
    [BindProperty] public List<Faktury.WierszPozycji> Pozycje { get; set; } = [];

    public IReadOnlyList<Kontrahent> Kontrahenci { get; private set; } = [];
    public IReadOnlyList<PozycjaCennika> Cennik { get; private set; } = [];

    public WynikWalidacji Walidacja { get; private set; } = new();

    public bool Poprawianie => Id is not null;

    /// <summary>Dni do wyboru: 1-28 oraz ostatni dzień miesiąca.</summary>
    /// <remarks>
    /// Powyżej 28 dnia nie ma w każdym miesiącu. Zamiast pozwalać wybrać 31
    /// i po cichu przycinać go w lutym, dajemy osobną pozycję „ostatni dzień".
    /// </remarks>
    public static IReadOnlyList<int> DniDoWyboru { get; } =
        [.. Enumerable.Range(1, 28), Cyklicznosc.OstatniDzien];

    public static string OpisDnia(int dzien) => dzien == Cyklicznosc.OstatniDzien
        ? "ostatni dzień miesiąca"
        : dzien.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string OpisRytmu(RytmFaktury rytm) => Cyklicznosc.Opis(rytm);

    public static string OpisFormy(FormaPlatnosci forma) =>
        Faktury.NowaModel.OpisFormy(forma);

    public static IReadOnlyList<string> KodyGtu => Faktury.NowaModel.KodyGtu;

    public async Task<IActionResult> OnGetAsync(CancellationToken anulowanie)
    {
        await WczytajListyAsync(anulowanie);

        if (Kontrahenci.Count == 0)
        {
            TempData["Ostrzezenie"] =
                "Zanim założysz wzorzec, dodaj przynajmniej jednego kontrahenta.";
            return RedirectToPage("/Kontrahenci/Nowy");
        }

        if (Id is Guid id)
        {
            WzorzecCykliczny? wzorzec = await usluga.ZnajdzAsync(id, anulowanie);

            if (wzorzec is null)
            {
                TempData["Ostrzezenie"] = "Nie znaleziono wskazanego wzorca.";
                return RedirectToPage("Index");
            }

            Wypelnij(wzorzec);
        }
        else
        {
            Od = DateOnly.FromDateTime(czas.GetUtcNow().UtcDateTime);
            Pozycje = [new Faktury.WierszPozycji()];
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        WynikZapisuWzorca wynik = await usluga.ZapiszAsync(
            Id, Nazwa, KontrahentId, Rytm, DzienMiesiaca, TerminPlatnosciDni,
            FormaPlatnosci, Od, Do, Aktywny,
            [.. Pozycje.Select(p => new WierszWzorca(
                p.Nazwa, p.Jednostka, p.Ilosc, p.CenaNetto, p.KodStawki, p.Gtu))],
            anulowanie);

        if (!wynik.Udalo)
        {
            Walidacja = wynik.Walidacja;
            await WczytajListyAsync(anulowanie);
            ZapewnijWiersz();
            return Page();
        }

        TempData["Komunikat"] = Poprawianie
            ? $"Zapisano wzorzec „{wynik.Wzorzec!.Nazwa}”."
            : $"Założono wzorzec „{wynik.Wzorzec!.Nazwa}”. " +
              $"Pierwsza faktura wypada {wynik.Wzorzec.NastepneWystawienie:yyyy-MM-dd}.";

        return RedirectToPage("Index");
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
            Pozycje.Add(new Faktury.WierszPozycji());
        }
    }

    private void Wypelnij(WzorzecCykliczny wzorzec)
    {
        Nazwa = wzorzec.Nazwa;
        KontrahentId = wzorzec.KontrahentId;
        Rytm = wzorzec.Rytm;
        DzienMiesiaca = wzorzec.DzienMiesiaca;
        TerminPlatnosciDni = wzorzec.TerminPlatnosciDni;
        FormaPlatnosci = wzorzec.FormaPlatnosci;
        Od = wzorzec.Od;
        Do = wzorzec.Do;
        Aktywny = wzorzec.Aktywny;

        Pozycje = [.. wzorzec.Pozycje
            .OrderBy(p => p.NrWiersza)
            .Select(p => new Faktury.WierszPozycji
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                KodStawki = p.KodStawki,
                Gtu = p.Gtu
            })];

        ZapewnijWiersz();
    }
}
