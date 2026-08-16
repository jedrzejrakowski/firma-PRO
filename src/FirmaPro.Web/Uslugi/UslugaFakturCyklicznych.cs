using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Wiersz wzorca przychodzący z formularza.</summary>
public sealed record WierszWzorca(string Nazwa, string Jednostka, decimal Ilosc,
                                  decimal CenaNetto, string KodStawki, string? Gtu);

/// <summary>Wynik próby zapisania wzorca.</summary>
public sealed record WynikZapisuWzorca(WzorzecCykliczny? Wzorzec, WynikWalidacji Walidacja)
{
    public bool Udalo => Wzorzec is not null;
}

/// <summary>Wzorzec, z którego trzeba wystawić fakturę.</summary>
/// <param name="Zalegle">
/// Ile faktur z tego wzorca powinno już być wystawionych - po dłuższej
/// przerwie w pracy bywa ich kilka.
/// </param>
public sealed record DoWystawienia(WzorzecCykliczny Wzorzec, int Zalegle);

/// <summary>
/// Faktury wystawiane cyklicznie.
/// </summary>
/// <remarks>
/// <para>
/// Program <b>nie wystawia faktur sam</b>. Pokazuje, co czeka na wystawienie,
/// a dokument powstaje dopiero po kliknięciu. Faktura jest dokumentem
/// prawnym: po wysłaniu do KSeF nie da się jej wycofać, a jedynie
/// skorygować - więc automat, który wystawia ją w tle za nieistniejącą już
/// usługę albo dla klienta, który wczoraj wypowiedział umowę, kosztuje
/// więcej niż oszczędza.
/// </para>
/// <para>
/// Sama faktura powstaje przez zwykłą usługę wystawiania, tę samą co przy
/// ręcznym wypełnieniu formularza. Dzięki temu numeracja, walidacja i podatek
/// liczą się w jednym miejscu, a nie w dwóch, które prędzej czy później by
/// się rozjechały.
/// </para>
/// </remarks>
public sealed class UslugaFakturCyklicznych(
    FirmaProDbContext baza,
    UslugaFaktur uslugaFaktur,
    TimeProvider czas)
{
    private DateOnly Dzisiaj => DateOnly.FromDateTime(czas.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<WzorzecCykliczny>> ListaAsync(
        CancellationToken anulowanie = default) =>
        await baza.WzorceCykliczne
            .Include(w => w.Kontrahent)
            .Include(w => w.Pozycje)
            .OrderBy(w => !w.Aktywny)
            .ThenBy(w => w.NastepneWystawienie)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    public async Task<WzorzecCykliczny?> ZnajdzAsync(Guid id,
                                                     CancellationToken anulowanie = default) =>
        await baza.WzorceCykliczne
            .Include(w => w.Pozycje)
            .FirstOrDefaultAsync(w => w.Id == id, anulowanie);

    /// <summary>Wzorce, z których wypada już wystawić fakturę.</summary>
    public async Task<IReadOnlyList<DoWystawienia>> DoWystawieniaAsync(
        CancellationToken anulowanie = default)
    {
        DateOnly dzisiaj = Dzisiaj;

        List<WzorzecCykliczny> wzorce = await baza.WzorceCykliczne
            .Include(w => w.Kontrahent)
            .Where(w => w.Aktywny && w.NastepneWystawienie <= dzisiaj)
            .OrderBy(w => w.NastepneWystawienie)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        return
        [
            .. wzorce
                .Select(w => new DoWystawienia(w, Cyklicznosc.IleZaleglych(
                    w.NastepneWystawienie, dzisiaj, w.DzienMiesiaca, w.Rytm, w.Do)))
                .Where(d => d.Zalegle > 0)
        ];
    }

    /// <summary>Ile faktur czeka na wystawienie - licznik na pulpicie.</summary>
    public async Task<int> IleCzekaAsync(CancellationToken anulowanie = default)
    {
        DateOnly dzisiaj = Dzisiaj;

        return await baza.WzorceCykliczne
            .CountAsync(w => w.Aktywny && w.NastepneWystawienie <= dzisiaj, anulowanie);
    }

    /// <summary>Zakłada wzorzec albo poprawia istniejący.</summary>
    public async Task<WynikZapisuWzorca> ZapiszAsync(
        Guid? id,
        string nazwa,
        Guid kontrahentId,
        RytmFaktury rytm,
        int dzienMiesiaca,
        int terminPlatnosciDni,
        FormaPlatnosci? formaPlatnosci,
        DateOnly od,
        DateOnly? doKiedy,
        bool aktywny,
        IReadOnlyList<WierszWzorca> pozycje,
        CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(pozycje);

        var walidacja = new WynikWalidacji();
        string czystaNazwa = (nazwa ?? string.Empty).Trim();

        if (czystaNazwa.Length == 0)
        {
            walidacja.Blad("Nazwa", "nazwa wzorca jest wymagana");
        }

        if (!await baza.Kontrahenci.AnyAsync(k => k.Id == kontrahentId, anulowanie))
        {
            walidacja.Blad("KontrahentId", "nie znaleziono wskazanego kontrahenta");
        }

        if (dzienMiesiaca is < Cyklicznosc.OstatniDzien or > 28)
        {
            // Powyżej 28 dnia nie ma w każdym miesiącu, a faktura wystawiona
            // „czasem 30., czasem 28." wprowadzałaby zamęt w terminach.
            // Kto chce końca miesiąca, wybiera „ostatni dzień".
            walidacja.Blad("DzienMiesiaca",
                "dzień wystawienia musi mieścić się w przedziale 1-28 " +
                "albo oznaczać ostatni dzień miesiąca");
        }

        if (terminPlatnosciDni is < 0 or > 365)
        {
            walidacja.Blad("TerminPlatnosciDni",
                "termin płatności musi mieścić się w przedziale 0-365 dni");
        }

        if (doKiedy is DateOnly koniec && koniec < od)
        {
            walidacja.Blad("Do", "data zakończenia nie może być wcześniejsza niż rozpoczęcia");
        }

        List<WierszWzorca> wiersze = [.. pozycje
            .Where(p => !string.IsNullOrWhiteSpace(p.Nazwa))];

        if (wiersze.Count == 0)
        {
            walidacja.Blad("Pozycje", "wzorzec musi mieć przynajmniej jedną pozycję");
        }

        foreach (WierszWzorca wiersz in wiersze)
        {
            if (!StawkaVat.TryZKodu(wiersz.KodStawki, out StawkaVat? _))
            {
                walidacja.Blad("Pozycje", $"nieznana stawka podatku „{wiersz.KodStawki}”");
            }
        }

        if (walidacja.SaBledy)
        {
            return new WynikZapisuWzorca(null, walidacja);
        }

        WzorzecCykliczny wzorzec;

        if (id is Guid istniejacy)
        {
            WzorzecCykliczny? znaleziony = await baza.WzorceCykliczne
                .Include(w => w.Pozycje)
                .FirstOrDefaultAsync(w => w.Id == istniejacy, anulowanie);

            if (znaleziony is null)
            {
                walidacja.Blad("Wzorzec", "nie znaleziono wskazanego wzorca");
                return new WynikZapisuWzorca(null, walidacja);
            }

            wzorzec = znaleziony;
            baza.PozycjeWzorcow.RemoveRange(wzorzec.Pozycje);
            wzorzec.Pozycje.Clear();
        }
        else
        {
            wzorzec = new WzorzecCykliczny();
            baza.WzorceCykliczne.Add(wzorzec);
        }

        wzorzec.Nazwa = czystaNazwa;
        wzorzec.KontrahentId = kontrahentId;
        wzorzec.Rytm = rytm;
        wzorzec.DzienMiesiaca = dzienMiesiaca;
        wzorzec.TerminPlatnosciDni = terminPlatnosciDni;
        wzorzec.FormaPlatnosci = formaPlatnosci;
        wzorzec.Od = od;
        wzorzec.Do = doKiedy;
        wzorzec.Aktywny = aktywny;

        // Datę najbliższej faktury wyliczamy tylko dla nowego wzorca oraz
        // wtedy, gdy jeszcze z niego nic nie poszło. Przy poprawianiu wzorca,
        // z którego już wystawiono faktury, przesunięcie terminu wstecz
        // kazałoby wystawić je po raz drugi.
        if (wzorzec.OstatnieWystawienie is null)
        {
            wzorzec.NastepneWystawienie =
                Cyklicznosc.PierwszaData(od, dzienMiesiaca, rytm);
        }

        int numer = 1;

        foreach (WierszWzorca wiersz in wiersze)
        {
            wzorzec.Pozycje.Add(new PozycjaWzorca
            {
                NrWiersza = numer++,
                Nazwa = wiersz.Nazwa.Trim(),
                Jednostka = string.IsNullOrWhiteSpace(wiersz.Jednostka)
                    ? "szt."
                    : wiersz.Jednostka.Trim(),
                Ilosc = wiersz.Ilosc,
                CenaNetto = wiersz.CenaNetto,
                KodStawki = wiersz.KodStawki,
                Gtu = string.IsNullOrWhiteSpace(wiersz.Gtu) ? null : wiersz.Gtu
            });
        }

        await baza.SaveChangesAsync(anulowanie);

        return new WynikZapisuWzorca(wzorzec, walidacja);
    }

    /// <summary>
    /// Wystawia jedną fakturę z wzorca i przesuwa termin na kolejny okres.
    /// </summary>
    /// <remarks>
    /// Jedno kliknięcie to jedna faktura, także wtedy, gdy zaległych jest
    /// kilka. Wystawienie ich hurtem jednym przyciskiem byłoby wygodne do
    /// chwili pierwszej pomyłki - a każda z nich to osobny dokument, który
    /// trzeba by potem korygować.
    /// </remarks>
    public async Task<WynikWystawienia> WystawAsync(Guid wzorzecId,
                                                    CancellationToken anulowanie = default)
    {
        WzorzecCykliczny? wzorzec = await baza.WzorceCykliczne
            .Include(w => w.Pozycje)
            .FirstOrDefaultAsync(w => w.Id == wzorzecId, anulowanie);

        if (wzorzec is null)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Wzorzec", "nie znaleziono wskazanego wzorca"));
        }

        if (!wzorzec.Aktywny)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Wzorzec", "wzorzec jest wstrzymany"));
        }

        DateOnly data = wzorzec.NastepneWystawienie;

        if (data > Dzisiaj)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Wzorzec",
                $"kolejna faktura z tego wzorca wypada {data:yyyy-MM-dd}"));
        }

        if (wzorzec.Do is DateOnly koniec && data > koniec)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Wzorzec", "wzorzec przestał obowiązywać"));
        }

        WynikWystawienia wynik = await uslugaFaktur.WystawAsync(
            wzorzec.KontrahentId,
            data,
            data,
            data.AddDays(wzorzec.TerminPlatnosciDni),
            wzorzec.FormaPlatnosci,
            null,
            [.. wzorzec.Pozycje
                .OrderBy(p => p.NrWiersza)
                .Select(p => (p.Nazwa, p.Jednostka, p.Ilosc, p.CenaNetto, p.KodStawki, p.Gtu))],
            anulowanie: anulowanie);

        if (!wynik.Udalo)
        {
            return wynik;
        }

        wzorzec.OstatnieWystawienie = data;
        wzorzec.NastepneWystawienie =
            Cyklicznosc.Nastepna(data, wzorzec.DzienMiesiaca, wzorzec.Rytm);

        await baza.SaveChangesAsync(anulowanie);

        return wynik;
    }

    /// <summary>Wstrzymuje wzorzec albo wznawia go.</summary>
    public async Task<bool> PrzelaczAktywnoscAsync(Guid id,
                                                   CancellationToken anulowanie = default)
    {
        WzorzecCykliczny? wzorzec = await baza.WzorceCykliczne
            .FirstOrDefaultAsync(w => w.Id == id, anulowanie);

        if (wzorzec is null)
        {
            return false;
        }

        wzorzec.Aktywny = !wzorzec.Aktywny;
        await baza.SaveChangesAsync(anulowanie);

        return true;
    }
}
