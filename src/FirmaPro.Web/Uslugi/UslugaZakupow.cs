using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Kwoty faktury zakupu w jednej stawce - dane z formularza.</summary>
public sealed record KwotaZakupu(string KodStawki, decimal Netto, decimal Vat);

/// <summary>Wynik próby zapisania faktury zakupu.</summary>
public sealed record WynikZapisuZakupu(FakturaZakupu? Faktura, WynikWalidacji Walidacja)
{
    public bool Udalo => Faktura is not null;
}

/// <summary>
/// Wprowadzanie faktur zakupu do rejestru.
/// </summary>
/// <remarks>
/// Faktury zakupu nie wystawiamy - przepisujemy ją z dokumentu otrzymanego
/// od dostawcy. Dlatego zamiast pozycji towarowych wprowadza się kwoty
/// w rozbiciu na stawki: tyle wystarczy rejestrowi VAT i deklaracji, a to
/// one są tu celem.
/// </remarks>
public sealed class UslugaZakupow(FirmaProDbContext baza)
{
    public async Task<WynikZapisuZakupu> ZapiszAsync(
        string numer,
        DateOnly dataWystawienia,
        DateOnly dataWplywu,
        DateOnly? dataObowiazkuPodatkowego,
        DateOnly? dataUjecia,
        Guid? kontrahentId,
        string sprzedawcaNazwa,
        string? sprzedawcaNip,
        RodzajZakupu rodzaj,
        bool odliczany,
        IReadOnlyList<KwotaZakupu> kwoty,
        string? uwagi,
        CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(kwoty);

        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        // Gdy nie podano daty obowiązku u sprzedawcy, przyjmujemy datę
        // wystawienia - dla typowej faktury to ten sam dzień co dostawa.
        DateOnly obowiazek = dataObowiazkuPodatkowego ?? dataWystawienia;

        OkresRozliczeniowy najwczesniejszy = TerminyVat.NajwczesniejszyOkresOdliczenia(
            obowiazek, dataWplywu, firma.TypOkresuVat);

        DateOnly ujecie = dataUjecia ?? najwczesniejszy.PierwszyDzien;

        WynikWalidacji walidacja = Sprawdz(numer, sprzedawcaNazwa, sprzedawcaNip,
            dataWystawienia, dataWplywu, obowiazek, ujecie, odliczany,
            firma.TypOkresuVat, kwoty);

        if (walidacja.SaBledy)
        {
            return new WynikZapisuZakupu(null, walidacja);
        }

        var faktura = new FakturaZakupu
        {
            Numer = numer.Trim(),
            DataWystawienia = dataWystawienia,
            DataWplywu = dataWplywu,
            DataObowiazkuPodatkowego = obowiazek,
            DataUjecia = ujecie,
            KontrahentId = kontrahentId,
            SprzedawcaNazwa = sprzedawcaNazwa.Trim(),
            SprzedawcaNip = string.IsNullOrWhiteSpace(sprzedawcaNip)
                ? null
                : new string(sprzedawcaNip.Where(char.IsDigit).ToArray()),
            Rodzaj = rodzaj,
            Odliczany = odliczany,
            Uwagi = string.IsNullOrWhiteSpace(uwagi) ? null : uwagi.Trim()
        };

        foreach (KwotaZakupu kwota in kwoty.Where(k => k.Netto != 0 || k.Vat != 0))
        {
            faktura.Kwoty.Add(new KwotaVatZakupu
            {
                KodStawki = kwota.KodStawki,
                Netto = Kwoty.Zaokraglij(kwota.Netto),
                Vat = Kwoty.Zaokraglij(kwota.Vat)
            });
        }

        faktura.RazemNetto = faktura.Kwoty.Sum(k => k.Netto);
        faktura.RazemVat = faktura.Kwoty.Sum(k => k.Vat);
        faktura.RazemBrutto = faktura.RazemNetto + faktura.RazemVat;

        baza.FakturyZakupu.Add(faktura);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikZapisuZakupu(faktura, walidacja);
    }

    public async Task<IReadOnlyList<FakturaZakupu>> ListaAsync(
        CancellationToken anulowanie = default) =>
        await baza.FakturyZakupu
            .Include(f => f.Kwoty)
            .OrderByDescending(f => f.DataUjecia)
            .ThenByDescending(f => f.DataWystawienia)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    public async Task<bool> UsunAsync(Guid id, CancellationToken anulowanie = default)
    {
        FakturaZakupu? faktura = await baza.FakturyZakupu
            .FirstOrDefaultAsync(f => f.Id == id, anulowanie);

        if (faktura is null)
        {
            return false;
        }

        baza.FakturyZakupu.Remove(faktura);
        await baza.SaveChangesAsync(anulowanie);

        return true;
    }

    // ------------------------------------------------------------ walidacja

    private static WynikWalidacji Sprawdz(
        string numer, string sprzedawcaNazwa, string? sprzedawcaNip,
        DateOnly dataWystawienia, DateOnly dataWplywu, DateOnly obowiazek,
        DateOnly ujecie, bool odliczany, TypOkresu typ,
        IReadOnlyList<KwotaZakupu> kwoty)
    {
        var wynik = new WynikWalidacji();

        if (string.IsNullOrWhiteSpace(numer))
        {
            wynik.Blad("Numer", "numer faktury jest wymagany");
        }

        if (string.IsNullOrWhiteSpace(sprzedawcaNazwa))
        {
            wynik.Blad("Sprzedawca", "nazwa sprzedawcy jest wymagana");
        }

        string nip = new((sprzedawcaNip ?? string.Empty).Where(char.IsDigit).ToArray());
        if (nip.Length > 0 && !Walidator.NipPoprawny(nip))
        {
            wynik.Blad("SprzedawcaNip", $"numer NIP „{sprzedawcaNip}” ma błędną sumę kontrolną");
        }

        if (dataWplywu < dataWystawienia)
        {
            wynik.Ostrzez("DataWplywu",
                "faktura wpłynęła przed datą wystawienia - sprawdź, czy daty się nie pomyliły");
        }

        if (kwoty.All(k => k.Netto == 0 && k.Vat == 0))
        {
            wynik.Blad("Kwoty", "faktura musi mieć przynajmniej jedną kwotę");
        }

        foreach (KwotaZakupu kwota in kwoty.Where(k => k.Netto != 0 || k.Vat != 0))
        {
            if (!StawkaVat.TryZKodu(kwota.KodStawki, out StawkaVat? stawka))
            {
                wynik.Blad("Kwoty", $"nieznana stawka „{kwota.KodStawki}”");
                continue;
            }

            if (kwota.Netto < 0 || kwota.Vat < 0)
            {
                wynik.Blad("Kwoty", $"kwoty w stawce {stawka.Opis} nie mogą być ujemne");
            }

            // Kwota podatku z dokumentu może różnić się o grosz od wyliczonej -
            // sprzedawca mógł zaokrąglić inaczej. Większa różnica oznacza
            // zwykle pomyłkę przy przepisywaniu.
            decimal oczekiwany = stawka.PodatekOd(kwota.Netto);
            if (Math.Abs(kwota.Vat - oczekiwany) > 0.01m)
            {
                wynik.Ostrzez("Kwoty",
                    $"przy stawce {stawka.Opis} i kwocie netto {kwota.Netto:N2} podatek " +
                    $"wychodzi {oczekiwany:N2}, a wpisano {kwota.Vat:N2}");
            }
        }

        if (odliczany)
        {
            foreach (Problem problem in
                     TerminyVat.SprawdzOkresOdliczenia(obowiazek, dataWplywu, ujecie, typ)
                               .Problemy)
            {
                wynik.Blad(problem.Pole, problem.Komunikat);
            }
        }

        return wynik;
    }
}
