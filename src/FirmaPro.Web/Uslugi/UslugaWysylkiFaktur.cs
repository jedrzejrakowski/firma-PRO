using System.Globalization;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Wysyłanie faktury kontrahentowi pocztą.
/// </summary>
/// <remarks>
/// <para>
/// Kontrahent dostaje z KSeF plik XML, ale chce czegoś, co da się przeczytać
/// i podpiąć pod przelew. Program wysyła więc wizualizację PDF - tę samą,
/// którą można pobrać ręcznie - a plik XML tylko na życzenie.
/// </para>
/// <para>
/// Wysyłka do kontrahenta to zupełnie co innego niż wysyłka do KSeF.
/// Ta pierwsza jest grzecznością wobec klienta i można ją powtórzyć;
/// druga wprowadza fakturę do obiegu prawnego i zdarza się raz.
/// </para>
/// </remarks>
public sealed class UslugaWysylkiFaktur(
    FirmaProDbContext baza,
    UslugaFaktur uslugaFaktur,
    INadawcaPoczty poczta,
    TimeProvider czas)
{
    /// <summary>Czy poczta jest skonfigurowana.</summary>
    public bool PocztaDziala => poczta.Dziala;

    public async Task<IReadOnlyList<WyslanieFaktury>> HistoriaAsync(
        Guid fakturaId, CancellationToken anulowanie = default) =>
        await baza.WysylkiFaktur
            .Where(w => w.FakturaId == fakturaId)
            .OrderByDescending(w => w.WyslanoUtc)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    /// <summary>Domyślny adres odbiorcy - z kartoteki kontrahenta.</summary>
    public async Task<string?> AdresKontrahentaAsync(Guid fakturaId,
                                                     CancellationToken anulowanie = default)
    {
        Guid? kontrahentId = await baza.FakturySprzedazy
            .Where(f => f.Id == fakturaId)
            .Select(f => f.KontrahentId)
            .FirstOrDefaultAsync(anulowanie);

        if (kontrahentId is null)
        {
            return null;
        }

        return await baza.Kontrahenci
            .Where(k => k.Id == kontrahentId)
            .Select(k => k.Email)
            .FirstOrDefaultAsync(anulowanie);
    }

    /// <summary>Wysyła fakturę pod wskazany adres.</summary>
    public async Task<WynikKonta<WyslanieFaktury>> WyslijAsync(
        Guid fakturaId,
        string adres,
        string? wiadomosc,
        bool dolaczXml,
        Guid? ktoWysyla,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();
        string odbiorca = (adres ?? string.Empty).Trim();

        if (!poczta.Dziala)
        {
            walidacja.Blad("Poczta",
                "poczta nie jest skonfigurowana - fakturę trzeba na razie wysłać samemu");
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        if (odbiorca.Length == 0 || !odbiorca.Contains('@', StringComparison.Ordinal))
        {
            walidacja.Blad("Adres", "podaj poprawny adres e-mail odbiorcy");
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        FakturaSprzedazy? faktura = await baza.FakturySprzedazy
            .FirstOrDefaultAsync(f => f.Id == fakturaId, anulowanie);

        if (faktura is null)
        {
            walidacja.Blad("Faktura", "nie znaleziono faktury");
            return new WynikKonta<WyslanieFaktury>(null, walidacja);
        }

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == faktura.FirmaId, anulowanie);

        var zalaczniki = new List<Zalacznik>
        {
            new(NazwaPliku(faktura, "pdf"),
                await uslugaFaktur.ZbudujPdfAsync(fakturaId, anulowanie),
                "application/pdf")
        };

        if (dolaczXml)
        {
            zalaczniki.Add(new Zalacznik(NazwaPliku(faktura, "xml"),
                await uslugaFaktur.ZbudujXmlAsync(fakturaId, anulowanie),
                "application/xml"));
        }

        await poczta.WyslijAsync(
            odbiorca,
            $"Faktura {faktura.Numer} - {firma.Nazwa}",
            Tresc(faktura, firma, wiadomosc),
            zalaczniki,
            anulowanie);

        var wyslanie = new WyslanieFaktury
        {
            FakturaId = fakturaId,
            Adres = odbiorca,
            UzytkownikId = ktoWysyla,
            WyslanoUtc = czas.GetUtcNow(),
            ZXmlem = dolaczXml
        };

        baza.WysylkiFaktur.Add(wyslanie);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<WyslanieFaktury>(wyslanie, walidacja);
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>
    /// Nazwa pliku załącznika.
    /// </summary>
    /// <remarks>
    /// Numer faktury zawiera ukośniki, które w nazwie pliku znaczą co innego -
    /// zamieniamy je na podkreślenia, żeby załącznik dało się zapisać na
    /// każdym systemie.
    /// </remarks>
    private static string NazwaPliku(FakturaSprzedazy faktura, string rozszerzenie)
    {
        string numer = new(faktura.Numer
            .Select(z => char.IsLetterOrDigit(z) || z is '-' or '_' ? z : '_')
            .ToArray());

        return $"Faktura_{numer}.{rozszerzenie}";
    }

    /// <summary>
    /// Zapis kwot dla człowieka: przecinek dziesiętny i spacja co trzy cyfry.
    /// </summary>
    /// <remarks>
    /// Wewnątrz programu liczby chodzą w zapisie niezależnym od języka, bo tak
    /// wymaga formularz przeglądarki. Wiadomość czyta jednak kontrahent, więc
    /// kwota ma wyglądać tak, jak na wydruku faktury.
    /// </remarks>
    private static readonly NumberFormatInfo FormatKwot = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        NumberGroupSizes = [3]
    };

    private static string Tresc(FakturaSprzedazy faktura, Firma firma, string? wiadomosc)
    {
        string kwota = faktura.RazemBrutto.ToString("N2", FormatKwot);

        string wstep = string.IsNullOrWhiteSpace(wiadomosc)
            ? "W załączeniu przesyłamy fakturę."
            : wiadomosc.Trim();

        // Podsumowanie składamy tylko z tego, co naprawdę mamy - wiersz
        // „Termin płatności:" bez daty wyglądałby na usterkę programu.
        var podsumowanie = new List<string>
        {
            $"Faktura {faktura.Numer} z dnia {faktura.DataWystawienia:yyyy-MM-dd}",
            $"Do zapłaty: {kwota} {faktura.Waluta}"
        };

        if (faktura.TerminPlatnosci is DateOnly termin)
        {
            podsumowanie.Add($"Termin płatności: {termin:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(firma.RachunekBankowy))
        {
            podsumowanie.Add($"Numer rachunku: {firma.RachunekBankowy}");
        }

        return string.Join(Environment.NewLine,
        [
            "Dzień dobry,",
            string.Empty,
            wstep,
            string.Empty,
            .. podsumowanie,
            string.Empty,
            "Pozdrawiamy,",
            firma.Nazwa
        ]);
    }
}
