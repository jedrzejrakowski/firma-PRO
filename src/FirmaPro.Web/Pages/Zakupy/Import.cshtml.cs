using FirmaPro.Domena;
using FirmaPro.Ksef;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Zakupy;

/// <summary>Decyzja użytkownika co do jednej faktury z listy - dane z formularza.</summary>
public sealed class PozycjaImportu
{
    public bool Zaznaczona { get; set; }

    public string NumerKsef { get; set; } = string.Empty;

    public RodzajZakupu Rodzaj { get; set; } = RodzajZakupu.TowaryIUslugi;

    public bool Odliczany { get; set; } = true;
}

/// <summary>
/// Pobieranie faktur zakupu z KSeF.
/// </summary>
/// <remarks>
/// Strona pokazuje, co KSeF ma za dany okres, i pozwala przenieść wybrane
/// dokumenty do rejestru. Kwoty biorą się z KSeF, nie z formularza -
/// przeglądarka decyduje wyłącznie o tym, które faktury wchodzą i jak są
/// zakwalifikowane.
/// </remarks>
public sealed class ImportModel(
    UslugaImportuZakupow uslugaImportu,
    UslugaRejestruVat uslugaRejestru) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly DataOd { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly DataDo { get; set; }

    [BindProperty]
    public List<PozycjaImportu> Pozycje { get; set; } = [];

    /// <summary>Faktury zwrócone przez KSeF - w tej samej kolejności co pozycje.</summary>
    public IReadOnlyList<ZnalezionaFaktura> Znalezione { get; private set; } = [];

    /// <summary>Czy odpytano już KSeF - odróżnia „nic nie ma" od „jeszcze nie pytaliśmy".</summary>
    public bool Szukano { get; private set; }

    public string? Blad { get; private set; }

    private TypOkresu _typOkresu = TypOkresu.Miesieczny;

    /// <summary>
    /// Okres, w którym program ujmie fakturę po zaimportowaniu.
    /// </summary>
    /// <remarks>
    /// Pokazujemy to od razu, bo to jedyny wniosek, jaki program wyciąga sam:
    /// odliczenie trafia do pierwszego okresu, w którym jest możliwe.
    /// </remarks>
    public string OpisOkresu(FakturaZakupowa dane)
    {
        ArgumentNullException.ThrowIfNull(dane);

        DateOnly wystawienia = dane.DataWystawienia ?? DataOd;
        DateOnly wplywu = dane.DataPrzyjecia is DateTimeOffset przyjecie
            ? DateOnly.FromDateTime(przyjecie.UtcDateTime)
            : wystawienia;

        return TerminyVat.NajwczesniejszyOkresOdliczenia(wystawienia, wplywu, _typOkresu).Nazwa;
    }

    public async Task OnGetAsync(CancellationToken anulowanie)
    {
        UstawDomyslneDaty();
        _typOkresu = await uslugaRejestru.TypOkresuAsync(anulowanie);
    }

    public async Task<IActionResult> OnPostSzukajAsync(CancellationToken anulowanie)
    {
        UstawDomyslneDaty();
        _typOkresu = await uslugaRejestru.TypOkresuAsync(anulowanie);

        if (DataDo < DataOd)
        {
            Blad = "Data początkowa jest późniejsza niż końcowa.";
            return Page();
        }

        try
        {
            Znalezione = await uslugaImportu.SzukajAsync(DataOd, DataDo, anulowanie);
            Szukano = true;
        }
        catch (BladKsefException blad)
        {
            Blad = blad.PelnyOpis();
            return Page();
        }

        // Formularz buduje się od nowa na podstawie tego, co przyszło z KSeF -
        // wcześniejsze zaznaczenia dotyczyłyby innej listy.
        Pozycje = Znalezione
            .Select(f => new PozycjaImportu
            {
                Zaznaczona = !f.JuzWRejestrze,
                NumerKsef = f.Dane.NumerKsef
            })
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostImportujAsync(CancellationToken anulowanie)
    {
        List<DecyzjaImportu> decyzje = Pozycje
            .Where(p => p.Zaznaczona && !string.IsNullOrWhiteSpace(p.NumerKsef))
            .Select(p => new DecyzjaImportu(p.NumerKsef, p.Rodzaj, p.Odliczany))
            .ToList();

        if (decyzje.Count == 0)
        {
            TempData["Ostrzezenie"] = "Nie zaznaczono żadnej faktury do pobrania.";
            return RedirectToPage(new { DataOd, DataDo });
        }

        WynikImportu wynik;
        try
        {
            wynik = await uslugaImportu.ImportujAsync(DataOd, DataDo, decyzje, anulowanie);
        }
        catch (BladKsefException blad)
        {
            UstawDomyslneDaty();
            _typOkresu = await uslugaRejestru.TypOkresuAsync(anulowanie);
            Blad = blad.PelnyOpis();
            return Page();
        }

        TempData["Komunikat"] = wynik.Pominieto == 0
            ? $"Pobrano faktur: {wynik.Zaimportowano}."
            : $"Pobrano faktur: {wynik.Zaimportowano}, pominięto już wpisane: {wynik.Pominieto}.";

        return RedirectToPage("Index");
    }

    /// <summary>Domyślnie pytamy o bieżący miesiąc - najczęstszy przypadek.</summary>
    private void UstawDomyslneDaty()
    {
        if (DataOd != default && DataDo != default)
        {
            return;
        }

        DateOnly dzisiaj = DateOnly.FromDateTime(DateTime.Today);
        DataOd = new DateOnly(dzisiaj.Year, dzisiaj.Month, 1);
        DataDo = dzisiaj;
    }
}
