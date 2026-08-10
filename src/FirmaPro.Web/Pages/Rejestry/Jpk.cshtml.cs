using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Rejestry;

/// <summary>
/// Deklaracja JPK_V7 za wybrany okres.
/// </summary>
/// <remarks>
/// Ekran pokazuje wszystkie wypełnione pozycje deklaracji, żeby dało się je
/// porównać z rejestrem przed złożeniem. Program nie wysyła pliku do urzędu -
/// robi się to bezpłatną aplikacją Ministerstwa, która wymaga podpisu.
/// </remarks>
public sealed class JpkModel(
    UslugaDeklaracji uslugaDeklaracji,
    UslugaRejestruVat uslugaRejestru) : PageModel
{
    public PodgladDeklaracji Podglad { get; private set; } = null!;

    public DeklaracjaVat Deklaracja => Podglad.Deklaracja;

    public OkresRozliczeniowy Okres => Deklaracja.Okres;

    public IReadOnlyList<OkresRozliczeniowy> DostepneOkresy { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string? okres, CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);
        return Page();
    }

    /// <summary>Udostępnia plik JPK_V7M do pobrania.</summary>
    public async Task<IActionResult> OnGetPlikAsync(string? okres, bool korekta,
                                                    CancellationToken anulowanie)
    {
        OkresRozliczeniowy wybrany = await UstalOkresAsync(okres, anulowanie);

        byte[] plik = await uslugaDeklaracji.ZbudujPlikAsync(wybrany,
            korekta ? CelZlozenia.Korekta : CelZlozenia.Pierwotny, anulowanie);

        return File(plik, "application/xml", $"JPK_V7M_{wybrany.Kod}.xml");
    }

    public async Task<IActionResult> OnPostZamknijAsync(string? okres,
                                                        CancellationToken anulowanie)
    {
        OkresRozliczeniowy wybrany = await UstalOkresAsync(okres, anulowanie);
        await uslugaDeklaracji.ZamknijAsync(wybrany, anulowanie);

        TempData["Komunikat"] =
            $"Zamknięto okres {wybrany.Nazwa}. Nadwyżka przechodzi na następny okres.";

        return RedirectToPage(new { okres = wybrany.Kod });
    }

    public async Task<IActionResult> OnPostOtworzAsync(string? okres,
                                                       CancellationToken anulowanie)
    {
        OkresRozliczeniowy wybrany = await UstalOkresAsync(okres, anulowanie);
        await uslugaDeklaracji.OtworzAsync(wybrany, anulowanie);

        TempData["Ostrzezenie"] =
            $"Otwarto okres {wybrany.Nazwa}. Jeżeli deklaracja została już złożona, " +
            "zmiany wymagają korekty.";

        return RedirectToPage(new { okres = wybrany.Kod });
    }

    /// <summary>Opis pozycji deklaracji, żeby liczby nie stały bez wyjaśnienia.</summary>
    public static string OpisPola(int numer) => numer switch
    {
        10 => "Dostawa towarów i usług na terytorium kraju, zwolniona od podatku",
        11 => "Dostawa towarów i świadczenie usług poza terytorium kraju",
        12 => "w tym usługi z art. 100 ust. 1 pkt 4 ustawy",
        13 => "Dostawa towarów i usług na terytorium kraju, stawka 0%",
        15 => "Stawka 5% - podstawa opodatkowania",
        16 => "Stawka 5% - podatek należny",
        17 => "Stawka 7% albo 8% - podstawa opodatkowania",
        18 => "Stawka 7% albo 8% - podatek należny",
        19 => "Stawka 22% albo 23% - podstawa opodatkowania",
        20 => "Stawka 22% albo 23% - podatek należny",
        21 => "Wewnątrzwspólnotowa dostawa towarów",
        22 => "Eksport towarów",
        31 => "Dostawa towarów, dla której podatnikiem jest nabywca",
        38 => "Łączna wysokość podatku należnego",
        39 => "Nadwyżka z poprzedniej deklaracji",
        40 => "Nabycie środków trwałych - wartość netto",
        41 => "Nabycie środków trwałych - podatek naliczony",
        42 => "Nabycie pozostałych towarów i usług - wartość netto",
        43 => "Nabycie pozostałych towarów i usług - podatek naliczony",
        48 => "Łączna wysokość podatku naliczonego do odliczenia",
        51 => "Wysokość podatku podlegająca wpłacie do urzędu",
        53 => "Wysokość nadwyżki podatku naliczonego nad należnym",
        62 => "Wysokość nadwyżki do przeniesienia na następny okres",
        _ => string.Empty
    };

    private async Task WczytajAsync(string? okres, CancellationToken anulowanie)
    {
        TypOkresu typ = await uslugaRejestru.TypOkresuAsync(anulowanie);
        OkresRozliczeniowy wybrany = await UstalOkresAsync(okres, anulowanie);

        int ile = typ == TypOkresu.Miesieczny ? 13 : 5;
        OkresRozliczeniowy biezacy = wybrany.Przesun(-(ile - 2));

        var okresy = new List<OkresRozliczeniowy>();
        for (int i = 0; i < ile; i++)
        {
            okresy.Add(biezacy);
            biezacy = biezacy.Nastepny;
        }

        DostepneOkresy = okresy;
        Podglad = await uslugaDeklaracji.ZbudujAsync(wybrany, anulowanie);
    }

    private async Task<OkresRozliczeniowy> UstalOkresAsync(string? okres,
                                                           CancellationToken anulowanie)
    {
        TypOkresu typ = await uslugaRejestru.TypOkresuAsync(anulowanie);

        return OkresRozliczeniowy.TryZKodu(okres, out OkresRozliczeniowy? zAdresu)
               && zAdresu!.Typ == typ
            ? zAdresu
            : OkresRozliczeniowy.Dla(DateOnly.FromDateTime(DateTime.Today), typ);
    }
}
