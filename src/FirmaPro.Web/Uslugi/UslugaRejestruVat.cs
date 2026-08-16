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
            .Include(f => f.RozliczoneZaliczki)
            .Where(f => f.DataUjeciaVat >= od && f.DataUjeciaVat <= Do)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        List<FakturaZakupu> zakupy = await baza.FakturyZakupu
            .Include(f => f.Kwoty)
            .Where(f => f.DataUjecia >= od && f.DataUjecia <= Do)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        Dictionary<Guid, Dictionary<string, decimal>> zaliczki =
            await ZafakturowaneZaliczkiAsync(sprzedaz, anulowanie);

        return RejestrVat.Zbuduj(
            okres,
            sprzedaz.Select(f => NaWpis(f, zaliczki.GetValueOrDefault(f.Id))),
            zakupy.Select(NaWpis));
    }

    /// <summary>
    /// Wartości netto zaliczek rozliczonych fakturami końcowymi, w podziale
    /// na stawki - osobno dla każdej faktury końcowej.
    /// </summary>
    /// <remarks>
    /// Podatek od zaliczki wykazano już w miesiącu jej otrzymania. Gdyby
    /// faktura końcowa weszła do rejestru całą wartością dostawy, ta sama
    /// sprzedaż trafiłaby do podstawy opodatkowania dwa razy.
    /// </remarks>
    private async Task<Dictionary<Guid, Dictionary<string, decimal>>>
        ZafakturowaneZaliczkiAsync(List<FakturaSprzedazy> sprzedaz,
                                   CancellationToken anulowanie)
    {
        List<Guid> zaliczkoweId = [.. sprzedaz
            .SelectMany(f => f.RozliczoneZaliczki)
            .Select(z => z.ZaliczkowaId)
            .Distinct()];

        if (zaliczkoweId.Count == 0)
        {
            return [];
        }

        // Zaliczkę przeliczamy jej własnym kursem, a nie kursem faktury
        // końcowej. Podatek od zaliczki wykazano w miesiącu jej otrzymania
        // i po tamtym kursie - odejmowanie kwoty przeliczonej inaczej
        // zostawiłoby w rejestrze różnicę kursową udającą sprzedaż.
        List<FakturaSprzedazy> zaliczkowe = await baza.FakturySprzedazy
            .Where(f => zaliczkoweId.Contains(f.Id))
            .Include(f => f.Pozycje)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        Dictionary<Guid, FakturaSprzedazy> wedlugId =
            zaliczkowe.ToDictionary(f => f.Id);

        Dictionary<Guid, Dictionary<string, decimal>> wynik = [];

        foreach (FakturaSprzedazy koncowa in sprzedaz.Where(f => f.RozliczoneZaliczki.Count > 0))
        {
            Dictionary<string, decimal> wedlugStawek = new(StringComparer.Ordinal);

            foreach (RozliczonaZaliczka rozliczona in koncowa.RozliczoneZaliczki)
            {
                if (!wedlugId.TryGetValue(rozliczona.ZaliczkowaId,
                        out FakturaSprzedazy? zaliczkowa))
                {
                    continue;
                }

                foreach (PozycjaFakturySprzedazy pozycja in zaliczkowa.Pozycje)
                {
                    wedlugStawek[pozycja.KodStawki] =
                        wedlugStawek.GetValueOrDefault(pozycja.KodStawki)
                        + Przeliczenie.NaZlote(pozycja.WartoscNetto,
                            zaliczkowa.KursDoPrzeliczen);
                }
            }

            wynik[koncowa.Id] = wedlugStawek;
        }

        return wynik;
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
    /// <param name="zaliczki">
    /// Netto zafakturowanych już zaliczek w podziale na stawki - odejmowane
    /// od faktury końcowej. <c>null</c>, gdy faktura żadnych nie rozlicza.
    /// </param>
    private static WpisSprzedazy NaWpis(FakturaSprzedazy faktura,
                                        Dictionary<string, decimal>? zaliczki = null)
    {
        // Faktura korygująca niesie pozycje w dwóch wersjach: sprzed zmiany
        // i po niej. Do rejestru wchodzi różnica, bo tylko o tyle zmienia się
        // podatek - wykazanie nowego stanu policzyłoby sprzedaż drugi raz.
        Dictionary<string, List<PozycjaFakturySprzedazy>> wedlugKodu = faktura.Pozycje
            .GroupBy(p => p.KodStawki, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Zaliczka mogła być w stawce, której na fakturze końcowej już nie ma
        // - i tak trzeba ją odjąć, więc idziemy po sumie obu zbiorów stawek.
        IEnumerable<string> kody = zaliczki is null
            ? wedlugKodu.Keys
            : wedlugKodu.Keys.Union(zaliczki.Keys, StringComparer.Ordinal);

        List<KwotyWStawce> wedlugStawek = kody
            .Select(kod =>
            {
                StawkaVat stawka = StawkaVat.ZKodu(kod);
                List<PozycjaFakturySprzedazy> pozycje = wedlugKodu.GetValueOrDefault(kod, []);

                // Rejestr prowadzi się w złotych, także dla faktury wystawionej
                // w euro - podatek państwo pobiera w złotych. Przeliczamy przed
                // zaokrągleniem, żeby suma zgadzała się z sumą przeliczoną.
                decimal netto = Kwoty.Zaokraglij(
                    Przeliczenie.NaZlote(
                        pozycje.Where(p => !p.StanPrzed).Sum(p => p.WartoscNetto)
                        - pozycje.Where(p => p.StanPrzed).Sum(p => p.WartoscNetto),
                        faktura.KursDoPrzeliczen)
                    - (zaliczki?.GetValueOrDefault(kod) ?? 0m));

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
