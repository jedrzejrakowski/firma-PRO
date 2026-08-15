using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Rodzaj znalezionego dokumentu - decyduje o ikonie i adresie.</summary>
public enum RodzajWyniku
{
    FakturaSprzedazy,
    FakturaZakupu,
    Kontrahent
}

/// <summary>Jedno trafienie wyszukiwania.</summary>
/// <param name="Tytul">To, czego użytkownik szukał - numer albo nazwa.</param>
/// <param name="Opis">Wiersz drugi: kontrahent, data, kwota.</param>
/// <param name="Strona">Strona Razor, na którą prowadzi trafienie.</param>
/// <param name="Id">Klucz dokumentu; null, gdy strona go nie potrzebuje.</param>
public sealed record Wynik(
    RodzajWyniku Rodzaj,
    string Tytul,
    string Opis,
    string Strona,
    Guid? Id);

/// <summary>Trafienia pogrupowane rodzajami.</summary>
public sealed record WynikiSzukania(
    string Szukane,
    IReadOnlyList<Wynik> Sprzedaz,
    IReadOnlyList<Wynik> Zakupy,
    IReadOnlyList<Wynik> Kontrahenci)
{
    public static readonly WynikiSzukania Puste = new(string.Empty, [], [], []);

    public int Ile => Sprzedaz.Count + Zakupy.Count + Kontrahenci.Count;

    /// <summary>Wszystkie trafienia po kolei - do listy podpowiedzi.</summary>
    public IEnumerable<Wynik> Wszystkie => Sprzedaz.Concat(Zakupy).Concat(Kontrahenci);
}

/// <summary>Dokąd prowadzi kliknięcie w trafienie.</summary>
/// <remarks>
/// Faktura kosztowa i kontrahent nie mają własnych ekranów szczegółów, więc
/// prowadzimy na listę, na której są widoczne. Lepsze to niż wyłączenie ich
/// z wyszukiwania: pytanie „czy mam już tę fakturę od dostawcy” jest
/// równie częste jak pytanie o własną fakturę.
/// </remarks>
public static class OdnosnikWyniku
{
    public static string Dla(Wynik wynik) => wynik.Rodzaj switch
    {
        RodzajWyniku.FakturaSprzedazy => $"/Faktury/Szczegoly/{wynik.Id}",
        RodzajWyniku.FakturaZakupu => "/Faktury?widok=koszty",
        _ => "/Kontrahenci"
    };

    /// <summary>Nazwa rodzaju widoczna nad grupą wyników.</summary>
    public static string Nazwa(RodzajWyniku rodzaj) => rodzaj switch
    {
        RodzajWyniku.FakturaSprzedazy => "Faktury sprzedaży",
        RodzajWyniku.FakturaZakupu => "Faktury kosztowe",
        _ => "Kontrahenci"
    };
}

/// <summary>
/// Szukanie po całym programie: faktury, koszty i kartoteka kontrahentów.
/// </summary>
/// <remarks>
/// <para>
/// Dokumentu szuka się zwykle wtedy, gdy dzwoni kontrahent - i wtedy w ręku
/// jest jedna informacja: numer faktury, nazwa firmy, NIP albo numer KSeF
/// z potwierdzenia. Wyszukiwarka przyjmuje każdą z nich i nie wymaga
/// wcześniejszego wybrania, gdzie właściwie szukać.
/// </para>
/// <para>
/// Szukamy wzorcem „zawiera”, bo numery faktur ludzie pamiętają od środka
/// („ta ósemka z sierpnia”), a NIP bywa przepisywany bez kresek. Cena za to
/// jest taka, że zapytanie nie skorzysta z indeksu - dlatego wyniki są
/// przycięte i osobno liczone dla każdego rodzaju dokumentu.
/// </para>
/// </remarks>
public sealed class UslugaSzukania(FirmaProDbContext baza)
{
    /// <summary>Od ilu znaków w ogóle zaczynamy szukać.</summary>
    /// <remarks>
    /// Jedna litera pasuje do wszystkiego - przepuszczenie jej znaczyłoby
    /// przeczytanie całej bazy po to, żeby pokazać przypadkowe dziesięć
    /// dokumentów.
    /// </remarks>
    public const int NajkrotszeSzukane = 2;

    public async Task<WynikiSzukania> SzukajAsync(
        string? szukane, int ileNaGrupe = 8, CancellationToken anulowanie = default)
    {
        string fraza = (szukane ?? string.Empty).Trim();

        if (fraza.Length < NajkrotszeSzukane)
        {
            return WynikiSzukania.Puste;
        }

        // NIP użytkownik wpisze z kreskami albo bez - do porównania z bazą,
        // gdzie leżą same cyfry, zostawiamy wyłącznie cyfry.
        string cyfry = new(fraza.Where(char.IsDigit).ToArray());
        bool szukaNipu = cyfry.Length >= 3;

        List<Wynik> sprzedaz = await SprzedazAsync(fraza, cyfry, szukaNipu, ileNaGrupe, anulowanie);
        List<Wynik> zakupy = await ZakupyAsync(fraza, cyfry, szukaNipu, ileNaGrupe, anulowanie);
        List<Wynik> kontrahenci = await KontrahenciAsync(fraza, cyfry, szukaNipu, ileNaGrupe, anulowanie);

        return new WynikiSzukania(fraza, sprzedaz, zakupy, kontrahenci);
    }

    // ------------------------------------------------------------ pomocnicze

    private async Task<List<Wynik>> SprzedazAsync(
        string fraza, string cyfry, bool szukaNipu, int ile, CancellationToken anulowanie)
    {
        List<FakturaSprzedazy> znalezione = await baza.FakturySprzedazy
            .Where(f => EF.Functions.ILike(f.Numer, $"%{fraza}%")
                     || EF.Functions.ILike(f.NabywcaNazwa, $"%{fraza}%")
                     || (f.NumerKsef != null && EF.Functions.ILike(f.NumerKsef, $"%{fraza}%"))
                     || (szukaNipu && EF.Functions.ILike(f.NabywcaNip, $"%{cyfry}%")))
            .OrderByDescending(f => f.DataWystawienia)
            .ThenByDescending(f => f.UtworzonoUtc)
            .Take(ile)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        return [.. znalezione.Select(f => new Wynik(
            RodzajWyniku.FakturaSprzedazy,
            f.Numer,
            $"{f.NabywcaNazwa} · {f.DataWystawienia:yyyy-MM-dd} · " +
            $"{Domena.Kwoty.NaTekst(f.RazemBrutto)} {f.Waluta}",
            "/Faktury/Szczegoly",
            f.Id))];
    }

    private async Task<List<Wynik>> ZakupyAsync(
        string fraza, string cyfry, bool szukaNipu, int ile, CancellationToken anulowanie)
    {
        List<FakturaZakupu> znalezione = await baza.FakturyZakupu
            .Where(f => EF.Functions.ILike(f.Numer, $"%{fraza}%")
                     || EF.Functions.ILike(f.SprzedawcaNazwa, $"%{fraza}%")
                     || (f.NumerKsef != null && EF.Functions.ILike(f.NumerKsef, $"%{fraza}%"))
                     || (szukaNipu && f.SprzedawcaNip != null
                                   && EF.Functions.ILike(f.SprzedawcaNip, $"%{cyfry}%")))
            .OrderByDescending(f => f.DataWystawienia)
            .Take(ile)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        // Faktura kosztowa nie ma własnego ekranu - prowadzimy na zakładkę
        // kosztów, czyli tam, gdzie widać ją w rejestrze.
        return [.. znalezione.Select(f => new Wynik(
            RodzajWyniku.FakturaZakupu,
            f.Numer,
            $"{f.SprzedawcaNazwa} · {f.DataWystawienia:yyyy-MM-dd} · " +
            $"{Domena.Kwoty.NaTekst(f.RazemBrutto)} {f.Waluta}",
            "/Faktury/Index",
            null))];
    }

    private async Task<List<Wynik>> KontrahenciAsync(
        string fraza, string cyfry, bool szukaNipu, int ile, CancellationToken anulowanie)
    {
        List<Kontrahent> znalezieni = await baza.Kontrahenci
            .Where(k => EF.Functions.ILike(k.Nazwa, $"%{fraza}%")
                     || (szukaNipu && EF.Functions.ILike(k.Nip, $"%{cyfry}%")))
            .OrderBy(k => k.Nazwa)
            .Take(ile)
            .AsNoTracking()
            .ToListAsync(anulowanie);

        return [.. znalezieni.Select(k => new Wynik(
            RodzajWyniku.Kontrahent,
            k.Nazwa,
            k.Nip.Length > 0 ? $"NIP {k.Nip} · {k.AdresLinia1}" : k.AdresLinia1,
            "/Kontrahenci/Index",
            k.Id))];
    }
}
