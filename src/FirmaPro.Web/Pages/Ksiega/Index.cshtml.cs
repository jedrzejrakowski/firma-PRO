using System.Globalization;
using System.Text;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using FirmaPro.Wydruk;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages.Ksiega;

/// <summary>
/// Księga podatkowa firmy.
/// </summary>
/// <remarks>
/// Jeden ekran, dwie różne księgi: przy skali i podatku liniowym księga
/// przychodów i rozchodów, przy ryczałcie ewidencja przychodów. Program
/// pokazuje tę, która firmie przysługuje - wybór między nimi nie jest
/// preferencją użytkownika, tylko skutkiem formy opodatkowania.
/// </remarks>
public sealed class IndexModel(
    FirmaProDbContext baza,
    UslugaKsiegi uslugaKsiegi) : PageModel
{
    public FormaOpodatkowania Forma { get; private set; }

    public OkresRozliczeniowy Okres { get; private set; } = null!;

    public IReadOnlyList<OkresRozliczeniowy> DostepneOkresy { get; private set; } = [];

    /// <summary>Księga przychodów i rozchodów - puste przy ryczałcie.</summary>
    public Kpir? Kpir { get; private set; }

    /// <summary>Ewidencja przychodów - puste poza ryczałtem.</summary>
    public EwidencjaRyczaltu? Ryczalt { get; private set; }

    /// <summary>Zapisy wprowadzone ręcznie - do pokazania i usunięcia.</summary>
    public IReadOnlyList<ZapisKsiegi> ZapisyReczne { get; private set; } = [];

    // --- formularz zapisu ręcznego ------------------------------------------

    [BindProperty] public DateOnly Data { get; set; }
    [BindProperty] public string NumerDowodu { get; set; } = string.Empty;
    [BindProperty] public string Opis { get; set; } = string.Empty;
    [BindProperty] public string? Kontrahent { get; set; }
    [BindProperty] public KolumnaKpir Kolumna { get; set; } = KolumnaKpir.PozostaleWydatki;
    [BindProperty] public decimal Kwota { get; set; }
    [BindProperty] public decimal? StawkaRyczaltu { get; set; }
    [BindProperty] public string? Uwagi { get; set; }

    public List<string> Bledy { get; } = [];

    /// <summary>Kolumny do wyboru przy zapisie ręcznym.</summary>
    public static IReadOnlyList<KolumnaKpir> Kolumny { get; } = Enum.GetValues<KolumnaKpir>();

    public static string NazwaKolumny(KolumnaKpir kolumna) => Domena.Kolumny.Nazwa(kolumna);

    public static int NumerKolumny(KolumnaKpir kolumna) => Domena.Kolumny.Numer(kolumna);

    public static IReadOnlyList<decimal> StawkiRyczaltu => Domena.StawkiRyczaltu.Wszystkie;

    public static string StawkaNaTekst(decimal stawka) => Domena.StawkiRyczaltu.NaTekst(stawka);

    /// <summary>Podatek w pełnych złotych, po polsku.</summary>
    public static string PodatekNaTekst(long kwota) => Kwoty.ZloteNaTekst(kwota);

    public async Task<IActionResult> OnGetAsync(string? okres, CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);

        if (Data == default)
        {
            Data = Okres.OstatniDzien;
        }

        return Page();
    }

    /// <summary>Dopisuje zapis, którego nie ma na żadnej fakturze.</summary>
    public async Task<IActionResult> OnPostDodajAsync(string? okres,
                                                      CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);

        if (string.IsNullOrWhiteSpace(NumerDowodu))
        {
            Bledy.Add("Zapis musi mieć numer dowodu księgowego.");
        }

        if (string.IsNullOrWhiteSpace(Opis))
        {
            Bledy.Add("Zapis musi mieć opis zdarzenia gospodarczego.");
        }

        if (Kwota == 0)
        {
            Bledy.Add("Kwota zapisu nie może być zerem.");
        }

        // Przychód bez stawki nie da się opodatkować ryczałtem, więc przy tej
        // formie stawka jest obowiązkowa - i tylko przy przychodach.
        bool przychod = Domena.Kolumny.Przychod(Kolumna);

        if (Forma == FormaOpodatkowania.Ryczalt && przychod && StawkaRyczaltu is null)
        {
            Bledy.Add("Przy ryczałcie przychód wymaga wskazania stawki.");
        }

        if (Bledy.Count > 0)
        {
            return Page();
        }

        baza.ZapisyKsiegi.Add(new ZapisKsiegi
        {
            Data = Data,
            NumerDowodu = NumerDowodu.Trim(),
            Opis = Opis.Trim(),
            Kontrahent = string.IsNullOrWhiteSpace(Kontrahent) ? null : Kontrahent.Trim(),
            Kolumna = Kolumna,
            Kwota = Kwoty.Zaokraglij(Kwota),
            StawkaRyczaltu = przychod ? StawkaRyczaltu : null,
            Uwagi = string.IsNullOrWhiteSpace(Uwagi) ? null : Uwagi.Trim()
        });

        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] = $"Dopisano zapis {NumerDowodu.Trim()} do księgi.";

        return RedirectToPage(new { okres = Okres.Kod });
    }

    public async Task<IActionResult> OnPostUsunAsync(Guid id, string? okres,
                                                     CancellationToken anulowanie)
    {
        ZapisKsiegi? zapis = await baza.ZapisyKsiegi
            .FirstOrDefaultAsync(z => z.Id == id, anulowanie);

        if (zapis is not null)
        {
            baza.ZapisyKsiegi.Remove(zapis);
            await baza.SaveChangesAsync(anulowanie);

            TempData["Komunikat"] = "Usunięto zapis z księgi.";
        }
        else
        {
            TempData["Ostrzezenie"] = "Nie znaleziono wskazanego zapisu.";
        }

        return RedirectToPage(new { okres });
    }

    /// <summary>
    /// Księga w PDF - do wydrukowania i przechowywania.
    /// </summary>
    /// <remarks>
    /// Plik CSV jest wygodny dla księgowej, ale księgą jest to, co da się
    /// wydrukować: podatnik ma obowiązek przechowywać ją wraz z dowodami,
    /// na których podstawie powstały zapisy.
    /// </remarks>
    public async Task<IActionResult> OnGetPdfAsync(string? okres, CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);

        Firma firma = await baza.Firmy
            .AsNoTracking()
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        byte[] pdf = Forma == FormaOpodatkowania.Ryczalt
            ? WydrukKsiegi.Utworz(Ryczalt!, firma.Nazwa, firma.Nip)
            : WydrukKsiegi.Utworz(Kpir!, firma.Nazwa, firma.Nip);

        string nazwa = Forma == FormaOpodatkowania.Ryczalt
            ? $"ewidencja-przychodow-{Okres.Kod}.pdf"
            : $"kpir-{Okres.Kod}.pdf";

        return File(pdf, "application/pdf", nazwa);
    }

    /// <summary>Księga w pliku do wysłania księgowej albo do archiwum.</summary>
    public async Task<IActionResult> OnGetCsvAsync(string? okres, CancellationToken anulowanie)
    {
        await WczytajAsync(okres, anulowanie);

        byte[] plik = Forma == FormaOpodatkowania.Ryczalt
            ? CsvRyczaltu(Ryczalt!)
            : CsvKsiegi(Kpir!);

        string nazwa = Forma == FormaOpodatkowania.Ryczalt
            ? $"ewidencja-przychodow-{Okres.Kod}.csv"
            : $"kpir-{Okres.Kod}.csv";

        return File(plik, "text/csv", nazwa);
    }

    // ------------------------------------------------------------ pomocnicze

    private async Task WczytajAsync(string? okres, CancellationToken anulowanie)
    {
        Forma = await uslugaKsiegi.FormaAsync(anulowanie);

        Okres = OkresRozliczeniowy.TryZKodu(okres, out OkresRozliczeniowy? zAdresu)
                && zAdresu!.Typ == TypOkresu.Miesieczny
            ? zAdresu
            : OkresRozliczeniowy.Dla(DateOnly.FromDateTime(DateTime.Today),
                TypOkresu.Miesieczny);

        // Trzynaście miesięcy: bieżący, dwanaście wstecz. Księgę zamyka się
        // rocznie, więc rzadko sięga się dalej.
        OkresRozliczeniowy najstarszy = Okres.Przesun(-12);
        var okresy = new List<OkresRozliczeniowy>();
        OkresRozliczeniowy biezacy = najstarszy;

        for (int i = 0; i < 13; i++)
        {
            okresy.Add(biezacy);
            biezacy = biezacy.Nastepny;
        }

        DostepneOkresy = okresy;

        if (Forma == FormaOpodatkowania.Ryczalt)
        {
            Ryczalt = await uslugaKsiegi.RyczaltAsync(Okres, anulowanie);
        }
        else
        {
            Kpir = await uslugaKsiegi.KpirAsync(Okres, anulowanie);
        }

        ZapisyReczne = await baza.ZapisyKsiegi
            .AsNoTracking()
            .Where(z => z.Data >= Okres.PierwszyDzien && z.Data <= Okres.OstatniDzien)
            .OrderBy(z => z.Data)
            .ToListAsync(anulowanie);
    }

    /// <summary>
    /// Księga w pliku CSV.
    /// </summary>
    /// <remarks>
    /// Średnik jako separator i przecinek dziesiętny - inaczej polski Excel
    /// wczyta wszystko do jednej kolumny. Znacznik kodowania na początku,
    /// bo bez niego polskie znaki zamieniają się w krzaki.
    /// </remarks>
    private static byte[] CsvKsiegi(Kpir ksiega)
    {
        var tekst = new StringBuilder();

        tekst.AppendLine("Lp.;Data;Nr dowodu;Kontrahent;Adres;Opis;"
                         + "7 Sprzedaż;8 Pozostałe przychody;9 Razem przychód;"
                         + "10 Zakup towarów;11 Koszty uboczne;12 Wynagrodzenia;"
                         + "13 Pozostałe wydatki;14 Razem wydatki;15 B+R;16 Uwagi");

        int lp = 1;

        foreach (WpisKsiegi wpis in ksiega.Wpisy)
        {
            tekst.Append(lp++).Append(';')
                 .Append(wpis.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                 .Append(';').Append(Pole(wpis.NumerDowodu))
                 .Append(';').Append(Pole(wpis.Kontrahent))
                 .Append(';').Append(Pole(wpis.Adres))
                 .Append(';').Append(Pole(wpis.Opis));

            foreach (KolumnaKpir kolumna in KolejnoscCsv)
            {
                tekst.Append(';')
                     .Append(wpis.Kolumna == kolumna ? NaCsv(wpis.Kwota) : string.Empty);

                // Kolumny sumujące - w wierszu zapisu zostają puste, bo zapis
                // trafia zawsze do jednej kolumny źródłowej.
                if (kolumna == KolumnaKpir.PozostalePrzychody
                    || kolumna == KolumnaKpir.PozostaleWydatki)
                {
                    tekst.Append(';');
                }
            }

            tekst.Append(';').Append(Pole(wpis.Uwagi)).AppendLine();
        }

        tekst.Append("RAZEM;;;;;")
             .Append(';').Append(NaCsv(ksiega.Kolumna(KolumnaKpir.SprzedazTowarowIUslug)))
             .Append(';').Append(NaCsv(ksiega.Kolumna(KolumnaKpir.PozostalePrzychody)))
             .Append(';').Append(NaCsv(ksiega.RazemPrzychod))
             .Append(';').Append(NaCsv(ksiega.Kolumna(KolumnaKpir.ZakupTowarow)))
             .Append(';').Append(NaCsv(ksiega.Kolumna(KolumnaKpir.KosztyUboczneZakupu)))
             .Append(';').Append(NaCsv(ksiega.Kolumna(KolumnaKpir.Wynagrodzenia)))
             .Append(';').Append(NaCsv(ksiega.Kolumna(KolumnaKpir.PozostaleWydatki)))
             .Append(';').Append(NaCsv(ksiega.RazemWydatki))
             .Append(';').Append(NaCsv(ksiega.Kolumna(KolumnaKpir.BadaniaIRozwoj)))
             .Append(';').AppendLine();

        tekst.Append("NARASTAJĄCO;;;;;;")
             .Append(NaCsv(ksiega.Narastajaco(KolumnaKpir.SprzedazTowarowIUslug)))
             .Append(';').Append(NaCsv(ksiega.Narastajaco(KolumnaKpir.PozostalePrzychody)))
             .Append(';').Append(NaCsv(ksiega.PrzychodNarastajaco))
             .Append(';').Append(NaCsv(ksiega.Narastajaco(KolumnaKpir.ZakupTowarow)))
             .Append(';').Append(NaCsv(ksiega.Narastajaco(KolumnaKpir.KosztyUboczneZakupu)))
             .Append(';').Append(NaCsv(ksiega.Narastajaco(KolumnaKpir.Wynagrodzenia)))
             .Append(';').Append(NaCsv(ksiega.Narastajaco(KolumnaKpir.PozostaleWydatki)))
             .Append(';').Append(NaCsv(ksiega.WydatkiNarastajaco))
             .Append(';').Append(NaCsv(ksiega.Narastajaco(KolumnaKpir.BadaniaIRozwoj)))
             .Append(';').AppendLine();

        return ZBom(tekst.ToString());
    }

    /// <summary>Kolejność kolumn na wydruku i w pliku - jak w rozporządzeniu.</summary>
    private static readonly KolumnaKpir[] KolejnoscCsv =
    [
        KolumnaKpir.SprzedazTowarowIUslug,
        KolumnaKpir.PozostalePrzychody,
        KolumnaKpir.ZakupTowarow,
        KolumnaKpir.KosztyUboczneZakupu,
        KolumnaKpir.Wynagrodzenia,
        KolumnaKpir.PozostaleWydatki,
        KolumnaKpir.BadaniaIRozwoj
    ];

    private static byte[] CsvRyczaltu(EwidencjaRyczaltu ewidencja)
    {
        var tekst = new StringBuilder();

        tekst.AppendLine("Lp.;Data;Nr dowodu;Opis;Stawka;Przychód");

        int lp = 1;

        foreach (WpisRyczaltu wpis in ewidencja.Wpisy)
        {
            tekst.Append(lp++).Append(';')
                 .Append(wpis.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                 .Append(';').Append(Pole(wpis.NumerDowodu))
                 .Append(';').Append(Pole(wpis.Opis))
                 .Append(';').Append(Domena.StawkiRyczaltu.NaTekst(wpis.Stawka))
                 .Append(';').Append(NaCsv(wpis.Kwota))
                 .AppendLine();
        }

        tekst.AppendLine();
        tekst.AppendLine("Stawka;Przychód okresu;Podatek okresu;"
                         + "Przychód narastająco;Podatek narastająco");

        foreach (PrzychodWStawce stawka in ewidencja.NarastajacoWedlugStawek)
        {
            PrzychodWStawce? okresu = ewidencja.WedlugStawek
                .FirstOrDefault(s => s.Stawka == stawka.Stawka);

            tekst.Append(Domena.StawkiRyczaltu.NaTekst(stawka.Stawka))
                 .Append(';').Append(NaCsv(okresu?.Przychod ?? 0m))
                 .Append(';').Append(okresu?.Podatek ?? 0L)
                 .Append(';').Append(NaCsv(stawka.Przychod))
                 .Append(';').Append(stawka.Podatek)
                 .AppendLine();
        }

        tekst.Append("RAZEM;").Append(NaCsv(ewidencja.RazemPrzychod))
             .Append(';').Append(ewidencja.RazemPodatek)
             .Append(';').Append(NaCsv(ewidencja.PrzychodNarastajaco))
             .Append(';').Append(ewidencja.PodatekNarastajaco)
             .AppendLine();

        return ZBom(tekst.ToString());
    }

    private static string NaCsv(decimal wartosc) =>
        wartosc.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Ujmuje pole w cudzysłowy, gdy zawiera średnik albo cudzysłów.</summary>
    private static string Pole(string? wartosc)
    {
        string tekst = wartosc ?? string.Empty;

        return tekst.Contains(';', StringComparison.Ordinal)
               || tekst.Contains('"', StringComparison.Ordinal)
               || tekst.Contains('\n', StringComparison.Ordinal)
            ? '"' + tekst.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : tekst;
    }

    private static byte[] ZBom(string tekst) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(tekst)];
}
