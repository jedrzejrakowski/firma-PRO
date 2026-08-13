using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Sprawdzenie połączenia z KSeF krok po kroku.
/// </summary>
/// <remarks>
/// Ekran istnieje dla pierwszego uruchomienia u klienta. Do tej pory jedynym
/// sposobem sprawdzenia integracji było wystawienie faktury i wysłanie jej -
/// czyli operacja, której na produkcji nie da się cofnąć. Tutaj nic nie
/// powstaje: program przechodzi tę samą drogę, ale zatrzymuje się tuż przed
/// wysłaniem dokumentu.
/// </remarks>
public sealed class KsefModel(
    FirmaProDbContext baza,
    UslugaDiagnostykiKsef diagnostyka) : PageModel
{
    public string NazwaFirmy { get; private set; } = string.Empty;
    public string Nip { get; private set; } = string.Empty;
    public SrodowiskoKsef Srodowisko { get; private set; } = SrodowiskoKsef.Test;

    /// <summary>Wynik ostatniego sprawdzenia - pusty, dopóki nikt nie kliknął.</summary>
    public WynikDiagnostyki? Wynik { get; private set; }

    public async Task OnGetAsync(CancellationToken anulowanie) =>
        await WczytajFirmeAsync(anulowanie);

    public async Task OnPostAsync(CancellationToken anulowanie)
    {
        await WczytajFirmeAsync(anulowanie);
        Wynik = await diagnostyka.SprawdzAsync(anulowanie);
    }

    private async Task WczytajFirmeAsync(CancellationToken anulowanie)
    {
        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        NazwaFirmy = firma.Nazwa;
        Nip = firma.Nip;
        Srodowisko = firma.Srodowisko;
    }

    /// <summary>Nazwa klasy CSS odpowiadająca stanowi kroku.</summary>
    public static string KlasaStanu(StanKroku stan) => stan switch
    {
        StanKroku.Ok => "znacznik-przyjeta",
        StanKroku.Ostrzezenie => "znacznik-robocza",
        StanKroku.Blad => "znacznik-odrzucona",
        _ => "znacznik-robocza"
    };

    /// <summary>Krótki opis stanu pokazywany przy kroku.</summary>
    public static string OpisStanu(StanKroku stan) => stan switch
    {
        StanKroku.Ok => "działa",
        StanKroku.Ostrzezenie => "uwaga",
        StanKroku.Blad => "błąd",
        _ => "pominięto"
    };
}
