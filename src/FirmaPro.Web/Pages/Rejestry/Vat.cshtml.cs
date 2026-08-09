using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages.Rejestry;

/// <summary>
/// Rejestr VAT za wybrany okres.
/// </summary>
/// <remarks>
/// Ekran pokazuje to samo, co trafi później do deklaracji: sprzedaż
/// w rozbiciu na stawki, zakupy z podziałem na środki trwałe i pozostałe
/// oraz różnicę podatku za okres.
/// </remarks>
public sealed class VatModel(UslugaRejestruVat uslugaRejestru) : PageModel
{
    public RejestrVat Rejestr { get; private set; } = null!;

    public OkresRozliczeniowy Okres => Rejestr.Okres;

    /// <summary>Okresy do wyboru na liście - rok wstecz i bieżący.</summary>
    public IReadOnlyList<OkresRozliczeniowy> DostepneOkresy { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string? okres, CancellationToken anulowanie)
    {
        TypOkresu typ = await uslugaRejestru.TypOkresuAsync(anulowanie);

        OkresRozliczeniowy wybrany =
            OkresRozliczeniowy.TryZKodu(okres, out OkresRozliczeniowy? zAdresu)
            && zAdresu!.Typ == typ
                ? zAdresu
                : OkresRozliczeniowy.Dla(DateOnly.FromDateTime(DateTime.Today), typ);

        // Lista sięga rok wstecz i jeden okres do przodu - dalsza przeszłość
        // rzadko jest potrzebna, a wybór z długiej listy męczy.
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
        Rejestr = await uslugaRejestru.ZbudujAsync(wybrany, anulowanie);

        return Page();
    }
}
