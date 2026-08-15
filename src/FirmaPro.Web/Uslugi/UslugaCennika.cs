using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Wynik próby zapisania pozycji cennika.</summary>
public sealed record WynikZapisuCennika(PozycjaCennika? Pozycja, WynikWalidacji Walidacja)
{
    public bool Udalo => Pozycja is not null;
}

/// <summary>
/// Kartoteka powtarzalnych pozycji faktury.
/// </summary>
/// <remarks>
/// <para>
/// Bez niej każdą fakturę wypełnia się od nowa: nazwa, jednostka, cena,
/// stawka. Przy jednej fakturze to drobiazg, przy dwudziestu miesięcznie
/// to główny powód, dla którego ktoś odkłada program i wraca do arkusza.
/// </para>
/// <para>
/// Kartoteka podaje wartości początkowe, a nie wiążące. Cenę na fakturze
/// wolno nadpisać - rabat dla stałego klienta nie może wymagać zakładania
/// drugiej pozycji w cenniku.
/// </para>
/// </remarks>
public sealed class UslugaCennika(FirmaProDbContext baza)
{
    /// <summary>Pozycje do wyboru przy wystawianiu faktury.</summary>
    public async Task<IReadOnlyList<PozycjaCennika>> DoWyboruAsync(
        CancellationToken anulowanie = default) =>
        await baza.Cennik
            .Where(p => p.Aktywna)
            .OrderBy(p => p.Nazwa)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    /// <summary>Cała kartoteka, razem z pozycjami wycofanymi.</summary>
    public async Task<IReadOnlyList<PozycjaCennika>> ListaAsync(
        CancellationToken anulowanie = default) =>
        await baza.Cennik
            .OrderBy(p => !p.Aktywna)
            .ThenBy(p => p.Nazwa)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    public async Task<PozycjaCennika?> ZnajdzAsync(Guid id,
                                                   CancellationToken anulowanie = default) =>
        await baza.Cennik.FirstOrDefaultAsync(p => p.Id == id, anulowanie);

    /// <summary>Dodaje pozycję albo poprawia istniejącą.</summary>
    public async Task<WynikZapisuCennika> ZapiszAsync(
        Guid? id,
        string nazwa,
        string jednostka,
        decimal cenaNetto,
        string kodStawki,
        string? gtu,
        string? pkwiu,
        string? cn,
        string? indeks,
        bool aktywna,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        string czystaNazwa = (nazwa ?? string.Empty).Trim();

        if (czystaNazwa.Length == 0)
        {
            walidacja.Blad("Nazwa", "nazwa pozycji jest wymagana");
        }

        // Cena zerowa bywa potrzebna - towar gratis, próbka, pozycja wyceniana
        // za każdym razem osobno. Ujemna nie znaczy nic.
        if (cenaNetto < 0)
        {
            walidacja.Blad("CenaNetto", "cena nie może być ujemna");
        }

        if (!StawkaVat.TryZKodu(kodStawki, out StawkaVat? _))
        {
            walidacja.Blad("KodStawki", $"nieznana stawka podatku „{kodStawki}”");
        }

        if (walidacja.SaBledy)
        {
            return new WynikZapisuCennika(null, walidacja);
        }

        PozycjaCennika pozycja;

        if (id is Guid istniejaca)
        {
            PozycjaCennika? znaleziona = await baza.Cennik
                .FirstOrDefaultAsync(p => p.Id == istniejaca, anulowanie);

            if (znaleziona is null)
            {
                // Filtr firmy nie przepuścił pozycji - albo jej nie ma, albo
                // należy do innej firmy. Dla użytkownika to ta sama sytuacja.
                walidacja.Blad("Pozycja", "nie znaleziono wskazanej pozycji");
                return new WynikZapisuCennika(null, walidacja);
            }

            pozycja = znaleziona;
        }
        else
        {
            pozycja = new PozycjaCennika();
            baza.Cennik.Add(pozycja);
        }

        pozycja.Nazwa = czystaNazwa;
        pozycja.Jednostka = string.IsNullOrWhiteSpace(jednostka) ? "szt." : jednostka.Trim();
        pozycja.CenaNetto = Kwoty.Zaokraglij(cenaNetto);
        pozycja.KodStawki = kodStawki;
        pozycja.Gtu = Puste(gtu);
        pozycja.Pkwiu = Puste(pkwiu);
        pozycja.Cn = Puste(cn);
        pozycja.Indeks = Puste(indeks);
        pozycja.Aktywna = aktywna;

        await baza.SaveChangesAsync(anulowanie);

        return new WynikZapisuCennika(pozycja, walidacja);
    }

    /// <summary>
    /// Wycofuje pozycję z użycia albo przywraca ją do niego.
    /// </summary>
    /// <remarks>
    /// Zamiast kasowania, bo skasowana pozycja zabrałaby ze sobą informację
    /// o tym, co i po ile sprzedawano. Wycofana znika z podpowiedzi przy
    /// wystawianiu, ale zostaje w kartotece.
    /// </remarks>
    public async Task<bool> PrzelaczAktywnoscAsync(Guid id,
                                                   CancellationToken anulowanie = default)
    {
        PozycjaCennika? pozycja = await baza.Cennik
            .FirstOrDefaultAsync(p => p.Id == id, anulowanie);

        if (pozycja is null)
        {
            return false;
        }

        pozycja.Aktywna = !pozycja.Aktywna;
        await baza.SaveChangesAsync(anulowanie);

        return true;
    }

    private static string? Puste(string? wartosc) =>
        string.IsNullOrWhiteSpace(wartosc) ? null : wartosc.Trim();
}
