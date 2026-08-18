using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Zus;

/// <summary>
/// Składki ZUS przedsiębiorcy.
/// </summary>
/// <remarks>
/// Ekran robi trzy rzeczy: liczy składkę, pilnuje terminu i przyjmuje
/// potwierdzenie zapłaty. Potwierdzenie nie jest formalnością - dopiero
/// zapłacona składka jest kosztem albo odliczeniem, więc od niego zależy,
/// co wejdzie do księgi.
/// </remarks>
public sealed class IndexModel(
    FirmaProDbContext baza,
    UslugaZus uslugaZus,
    UslugaKsiegi uslugaKsiegi) : PageModel
{
    public int Rok { get; private set; }

    public IReadOnlyList<MiesiacZus> Miesiace { get; private set; } = [];

    public StawkiZus? Stawki { get; private set; }

    public UstawieniaZusFirmy Ustawienia { get; private set; } = new();

    public FormaOpodatkowania Forma { get; private set; }

    public RozliczenieZdrowotnej? RozliczenieRoczne { get; private set; }

    public decimal SpoleczneDoOdliczenia { get; private set; }

    public decimal ZdrowotnaDoOdliczenia { get; private set; }

    public DateOnly Dzis { get; } = DateOnly.FromDateTime(DateTime.Today);

    // --- formularz ustawień ---------------------------------------------------

    [BindProperty] public TytulUbezpieczenia Tytul { get; set; } = TytulUbezpieczenia.Pelny;
    [BindProperty] public bool Chorobowe { get; set; } = true;
    [BindProperty] public decimal StopaWypadkowa { get; set; } = StopyZus.WypadkowaMalyPlatnik;
    [BindProperty] public bool BezFunduszuPracy { get; set; }
    [BindProperty] public bool SpoleczneWKosztach { get; set; }
    [BindProperty] public decimal? DochodPoprzedniegoRoku { get; set; }
    [BindProperty] public decimal? PrzychodPoprzedniegoRoku { get; set; }
    [BindProperty] public int DniProwadzeniaPoprzedniegoRoku { get; set; } = 365;

    public List<string> Bledy { get; } = [];

    public static IReadOnlyList<TytulUbezpieczenia> Tytuly { get; } =
        Enum.GetValues<TytulUbezpieczenia>();

    /// <summary>Nazwa tytułu wraz z tym, co z niego wynika.</summary>
    public static string NazwaTytulu(TytulUbezpieczenia tytul) => tytul switch
    {
        TytulUbezpieczenia.UlgaNaStart => "Ulga na start — 6 miesięcy, tylko zdrowotna",
        TytulUbezpieczenia.Preferencyjny => "Preferencyjny — 24 miesiące, 30% minimalnego",
        TytulUbezpieczenia.MalyZusPlus => "Mały ZUS Plus — podstawa od dochodu",
        TytulUbezpieczenia.Pelny => "Pełny ZUS — 60% przeciętnego",
        _ => tytul.ToString()
    };

    /// <summary>Nazwa miesiąca po polsku.</summary>
    public static string NazwaMiesiaca(int miesiac) => miesiac switch
    {
        1 => "styczeń", 2 => "luty", 3 => "marzec", 4 => "kwiecień",
        5 => "maj", 6 => "czerwiec", 7 => "lipiec", 8 => "sierpień",
        9 => "wrzesień", 10 => "październik", 11 => "listopad", 12 => "grudzień",
        _ => miesiac.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>Czy ryczałt - wtedy zdrowotna liczy się od przychodu, nie od dochodu.</summary>
    public bool Ryczalt => Forma == FormaOpodatkowania.Ryczalt;

    /// <summary>Suma składek naliczonych w roku.</summary>
    public decimal RazemRok => Kwoty.Zaokraglij(Miesiace.Sum(m => m.Razem));

    /// <summary>Suma faktycznie potwierdzona jako zapłacona.</summary>
    public decimal RazemZaplacone =>
        Kwoty.Zaokraglij(Miesiace.Where(m => m.Zaplacona).Sum(m => m.Razem));

    /// <summary>Miesiące po terminie - to one wymagają reakcji.</summary>
    public IReadOnlyList<MiesiacZus> PoTerminie =>
        [.. Miesiace.Where(m => m.PoTerminie(Dzis))];

    public async Task OnGetAsync(int? rok, CancellationToken anulowanie) =>
        await WczytajAsync(rok, anulowanie);

    /// <summary>Zapisuje zasady, na jakich firma opłaca składki.</summary>
    public async Task<IActionResult> OnPostUstawieniaAsync(int? rok,
                                                           CancellationToken anulowanie)
    {
        await WczytajAsync(rok, anulowanie);

        if (StopaWypadkowa is < 0m or > 0.1m)
        {
            Bledy.Add("Stopa wypadkowa mieści się między 0% a 10%.");
        }

        if (DniProwadzeniaPoprzedniegoRoku is < 1 or > 366)
        {
            Bledy.Add("Dni prowadzenia działalności to liczba od 1 do 366.");
        }

        // Bez dochodu za rok poprzedni nie da się ustalić podstawy Małego
        // ZUS Plus, a program nie ma jej skąd zgadnąć.
        if (Tytul == TytulUbezpieczenia.MalyZusPlus && DochodPoprzedniegoRoku is null)
        {
            Bledy.Add("Mały ZUS Plus wymaga podania dochodu za rok poprzedni.");
        }

        if (Tytul == TytulUbezpieczenia.MalyZusPlus
            && Stawki is { } stawki
            && PrzychodPoprzedniegoRoku is decimal przychod
            && !Domena.Zus.MalyZusPlusPrzysluguje(przychod, stawki))
        {
            Bledy.Add($"Przy przychodzie ponad {Kwoty.NaTekst(stawki.LimitPrzychoduMalyZusPlus)} zł "
                      + "Mały ZUS Plus nie przysługuje.");
        }

        if (Bledy.Count > 0)
        {
            return Page();
        }

        UstawieniaZusFirmy ustawienia = await uslugaZus.UstawieniaDoZapisuAsync(anulowanie);

        ustawienia.Tytul = Tytul;
        ustawienia.Chorobowe = Chorobowe;
        ustawienia.StopaWypadkowa = StopaWypadkowa;
        ustawienia.BezFunduszuPracy = BezFunduszuPracy;
        ustawienia.SpoleczneWKosztach = SpoleczneWKosztach;
        ustawienia.DochodPoprzedniegoRoku = DochodPoprzedniegoRoku;
        ustawienia.PrzychodPoprzedniegoRoku = PrzychodPoprzedniegoRoku;
        ustawienia.DniProwadzeniaPoprzedniegoRoku = DniProwadzeniaPoprzedniegoRoku;

        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] = "Zapisano zasady opłacania składek.";

        return RedirectToPage(new { rok = Rok });
    }

    /// <summary>Potwierdza zapłatę składek za miesiąc.</summary>
    public async Task<IActionResult> OnPostZaplacAsync(int rok, int miesiac,
                                                       DateOnly dzien,
                                                       CancellationToken anulowanie)
    {
        await uslugaZus.ZaplacAsync(rok, miesiac,
            dzien == default ? DateOnly.FromDateTime(DateTime.Today) : dzien,
            anulowanie);

        TempData["Komunikat"] =
            $"Zapisano zapłatę składek za {NazwaMiesiaca(miesiac)} {rok}.";

        return RedirectToPage(new { rok });
    }

    /// <summary>Cofa potwierdzenie zapłaty - zapis znika też z księgi.</summary>
    public async Task<IActionResult> OnPostCofnijAsync(int rok, int miesiac,
                                                       CancellationToken anulowanie)
    {
        await uslugaZus.CofnijZaplateAsync(rok, miesiac, anulowanie);

        TempData["Komunikat"] =
            $"Cofnięto zapłatę składek za {NazwaMiesiaca(miesiac)} {rok}.";

        return RedirectToPage(new { rok });
    }

    private async Task WczytajAsync(int? rok, CancellationToken anulowanie)
    {
        Rok = rok ?? DateTime.Today.Year;
        Stawki = StawkiZus.Dla(Rok);

        Forma = await uslugaKsiegi.FormaAsync(anulowanie);
        Ustawienia = await uslugaZus.UstawieniaAsync(anulowanie);
        Miesiace = await uslugaZus.RokAsync(Rok, anulowanie);

        RozliczenieRoczne = await uslugaZus.RozliczenieZdrowotnejAsync(Rok, anulowanie);
        SpoleczneDoOdliczenia = await uslugaZus.SpoleczneDoOdliczeniaAsync(Rok, anulowanie);

        ZdrowotnaDoOdliczenia = Stawki is null
            ? 0m
            : Domena.Zus.ZdrowotnaDoOdliczenia(Forma,
                await uslugaZus.ZdrowotnaZaplaconaAsync(Rok, anulowanie), Stawki);

        // Formularz pokazuje to, co zapisane - inaczej po błędzie walidacji
        // użytkownik zobaczyłby wartości domyślne zamiast swoich.
        if (!ModelState.IsValid || Bledy.Count == 0 && !HttpContext.Request.HasFormContentType)
        {
            Tytul = Ustawienia.Tytul;
            Chorobowe = Ustawienia.Chorobowe;
            StopaWypadkowa = Ustawienia.StopaWypadkowa;
            BezFunduszuPracy = Ustawienia.BezFunduszuPracy;
            SpoleczneWKosztach = Ustawienia.SpoleczneWKosztach;
            DochodPoprzedniegoRoku = Ustawienia.DochodPoprzedniegoRoku;
            PrzychodPoprzedniegoRoku = Ustawienia.PrzychodPoprzedniegoRoku;
            DniProwadzeniaPoprzedniegoRoku = Ustawienia.DniProwadzeniaPoprzedniegoRoku;
        }
    }
}
