using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Rejestry;

/// <summary>
/// Zestawienia okresu do wysłania biuru rachunkowemu.
/// </summary>
/// <remarks>
/// Ekran istnieje po to, żeby na koniec miesiąca zamknąć jedną sprawę jednym
/// pobraniem. Księgowa nie wchodzi do programu klienta - dostaje pliki, więc
/// liczy się to, co da się wysłać w załączniku.
/// </remarks>
public sealed class ZestawieniaModel(
    FirmaProDbContext baza,
    UslugaRejestruVat uslugaRejestru,
    UslugaZestawien uslugaZestawien) : PageModel
{
    public RejestrVat Rejestr { get; private set; } = null!;

    public OkresRozliczeniowy Okres => Rejestr.Okres;

    public IReadOnlyList<OkresRozliczeniowy> DostepneOkresy { get; private set; } = [];

    /// <summary>
    /// Termin złożenia deklaracji i zapłaty podatku.
    /// </summary>
    /// <remarks>
    /// Dwudziesty piąty dzień miesiąca po zakończeniu okresu (art. 99 ust. 1
    /// i art. 103 ust. 1 ustawy o VAT). Data widoczna obok zestawienia
    /// odpowiada na pytanie, które i tak pada zaraz po jego pobraniu.
    /// </remarks>
    public DateOnly Termin => TerminDla(Okres);

    public async Task<IActionResult> OnGetAsync(string? okres, CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);
        return Page();
    }

    public async Task<IActionResult> OnGetSprzedazAsync(string? okres,
                                                        CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);

        return File(UslugaZestawien.SprzedazCsv(Rejestr), "text/csv",
            UslugaZestawien.NazwaPliku("sprzedaz", Okres, "csv"));
    }

    public async Task<IActionResult> OnGetZakupyAsync(string? okres,
                                                      CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);

        return File(UslugaZestawien.ZakupyCsv(Rejestr), "text/csv",
            UslugaZestawien.NazwaPliku("zakupy", Okres, "csv"));
    }

    /// <summary>Komplet w jednym pliku - to wysyła się księgowej.</summary>
    public async Task<IActionResult> OnGetPaczkaAsync(string? okres,
                                                      CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);

        Firma firma = await baza.Firmy
            .AsNoTracking()
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        return File(UslugaZestawien.Paczka(Rejestr, firma.Nazwa, firma.Nip),
            "application/zip",
            UslugaZestawien.NazwaPliku("zestawienie", Okres, "zip"));
    }

    // ------------------------------------------------------------ pomocnicze

    private static DateOnly TerminDla(OkresRozliczeniowy okres)
    {
        DateOnly poOkresie = okres.OstatniDzien.AddMonths(1);

        return new DateOnly(poOkresie.Year, poOkresie.Month, 25);
    }

    private async Task WczytajAsync(string? okres, CancellationToken anulowanie)
    {
        TypOkresu typ = await uslugaRejestru.TypOkresuAsync(anulowanie);

        OkresRozliczeniowy wybrany =
            OkresRozliczeniowy.TryZKodu(okres, out OkresRozliczeniowy? zAdresu)
            && zAdresu!.Typ == typ
                ? zAdresu
                : OkresRozliczeniowy.Dla(DateOnly.FromDateTime(DateTime.Today), typ);

        int ile = typ == TypOkresu.Miesieczny ? 13 : 5;
        OkresRozliczeniowy najstarszy = wybrany.Przesun(-(ile - 2));

        var okresy = new List<OkresRozliczeniowy>();
        OkresRozliczeniowy biezacy = najstarszy;

        for (int i = 0; i < ile; i++)
        {
            okresy.Add(biezacy);
            biezacy = biezacy.Nastepny;
        }

        DostepneOkresy = okresy;
        Rejestr = await uslugaZestawien.RejestrAsync(wybrany, anulowanie);
    }
}
