using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Składa rejestr VAT z dokumentów zapisanych w bazie.
/// </summary>
/// <remarks>
/// Rejestr nie jest osobno przechowywany - powstaje z faktur przy każdym
/// otwarciu ekranu. Dzięki temu poprawka w dokumencie widać od razu
/// w rejestrze i nie ma dwóch źródeł prawdy, które mogłyby się rozjechać.
/// </remarks>
public sealed class UslugaRejestruVat(FirmaProDbContext baza)
{
    /// <summary>Rytm rozliczania VAT wybrany przez firmę.</summary>
    public async Task<TypOkresu> TypOkresuAsync(CancellationToken anulowanie = default)
    {
        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        return firma.TypOkresuVat;
    }

    /// <summary>Buduje komplet rejestrów za wskazany okres.</summary>
    public async Task<RejestrVat> ZbudujAsync(OkresRozliczeniowy okres,
                                              CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(okres);

        DateOnly od = okres.PierwszyDzien;
        DateOnly Do = okres.OstatniDzien;

        // Wybór idzie po dacie ujęcia, nie po dacie wystawienia: faktura
        // wystawiona 3 września za usługę wykonaną 28 sierpnia należy
        // do sierpnia.
        List<FakturaSprzedazy> sprzedaz = await baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .Where(f => f.DataUjeciaVat >= od && f.DataUjeciaVat <= Do)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        List<FakturaZakupu> zakupy = await baza.FakturyZakupu
            .Include(f => f.Kwoty)
            .Where(f => f.DataUjecia >= od && f.DataUjecia <= Do)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        return RejestrVat.Zbuduj(okres, sprzedaz.Select(NaWpis), zakupy.Select(NaWpis));
    }

    /// <summary>
    /// Zamienia fakturę sprzedaży na wpis rejestru.
    /// </summary>
    /// <remarks>
    /// Podatek liczymy od sumy wartości netto w danej stawce, a nie jako sumę
    /// podatków z pozycji. Tak wylicza go faktura (art. 106e) i tak musi się
    /// zgadzać rejestr - przy kilku drobnych pozycjach obie drogi dają różne
    /// grosze.
    /// </remarks>
    private static WpisSprzedazy NaWpis(FakturaSprzedazy faktura)
    {
        // Faktura korygująca niesie pozycje w dwóch wersjach: sprzed zmiany
        // i po niej. Do rejestru wchodzi różnica, bo tylko o tyle zmienia się
        // podatek - wykazanie nowego stanu policzyłoby sprzedaż drugi raz.
        List<KwotyWStawce> wedlugStawek = faktura.Pozycje
            .GroupBy(p => p.KodStawki, StringComparer.Ordinal)
            .Select(grupa =>
            {
                StawkaVat stawka = StawkaVat.ZKodu(grupa.Key);

                decimal netto = Kwoty.Zaokraglij(
                    grupa.Where(p => !p.StanPrzed).Sum(p => p.WartoscNetto)
                    - grupa.Where(p => p.StanPrzed).Sum(p => p.WartoscNetto));

                return new KwotyWStawce(stawka, netto, stawka.PodatekOd(netto));
            })
            .Where(k => k.Netto != 0 || k.Vat != 0)
            .OrderBy(k => k.Stawka.Kolejnosc)
            .ToList();

        return new WpisSprzedazy(
            faktura.Numer,
            faktura.DataWystawienia,
            faktura.DataUjeciaVat,
            faktura.NabywcaNazwa,
            string.IsNullOrWhiteSpace(faktura.NabywcaNip) ? null : faktura.NabywcaNip,
            faktura.NumerKsef,
            wedlugStawek);
    }

    private static WpisZakupu NaWpis(FakturaZakupu faktura) =>
        new(faktura.Numer,
            faktura.DataWystawienia,
            faktura.DataWplywu,
            faktura.DataUjecia,
            faktura.SprzedawcaNazwa,
            faktura.SprzedawcaNip,
            faktura.Rodzaj,
            faktura.Odliczany,
            faktura.RazemNetto,
            faktura.RazemVat);
}
