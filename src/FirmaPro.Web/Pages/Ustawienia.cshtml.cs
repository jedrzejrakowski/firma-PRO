using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Ustawienia firmy: dane na fakturze, numeracja i dostęp do KSeF.
/// </summary>
/// <remarks>
/// <para>
/// Bez tej strony tokena KSeF dałoby się wprowadzić wyłącznie zmienną
/// środowiskową albo ręcznym zapisem w bazie - a to zadanie dla programisty,
/// nie dla osoby wystawiającej faktury.
/// </para>
/// <para>
/// Token nigdy nie wraca na stronę. Formularz pokazuje tylko informację, czy
/// jest zapisany; puste pole oznacza „zostaw jak było”, nie „skasuj”. Dzięki
/// temu zapisanie zmiany adresu nie kasuje przy okazji dostępu do KSeF, a
/// szyfrogram nie krąży po sieci przy każdym otwarciu strony.
/// </para>
/// </remarks>
public sealed class UstawieniaModel(
    FirmaProDbContext baza,
    IOchronaTokena ochronaTokena) : PageModel
{
    [BindProperty] public string Nazwa { get; set; } = string.Empty;
    [BindProperty] public string Nip { get; set; } = string.Empty;
    [BindProperty] public string KodKraju { get; set; } = "PL";
    [BindProperty] public string AdresLinia1 { get; set; } = string.Empty;
    [BindProperty] public string AdresLinia2 { get; set; } = string.Empty;
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Telefon { get; set; } = string.Empty;
    [BindProperty] public string RachunekBankowy { get; set; } = string.Empty;
    [BindProperty] public string NazwaBanku { get; set; } = string.Empty;
    [BindProperty] public string MiejsceWystawienia { get; set; } = string.Empty;
    [BindProperty] public string StopkaFaktury { get; set; } = string.Empty;
    [BindProperty] public int DomyslnyTerminPlatnosciDni { get; set; } = 14;
    [BindProperty] public SrodowiskoKsef Srodowisko { get; set; } = SrodowiskoKsef.Test;
    [BindProperty] public TypOkresu TypOkresuVat { get; set; } = TypOkresu.Miesieczny;
    [BindProperty] public string KodUrzeduSkarbowego { get; set; } = string.Empty;

    /// <summary>Nowy token KSeF - puste pole zostawia dotychczasowy.</summary>
    [BindProperty] public string TokenKsef { get; set; } = string.Empty;

    /// <summary>Zaznaczone pole usuwa zapisany token.</summary>
    [BindProperty] public bool UsunToken { get; set; }

    public bool TokenZapisany { get; private set; }

    /// <summary>
    /// Token wskazany zmienną środowiskową ma pierwszeństwo przed zapisanym.
    /// </summary>
    public bool TokenZeSrodowiska { get; private set; }

    public List<string> Bledy { get; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken anulowanie)
    {
        // Potwierdzenie zapisu wyświetla wspólny układ strony (TempData).
        Firma firma = await WczytajFirmeAsync(anulowanie);
        Wypelnij(firma);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken anulowanie)
    {
        Firma firma = await WczytajFirmeAsync(anulowanie);

        string nip = new((Nip ?? string.Empty).Where(char.IsDigit).ToArray());
        string rachunek = new((RachunekBankowy ?? string.Empty)
            .Where(char.IsLetterOrDigit).ToArray());

        if (string.IsNullOrWhiteSpace(Nazwa))
        {
            Bledy.Add("Nazwa firmy jest wymagana.");
        }

        // NIP sprzedawcy jest na fakturze obowiązkowy i to po nim KSeF
        // rozpoznaje wystawcę - tutaj, w odróżnieniu od kartoteki nabywców,
        // nie może go zabraknąć.
        if (!Walidator.NipPoprawny(nip))
        {
            Bledy.Add($"Numer NIP „{Nip}” jest nieprawidłowy.");
        }

        if ((KodKraju ?? string.Empty).Length != 2)
        {
            Bledy.Add("Kod kraju musi mieć dokładnie dwie litery, np. PL.");
        }

        if (string.IsNullOrWhiteSpace(AdresLinia1))
        {
            Bledy.Add("Adres firmy jest wymagany.");
        }

        if (rachunek.Length > 0 && !Walidator.RachunekPoprawny(rachunek))
        {
            Bledy.Add("Numer rachunku bankowego ma błędną sumę kontrolną.");
        }

        if (DomyslnyTerminPlatnosciDni is < 0 or > 365)
        {
            Bledy.Add("Termin płatności musi mieścić się w przedziale 0-365 dni.");
        }

        string kodUrzedu = (KodUrzeduSkarbowego ?? string.Empty).Trim();
        if (kodUrzedu.Length > 0 && (kodUrzedu.Length != 4 || !kodUrzedu.All(char.IsDigit)))
        {
            Bledy.Add("Kod urzędu skarbowego składa się z czterech cyfr.");
        }

        if (Bledy.Count > 0)
        {
            // Przy błędzie zostawiamy to, co wpisał użytkownik, ale stan
            // tokena odczytujemy z bazy - token nie pochodzi z formularza.
            OdczytajStanTokena(firma);
            return Page();
        }

        firma.Nazwa = Nazwa.Trim();
        firma.Nip = nip;
        firma.KodKraju = KodKraju!.ToUpperInvariant();
        firma.AdresLinia1 = AdresLinia1.Trim();
        firma.AdresLinia2 = Puste(AdresLinia2);
        firma.Email = Puste(Email);
        firma.Telefon = Puste(Telefon);
        firma.RachunekBankowy = rachunek.Length > 0 ? rachunek : null;
        firma.NazwaBanku = Puste(NazwaBanku);
        firma.MiejsceWystawienia = Puste(MiejsceWystawienia);
        firma.StopkaFaktury = Puste(StopkaFaktury);
        firma.DomyslnyTerminPlatnosciDni = DomyslnyTerminPlatnosciDni;
        firma.Srodowisko = Srodowisko;
        firma.TypOkresuVat = TypOkresuVat;
        firma.KodUrzeduSkarbowego = Puste(KodUrzeduSkarbowego);

        if (UsunToken)
        {
            firma.TokenKsefZaszyfrowany = null;
        }
        else if (!string.IsNullOrWhiteSpace(TokenKsef))
        {
            firma.TokenKsefZaszyfrowany = ochronaTokena.Zaszyfruj(TokenKsef.Trim());
        }

        await baza.SaveChangesAsync(anulowanie);

        TempData["Komunikat"] = "Zapisano ustawienia firmy.";
        return RedirectToPage();
    }

    // ------------------------------------------------------------ pomocnicze

    private async Task<Firma> WczytajFirmeAsync(CancellationToken anulowanie) =>
        await baza.Firmy.SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

    private void Wypelnij(Firma firma)
    {
        Nazwa = firma.Nazwa;
        Nip = firma.Nip;
        KodKraju = firma.KodKraju;
        AdresLinia1 = firma.AdresLinia1;
        AdresLinia2 = firma.AdresLinia2 ?? string.Empty;
        Email = firma.Email ?? string.Empty;
        Telefon = firma.Telefon ?? string.Empty;
        RachunekBankowy = firma.RachunekBankowy ?? string.Empty;
        NazwaBanku = firma.NazwaBanku ?? string.Empty;
        MiejsceWystawienia = firma.MiejsceWystawienia ?? string.Empty;
        StopkaFaktury = firma.StopkaFaktury ?? string.Empty;
        DomyslnyTerminPlatnosciDni = firma.DomyslnyTerminPlatnosciDni;
        Srodowisko = firma.Srodowisko;
        TypOkresuVat = firma.TypOkresuVat;
        KodUrzeduSkarbowego = firma.KodUrzeduSkarbowego ?? string.Empty;

        OdczytajStanTokena(firma);
    }

    private void OdczytajStanTokena(Firma firma)
    {
        TokenZapisany = firma.TokenKsefZaszyfrowany is { Length: > 0 };
        TokenZeSrodowiska = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("KSEF_TOKEN"));
    }

    private static string? Puste(string? wartosc) =>
        string.IsNullOrWhiteSpace(wartosc) ? null : wartosc.Trim();
}
