using System.Buffers.Text;
using System.Security.Cryptography;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Wynik operacji na kontach - dane albo powód odmowy.</summary>
public sealed record WynikKonta<T>(T? Dane, WynikWalidacji Walidacja) where T : class
{
    public bool Udalo => Dane is not null;
}

/// <summary>
/// Zakładanie kont, zapraszanie współpracowników i zarządzanie dostępem.
/// </summary>
/// <remarks>
/// <para>
/// Wszystkie reguły dotyczące tego, kto ma dostęp do ksiąg firmy, siedzą
/// w jednym miejscu - inaczej łatwo o furtkę zostawioną w jednym ekranie
/// i zamkniętą w innym.
/// </para>
/// <para>
/// Zaproszenia i zmiany hasła załatwiane są jednorazowym odnośnikiem. Idzie
/// on pocztą, gdy jest skonfigurowana; bez niej właściciel przekazuje go sam.
/// Odnośnik jest wart tyle co hasło, dlatego traci ważność i działa raz.
/// </para>
/// </remarks>
public sealed class UslugaKont(
    FirmaProDbContext baza,
    IPasswordHasher<object> haszowanie,
    TimeProvider czas)
{
    /// <summary>Ile dni żyje zaproszenie, zanim przestanie działać.</summary>
    public const int DniWaznosciZaproszenia = 7;

    // ------------------------------------------------------------ rejestracja

    /// <summary>Zakłada konto wraz z nową firmą; zakładający zostaje właścicielem.</summary>
    public async Task<WynikKonta<CzlonkostwoWFirmie>> ZarejestrujAsync(
        string email,
        string haslo,
        string powtorzHaslo,
        string nazwaFirmy,
        string nip,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        string adres = (email ?? string.Empty).Trim();
        string czysteNip = new((nip ?? string.Empty).Where(char.IsDigit).ToArray());

        SprawdzAdres(walidacja, adres);
        SprawdzHaslo(walidacja, haslo, powtorzHaslo);

        if (string.IsNullOrWhiteSpace(nazwaFirmy))
        {
            walidacja.Blad("NazwaFirmy", "nazwa firmy jest wymagana");
        }

        if (!Walidator.NipPoprawny(czysteNip))
        {
            walidacja.Blad("Nip", "numer NIP ma błędną sumę kontrolną");
        }

        if (!walidacja.SaBledy && await AdresZajetyAsync(adres, anulowanie))
        {
            walidacja.Blad("Email", "konto o tym adresie już istnieje");
        }

        if (walidacja.SaBledy)
        {
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        var firma = new Firma
        {
            Nazwa = nazwaFirmy.Trim(),
            Nip = czysteNip,
            Srodowisko = SrodowiskoKsef.Test
        };

        var czlonkostwo = new CzlonkostwoWFirmie
        {
            Firma = firma,
            Uzytkownik = NowyUzytkownik(adres, haslo!),
            Rola = RolaWFirmie.Wlasciciel
        };

        baza.Czlonkostwa.Add(czlonkostwo);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<CzlonkostwoWFirmie>(czlonkostwo, walidacja);
    }

    // ------------------------------------------------------------ zaproszenia

    /// <summary>Wystawia zaproszenie do bieżącej firmy.</summary>
    public async Task<WynikKonta<Zaproszenie>> ZaproscAsync(
        string email, RolaWFirmie rola, Guid zapraszajacyId,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();
        string adres = (email ?? string.Empty).Trim();

        SprawdzAdres(walidacja, adres);

        if (!walidacja.SaBledy && await JestJuzWFirmieAsync(adres, anulowanie))
        {
            walidacja.Blad("Email", "ta osoba już pracuje w tej firmie");
        }

        if (walidacja.SaBledy)
        {
            return new WynikKonta<Zaproszenie>(null, walidacja);
        }

        // Poprzednie niewykorzystane zaproszenie dla tego adresu przestaje
        // działać - inaczej po zmianie roli w obiegu byłyby dwa odnośniki
        // dające różne uprawnienia.
        foreach (Zaproszenie stare in await baza.Zaproszenia
                     .Where(z => z.PrzyjeteUtc == null && EF.Functions.ILike(z.Email, adres))
                     .ToListAsync(anulowanie))
        {
            baza.Zaproszenia.Remove(stare);
        }

        var zaproszenie = new Zaproszenie
        {
            Email = adres,
            Rola = rola,
            Kod = NowyKod(),
            WaznoscDoUtc = czas.GetUtcNow().AddDays(DniWaznosciZaproszenia),
            ZapraszajacyId = zapraszajacyId
        };

        baza.Zaproszenia.Add(zaproszenie);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<Zaproszenie>(zaproszenie, walidacja);
    }

    /// <summary>
    /// Odnajduje ważne zaproszenie po kodzie z odnośnika.
    /// </summary>
    /// <remarks>
    /// Czyta z pominięciem filtru firm - z założenia robi to ktoś, kto do tej
    /// firmy jeszcze nie należy. Kod jest tu jedynym uprawnieniem, dlatego
    /// nigdzie indziej nie wolno tego filtru pomijać.
    /// </remarks>
    public async Task<Zaproszenie?> ZnajdzZaproszenieAsync(
        string kod, CancellationToken anulowanie = default)
    {
        if (string.IsNullOrWhiteSpace(kod))
        {
            return null;
        }

        // Bez śledzenia zmian: zaproszenie „zużywa się" poleceniem
        // aktualizującym, które omija śledzenie. Wersja z pamięci pozostałaby
        // po nim nieaktualna i kolejne sprawdzenie w tym samym żądaniu
        // uznałoby wykorzystane zaproszenie za wciąż ważne.
        Zaproszenie? zaproszenie = await baza.Zaproszenia
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(z => z.Firma)
            .FirstOrDefaultAsync(z => z.Kod == kod, anulowanie);

        if (zaproszenie is null || zaproszenie.PrzyjeteUtc is not null)
        {
            return null;
        }

        return zaproszenie.WaznoscDoUtc < czas.GetUtcNow() ? null : zaproszenie;
    }

    /// <summary>
    /// Przyjmuje zaproszenie: zakłada konto albo dopisuje istniejące do firmy.
    /// </summary>
    /// <param name="haslo">
    /// Hasło do nowego konta albo hasło istniejącego konta - zaproszenie nie
    /// może stać się sposobem na przejęcie cudzego adresu bez znajomości hasła.
    /// </param>
    public async Task<WynikKonta<CzlonkostwoWFirmie>> PrzyjmijZaproszenieAsync(
        string kod, string? imieINazwisko, string haslo,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        Zaproszenie? zaproszenie = await ZnajdzZaproszenieAsync(kod, anulowanie);
        if (zaproszenie is null)
        {
            walidacja.Blad("Kod", "zaproszenie jest nieważne albo zostało już wykorzystane");
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        Uzytkownik? uzytkownik = await ZnajdzUzytkownikaAsync(zaproszenie.Email, anulowanie);

        if (uzytkownik is null)
        {
            SprawdzHaslo(walidacja, haslo, haslo);

            if (walidacja.SaBledy)
            {
                return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
            }

            uzytkownik = NowyUzytkownik(zaproszenie.Email, haslo);
            uzytkownik.ImieINazwisko = string.IsNullOrWhiteSpace(imieINazwisko)
                ? zaproszenie.Email
                : imieINazwisko.Trim();

            baza.Uzytkownicy.Add(uzytkownik);
        }
        else
        {
            PasswordVerificationResult sprawdzenie = haszowanie.VerifyHashedPassword(
                new object(), uzytkownik.HaszHasla, haslo ?? string.Empty);

            if (sprawdzenie == PasswordVerificationResult.Failed)
            {
                walidacja.Blad("Haslo",
                    "konto o tym adresie już istnieje - podaj jego hasło, żeby dołączyć do firmy");
                return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
            }
        }

        // Zaproszenie „zużywamy" jednym poleceniem z warunkiem, że nikt nie
        // zdążył go użyć wcześniej. Sprawdzenie i zapis w dwóch krokach
        // pozwoliłyby dwóm osobom kliknąć ten sam odnośnik jednocześnie
        // i wejść obie. Polecenie omija też śledzenie zmian, a to konieczne:
        // przyjmujący pracuje w kontekście innej firmy (albo żadnej), więc
        // strażnik izolacji słusznie nie przepuściłby zwykłego zapisu do
        // rekordu firmy zapraszającej.
        await using var przelacznik = await baza.Database.BeginTransactionAsync(anulowanie);

        int zuzyte = await baza.Zaproszenia
            .IgnoreQueryFilters()
            .Where(z => z.Id == zaproszenie.Id && z.PrzyjeteUtc == null)
            .ExecuteUpdateAsync(z => z.SetProperty(p => p.PrzyjeteUtc, czas.GetUtcNow()),
                                anulowanie);

        if (zuzyte != 1)
        {
            await przelacznik.RollbackAsync(anulowanie);
            walidacja.Blad("Kod", "zaproszenie zostało właśnie wykorzystane");
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        var czlonkostwo = new CzlonkostwoWFirmie
        {
            Uzytkownik = uzytkownik,
            FirmaId = zaproszenie.FirmaId,
            Rola = zaproszenie.Rola
        };

        baza.Czlonkostwa.Add(czlonkostwo);

        await baza.SaveChangesAsync(anulowanie);
        await przelacznik.CommitAsync(anulowanie);

        // Firma dociągnięta przy szukaniu zaproszenia - przyda się do
        // zbudowania ciasteczka logowania.
        czlonkostwo.Firma = zaproszenie.Firma;

        return new WynikKonta<CzlonkostwoWFirmie>(czlonkostwo, walidacja);
    }

    /// <summary>Czy istnieje już konto o takim adresie.</summary>
    public Task<bool> CzyKontoIstniejeAsync(string email, CancellationToken anulowanie = default) =>
        AdresZajetyAsync((email ?? string.Empty).Trim(), anulowanie);

    public async Task<IReadOnlyList<Zaproszenie>> ZaproszeniaAsync(
        CancellationToken anulowanie = default) =>
        await baza.Zaproszenia
            .Where(z => z.PrzyjeteUtc == null && z.WaznoscDoUtc > czas.GetUtcNow())
            .OrderBy(z => z.Email)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    public async Task<bool> OdwolajZaproszenieAsync(Guid id, CancellationToken anulowanie = default)
    {
        Zaproszenie? zaproszenie = await baza.Zaproszenia
            .FirstOrDefaultAsync(z => z.Id == id, anulowanie);

        if (zaproszenie is null)
        {
            return false;
        }

        baza.Zaproszenia.Remove(zaproszenie);
        await baza.SaveChangesAsync(anulowanie);

        return true;
    }

    // ------------------------------------------------------------- dostęp

    public async Task<IReadOnlyList<CzlonkostwoWFirmie>> CzlonkowieAsync(
        CancellationToken anulowanie = default) =>
        await baza.Czlonkostwa
            .Include(c => c.Uzytkownik)
            .Where(c => c.FirmaId == baza.AktualnaFirmaId)
            .OrderByDescending(c => c.Rola)
            .ThenBy(c => c.Uzytkownik!.Email)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    /// <summary>Firmy, w których pracuje dany użytkownik.</summary>
    public async Task<IReadOnlyList<CzlonkostwoWFirmie>> FirmyUzytkownikaAsync(
        Guid uzytkownikId, CancellationToken anulowanie = default) =>
        await baza.Czlonkostwa
            .Include(c => c.Firma)
            .Where(c => c.UzytkownikId == uzytkownikId)
            .OrderBy(c => c.Firma!.Nazwa)
            .AsNoTracking()
            .ToListAsync(anulowanie);

    public async Task<WynikKonta<CzlonkostwoWFirmie>> ZmienRoleAsync(
        Guid czlonkostwoId, RolaWFirmie rola, CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        CzlonkostwoWFirmie? czlonkostwo = await baza.Czlonkostwa
            .Include(c => c.Uzytkownik)
            .FirstOrDefaultAsync(c => c.Id == czlonkostwoId, anulowanie);

        if (czlonkostwo is null)
        {
            walidacja.Blad("Czlonkostwo", "nie znaleziono takiej osoby w tej firmie");
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        if (czlonkostwo.Rola == RolaWFirmie.Wlasciciel && rola != RolaWFirmie.Wlasciciel
            && await OstatniWlascicielAsync(czlonkostwo.Id, anulowanie))
        {
            walidacja.Blad("Rola",
                "to jedyny właściciel firmy - najpierw wskaż innego, potem zmień rolę");
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        czlonkostwo.Rola = rola;
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<CzlonkostwoWFirmie>(czlonkostwo, walidacja);
    }

    public async Task<WynikKonta<CzlonkostwoWFirmie>> OdbierzDostepAsync(
        Guid czlonkostwoId, CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        CzlonkostwoWFirmie? czlonkostwo = await baza.Czlonkostwa
            .Include(c => c.Uzytkownik)
            .FirstOrDefaultAsync(c => c.Id == czlonkostwoId, anulowanie);

        if (czlonkostwo is null)
        {
            walidacja.Blad("Czlonkostwo", "nie znaleziono takiej osoby w tej firmie");
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        // Firma bez właściciela byłaby firmą, do której nikt nie ma pełnego
        // dostępu - i nikt nie mógłby tego już naprawić od środka.
        if (czlonkostwo.Rola == RolaWFirmie.Wlasciciel
            && await OstatniWlascicielAsync(czlonkostwo.Id, anulowanie))
        {
            walidacja.Blad("Czlonkostwo",
                "to jedyny właściciel firmy - nie można odebrać mu dostępu");
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        baza.Czlonkostwa.Remove(czlonkostwo);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<CzlonkostwoWFirmie>(czlonkostwo, walidacja);
    }

    // ------------------------------------------------------------ własne konto

    /// <summary>Zmienia hasło po sprawdzeniu obecnego.</summary>
    public async Task<WynikKonta<Uzytkownik>> ZmienHasloAsync(
        Guid uzytkownikId, string obecne, string nowe, string powtorzenie,
        CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        Uzytkownik? uzytkownik = await baza.Uzytkownicy
            .FirstOrDefaultAsync(u => u.Id == uzytkownikId, anulowanie);

        if (uzytkownik is null)
        {
            walidacja.Blad("Konto", "nie znaleziono konta");
            return new WynikKonta<Uzytkownik>(null, walidacja);
        }

        // Znajomość obecnego hasła jest tu jedynym dowodem, że przy klawiaturze
        // siedzi właściciel konta, a nie ktoś, kto zastał otwartą przeglądarkę.
        if (haszowanie.VerifyHashedPassword(new object(), uzytkownik.HaszHasla, obecne ?? string.Empty)
            == PasswordVerificationResult.Failed)
        {
            walidacja.Blad("ObecneHaslo", "obecne hasło jest nieprawidłowe");
            return new WynikKonta<Uzytkownik>(null, walidacja);
        }

        SprawdzHaslo(walidacja, nowe, powtorzenie);

        if (walidacja.SaBledy)
        {
            return new WynikKonta<Uzytkownik>(null, walidacja);
        }

        UstawHaslo(uzytkownik, nowe);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<Uzytkownik>(uzytkownik, walidacja);
    }

    /// <summary>Zmienia imię i nazwisko pokazywane w programie.</summary>
    public async Task<Uzytkownik?> ZmienDaneAsync(
        Guid uzytkownikId, string? imieINazwisko, CancellationToken anulowanie = default)
    {
        Uzytkownik? uzytkownik = await baza.Uzytkownicy
            .FirstOrDefaultAsync(u => u.Id == uzytkownikId, anulowanie);

        if (uzytkownik is null)
        {
            return null;
        }

        uzytkownik.ImieINazwisko = string.IsNullOrWhiteSpace(imieINazwisko)
            ? uzytkownik.Email
            : imieINazwisko.Trim();

        await baza.SaveChangesAsync(anulowanie);

        return uzytkownik;
    }

    /// <summary>Wypisuje użytkownika z bieżącej firmy.</summary>
    public async Task<WynikKonta<CzlonkostwoWFirmie>> OpuscFirmeAsync(
        Guid uzytkownikId, CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        CzlonkostwoWFirmie? czlonkostwo = await baza.Czlonkostwa
            .FirstOrDefaultAsync(
                c => c.UzytkownikId == uzytkownikId && c.FirmaId == baza.AktualnaFirmaId,
                anulowanie);

        if (czlonkostwo is null)
        {
            walidacja.Blad("Firma", "nie pracujesz w tej firmie");
            return new WynikKonta<CzlonkostwoWFirmie>(null, walidacja);
        }

        return await OdbierzDostepAsync(czlonkostwo.Id, anulowanie);
    }

    // ------------------------------------------------------------ reset hasła

    /// <summary>Ile godzin żyje odnośnik do ustawienia nowego hasła.</summary>
    public const int GodzinWaznosciResetu = 2;

    public Task<Uzytkownik?> ZnajdzPoAdresieAsync(string email,
                                                  CancellationToken anulowanie = default) =>
        ZnajdzUzytkownikaAsync((email ?? string.Empty).Trim(), anulowanie);

    /// <summary>
    /// Wystawia jednorazowy odnośnik do ustawienia nowego hasła.
    /// </summary>
    /// <remarks>
    /// Wcześniejsze niewykorzystane odnośniki przestają działać - w obiegu ma
    /// być najwyżej jeden, żeby stary wykradziony odnośnik nie czekał na
    /// swoją okazję.
    /// </remarks>
    public async Task<ResetHasla> WystawResetAsync(Guid uzytkownikId,
                                                   CancellationToken anulowanie = default)
    {
        foreach (ResetHasla stary in await baza.ResetyHasla
                     .Where(r => r.UzytkownikId == uzytkownikId && r.WykorzystanoUtc == null)
                     .ToListAsync(anulowanie))
        {
            baza.ResetyHasla.Remove(stary);
        }

        var reset = new ResetHasla
        {
            UzytkownikId = uzytkownikId,
            Kod = NowyKod(),
            WaznoscDoUtc = czas.GetUtcNow().AddHours(GodzinWaznosciResetu)
        };

        baza.ResetyHasla.Add(reset);
        await baza.SaveChangesAsync(anulowanie);

        return reset;
    }

    public async Task<ResetHasla?> ZnajdzResetAsync(string kod,
                                                    CancellationToken anulowanie = default)
    {
        if (string.IsNullOrWhiteSpace(kod))
        {
            return null;
        }

        ResetHasla? reset = await baza.ResetyHasla
            .AsNoTracking()
            .Include(r => r.Uzytkownik)
            .FirstOrDefaultAsync(r => r.Kod == kod, anulowanie);

        if (reset is null || reset.WykorzystanoUtc is not null)
        {
            return null;
        }

        return reset.WaznoscDoUtc < czas.GetUtcNow() ? null : reset;
    }

    /// <summary>Ustawia nowe hasło na podstawie odnośnika.</summary>
    public async Task<WynikKonta<Uzytkownik>> UstawNoweHasloAsync(
        string kod, string haslo, string powtorzenie, CancellationToken anulowanie = default)
    {
        var walidacja = new WynikWalidacji();

        ResetHasla? reset = await ZnajdzResetAsync(kod, anulowanie);
        if (reset is null)
        {
            walidacja.Blad("Kod", "odnośnik jest nieważny albo został już wykorzystany");
            return new WynikKonta<Uzytkownik>(null, walidacja);
        }

        SprawdzHaslo(walidacja, haslo, powtorzenie);
        if (walidacja.SaBledy)
        {
            return new WynikKonta<Uzytkownik>(null, walidacja);
        }

        Uzytkownik? uzytkownik = await baza.Uzytkownicy
            .FirstOrDefaultAsync(u => u.Id == reset.UzytkownikId, anulowanie);

        if (uzytkownik is null)
        {
            walidacja.Blad("Kod", "konto powiązane z odnośnikiem już nie istnieje");
            return new WynikKonta<Uzytkownik>(null, walidacja);
        }

        // Odnośnik zużywamy warunkowo, tak samo jak zaproszenie: sprawdzenie
        // i zapis w dwóch krokach pozwoliłyby użyć go dwa razy naraz.
        int zuzyte = await baza.ResetyHasla
            .Where(r => r.Id == reset.Id && r.WykorzystanoUtc == null)
            .ExecuteUpdateAsync(r => r.SetProperty(p => p.WykorzystanoUtc, czas.GetUtcNow()),
                                anulowanie);

        if (zuzyte != 1)
        {
            walidacja.Blad("Kod", "odnośnik został właśnie wykorzystany");
            return new WynikKonta<Uzytkownik>(null, walidacja);
        }

        UstawHaslo(uzytkownik, haslo);
        await baza.SaveChangesAsync(anulowanie);

        return new WynikKonta<Uzytkownik>(uzytkownik, walidacja);
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>
    /// Zapisuje nowe hasło i unieważnia wcześniejsze sesje.
    /// </summary>
    /// <remarks>
    /// Zmiana hasła bez zmiany stempla zostawiałaby otwarte drzwi: ciasteczko
    /// wykradzione wcześniej działałoby dalej, mimo że hasło już nie.
    /// </remarks>
    private void UstawHaslo(Uzytkownik uzytkownik, string haslo)
    {
        uzytkownik.HaszHasla = haszowanie.HashPassword(new object(), haslo);
        uzytkownik.StempelBezpieczenstwa = Guid.NewGuid();
    }

    private Uzytkownik NowyUzytkownik(string email, string haslo) => new()
    {
        Email = email,
        ImieINazwisko = email,
        HaszHasla = haszowanie.HashPassword(new object(), haslo)
    };

    private static string NowyKod()
    {
        // 32 bajty losowości - odnośnik do zaproszenia jest wart tyle,
        // co hasło, więc nie może dać się zgadnąć.
        byte[] losowe = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(losowe);
    }

    private static void SprawdzAdres(WynikWalidacji walidacja, string adres)
    {
        if (string.IsNullOrWhiteSpace(adres) || !adres.Contains('@', StringComparison.Ordinal))
        {
            walidacja.Blad("Email", "podaj poprawny adres e-mail");
        }
    }

    private static void SprawdzHaslo(WynikWalidacji walidacja, string? haslo, string? powtorzenie)
    {
        if ((haslo ?? string.Empty).Length < UstawieniaStartu.MinimalnaDlugoscHasla)
        {
            walidacja.Blad("Haslo",
                $"hasło musi mieć co najmniej {UstawieniaStartu.MinimalnaDlugoscHasla} znaków");
        }
        else if (haslo != powtorzenie)
        {
            walidacja.Blad("PowtorzHaslo", "hasła nie są takie same");
        }
    }

    private Task<bool> AdresZajetyAsync(string adres, CancellationToken anulowanie) =>
        baza.Uzytkownicy.AnyAsync(u => EF.Functions.ILike(u.Email, adres), anulowanie);

    private Task<Uzytkownik?> ZnajdzUzytkownikaAsync(string adres, CancellationToken anulowanie) =>
        baza.Uzytkownicy.FirstOrDefaultAsync(u => EF.Functions.ILike(u.Email, adres), anulowanie);

    private Task<bool> JestJuzWFirmieAsync(string adres, CancellationToken anulowanie) =>
        baza.Czlonkostwa.AnyAsync(
            c => c.FirmaId == baza.AktualnaFirmaId
                 && EF.Functions.ILike(c.Uzytkownik!.Email, adres),
            anulowanie);

    /// <summary>Czy poza wskazanym członkostwem firma nie ma już właściciela.</summary>
    private async Task<bool> OstatniWlascicielAsync(Guid czlonkostwoId,
                                                    CancellationToken anulowanie) =>
        !await baza.Czlonkostwa
            .AnyAsync(c => c.FirmaId == baza.AktualnaFirmaId
                           && c.Rola == RolaWFirmie.Wlasciciel
                           && c.Id != czlonkostwoId,
                      anulowanie);
}
