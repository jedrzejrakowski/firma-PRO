using System.Globalization;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Wynik pojedynczego sprawdzenia.</summary>
public enum StanKroku
{
    /// <summary>Sprawdzenie wypadło pomyślnie.</summary>
    Ok,

    /// <summary>Działa, ale coś wymaga uwagi.</summary>
    Ostrzezenie,

    /// <summary>Sprawdzenie nie przeszło - dalej się nie da.</summary>
    Blad,

    /// <summary>Nie sprawdzano, bo wcześniejszy krok nie przeszedł.</summary>
    Pominiety
}

/// <summary>Jeden krok sprawdzenia połączenia z KSeF.</summary>
/// <param name="Wskazowka">Co zrobić, gdy krok nie przeszedł.</param>
public sealed record KrokDiagnostyki(
    string Nazwa,
    StanKroku Stan,
    string Komunikat,
    string? Wskazowka = null);

/// <summary>Komplet sprawdzeń wykonanych za jednym razem.</summary>
public sealed record WynikDiagnostyki(
    IReadOnlyList<KrokDiagnostyki> Kroki,
    SrodowiskoKsef Srodowisko,
    DateTimeOffset Kiedy)
{
    /// <summary>Czy da się wystawiać faktury do KSeF.</summary>
    public bool Udalo => Kroki.All(k => k.Stan is not (StanKroku.Blad or StanKroku.Pominiety));

    public int IleBledow => Kroki.Count(k => k.Stan == StanKroku.Blad);
}

/// <summary>
/// Sprawdza po kolei wszystko, co musi zadziałać, żeby wysłać fakturę do KSeF.
/// </summary>
/// <remarks>
/// <para>
/// Pierwsze uruchomienie u klienta zwykle nie udaje się za pierwszym razem,
/// a pojedynczy komunikat „nie udało się wysłać faktury" nie mówi, czy winna
/// jest sieć, token, NIP, czy uprawnienia. Ta usługa rozbija drogę na kroki
/// i przy każdym mówi wprost, co zrobić dalej.
/// </para>
/// <para>
/// Sprawdzenie niczego nie wystawia i nie zostawia otwartej sesji: otwiera ją
/// tylko po to, żeby udowodnić, że się da, i natychmiast zamyka. Token nigdy
/// nie trafia do wyniku ani do dziennika.
/// </para>
/// </remarks>
public sealed class UslugaDiagnostykiKsef(
    FirmaProDbContext baza,
    IFabrykaKlientowKsef fabrykaKlientow,
    IOchronaTokena ochronaTokena,
    TimeProvider czas,
    ILogger<UslugaDiagnostykiKsef> dziennik)
{
    public async Task<WynikDiagnostyki> SprawdzAsync(CancellationToken anulowanie = default)
    {
        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        List<KrokDiagnostyki> kroki = [];

        kroki.Add(SprawdzDaneFirmy(firma));
        kroki.Add(OpiszSrodowisko(firma));

        (KrokDiagnostyki krokTokena, string? token) = SprawdzToken(firma);
        kroki.Add(krokTokena);

        if (kroki.Any(k => k.Stan == StanKroku.Blad))
        {
            // Bez poprawnych danych firmy albo tokena reszta i tak nie ma szans,
            // a nieudane wywołania tylko zaciemniłyby obraz.
            kroki.AddRange(Pominiete(
                "Połączenie z serwerem", "Klucze szyfrowania",
                "Uwierzytelnienie tokenem", "Otwarcie sesji wysyłkowej",
                "Dostęp do faktur zakupu"));

            return Zakoncz(kroki, firma);
        }

        IKlientKsef klient = fabrykaKlientow.Utworz(firma.Srodowisko);

        (KrokDiagnostyki polaczenie, StanSrodowiska? stan) =
            await SprawdzPolaczenieAsync(klient, anulowanie);

        kroki.Add(polaczenie);

        if (stan is null)
        {
            kroki.AddRange(Pominiete(
                "Klucze szyfrowania", "Uwierzytelnienie tokenem",
                "Otwarcie sesji wysyłkowej", "Dostęp do faktur zakupu"));

            return Zakoncz(kroki, firma);
        }

        kroki.Add(SprawdzKlucze(stan));

        KrokDiagnostyki uwierzytelnienie =
            await SprawdzUwierzytelnienieAsync(klient, firma, token!, anulowanie);

        kroki.Add(uwierzytelnienie);

        if (uwierzytelnienie.Stan == StanKroku.Blad)
        {
            kroki.AddRange(Pominiete("Otwarcie sesji wysyłkowej", "Dostęp do faktur zakupu"));
            return Zakoncz(kroki, firma);
        }

        kroki.Add(await SprawdzSesjeAsync(klient, anulowanie));
        kroki.Add(await SprawdzZakupyAsync(klient, anulowanie));

        return Zakoncz(kroki, firma);
    }

    // ------------------------------------------------------------ kroki

    private static KrokDiagnostyki SprawdzDaneFirmy(Firma firma)
    {
        List<string> braki = [];

        if (!Walidator.NipPoprawny(firma.Nip))
        {
            braki.Add("NIP jest nieprawidłowy");
        }

        if (string.IsNullOrWhiteSpace(firma.Nazwa))
        {
            braki.Add("brakuje nazwy firmy");
        }

        if (string.IsNullOrWhiteSpace(firma.AdresLinia1))
        {
            braki.Add("brakuje adresu");
        }

        return braki.Count == 0
            ? new KrokDiagnostyki("Dane firmy", StanKroku.Ok,
                $"{firma.Nazwa} — NIP {firma.Nip}")
            : new KrokDiagnostyki("Dane firmy", StanKroku.Blad,
                "Dane wystawcy są niekompletne: " + string.Join(", ", braki) + ".",
                "Uzupełnij je w Ustawieniach firmy. KSeF rozpoznaje wystawcę po " +
                "numerze NIP - musi być ten sam, na który wygenerowano token.");
    }

    private static KrokDiagnostyki OpiszSrodowisko(Firma firma)
    {
        string adres = AdresyKsef.Api(firma.Srodowisko);

        return firma.Srodowisko == SrodowiskoKsef.Produkcja
            ? new KrokDiagnostyki("Środowisko", StanKroku.Ostrzezenie,
                $"Produkcyjne ({adres}).",
                "Faktury wysłane na produkcję są dokumentami w obrocie prawnym " +
                "i nie da się ich usunąć. Do prób służy środowisko testowe.")
            : new KrokDiagnostyki("Środowisko", StanKroku.Ok,
                $"{OpisSrodowiska(firma.Srodowisko)} ({adres}).");
    }

    /// <summary>
    /// Sprawdza, czy token w ogóle jest - i skąd pochodzi.
    /// </summary>
    /// <remarks>
    /// Sam token nigdy nie trafia do wyniku. Pokazujemy wyłącznie jego
    /// źródło, bo to ono najczęściej bywa niespodzianką: zmienna środowiskowa
    /// ma pierwszeństwo i potrafi przesłonić token wpisany w Ustawieniach.
    /// </remarks>
    private (KrokDiagnostyki Krok, string? Token) SprawdzToken(Firma firma)
    {
        string? zeSrodowiska = Environment.GetEnvironmentVariable("KSEF_TOKEN");

        if (!string.IsNullOrWhiteSpace(zeSrodowiska))
        {
            bool takzeWBazie = firma.TokenKsefZaszyfrowany is { Length: > 0 };

            return (new KrokDiagnostyki("Token KSeF",
                takzeWBazie ? StanKroku.Ostrzezenie : StanKroku.Ok,
                takzeWBazie
                    ? "Używany jest token ze zmiennej KSEF_TOKEN - przesłania ten " +
                      "zapisany w Ustawieniach."
                    : "Token pobrany ze zmiennej środowiskowej KSEF_TOKEN.",
                takzeWBazie
                    ? "Jeśli chcesz używać tokena z Ustawień, usuń zmienną " +
                      "KSEF_TOKEN ze środowiska programu."
                    : null),
                zeSrodowiska);
        }

        if (firma.TokenKsefZaszyfrowany is not { Length: > 0 } zaszyfrowany)
        {
            return (new KrokDiagnostyki("Token KSeF", StanKroku.Blad,
                "Nie zapisano tokena KSeF.",
                "Wygeneruj token w aplikacji webowej KSeF (dla tego samego " +
                "środowiska co wybrane wyżej) i wklej go w Ustawieniach firmy."),
                null);
        }

        // Token jest w bazie, ale nie daje się odczytać - to zupełnie inna
        // sytuacja niż jego brak i wymaga innej reakcji, więc mówimy o niej
        // wprost, zamiast udawać, że tokena nie ma.
        if (ochronaTokena.Odszyfruj(zaszyfrowany) is not { Length: > 0 } token)
        {
            return (new KrokDiagnostyki("Token KSeF", StanKroku.Blad,
                "Token jest zapisany, ale nie daje się odszyfrować.",
                "Tak dzieje się po utracie kluczy ochrony danych - najczęściej " +
                "gdy program wystartował bez trwałego katalogu kluczy " +
                "(zmienna KATALOG_KLUCZY, wolumin „klucze\" we wdrożeniu). " +
                "Przywróć katalog kluczy albo wpisz token ponownie w Ustawieniach."),
                null);
        }

        return (new KrokDiagnostyki("Token KSeF", StanKroku.Ok,
            "Token zapisany w Ustawieniach firmy."), token);
    }

    private static async Task<(KrokDiagnostyki Krok, StanSrodowiska? Stan)>
        SprawdzPolaczenieAsync(IKlientKsef klient, CancellationToken anulowanie)
    {
        try
        {
            StanSrodowiska stan = await klient.SprawdzSrodowiskoAsync(anulowanie);

            return (new KrokDiagnostyki("Połączenie z serwerem", StanKroku.Ok,
                $"Serwer odpowiedział pod adresem {stan.Adres}."), stan);
        }
        catch (BladKsefException blad)
        {
            (string komunikat, string wskazowka) = Wytlumacz(blad);

            return (new KrokDiagnostyki("Połączenie z serwerem", StanKroku.Blad,
                komunikat, wskazowka), null);
        }
    }

    private static KrokDiagnostyki SprawdzKlucze(StanSrodowiska stan)
    {
        if (!stan.MaKluczDoTokena || !stan.MaKluczDoSesji)
        {
            return new KrokDiagnostyki("Klucze szyfrowania", StanKroku.Blad,
                "KSeF nie udostępnia kompletu ważnych certyfikatów " +
                $"(znaleziono {stan.IleCertyfikatow}).",
                "To problem po stronie systemu albo objaw rozmowy z niewłaściwym " +
                "adresem. Spróbuj ponownie za jakiś czas.");
        }

        string opis = $"Dostępne {stan.IleCertyfikatow} ważnych certyfikatów.";

        if (stan.NajblizszeWygasniecie is DateTimeOffset koniec)
        {
            opis += " Najbliższy traci ważność " +
                    koniec.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".";
        }

        return new KrokDiagnostyki("Klucze szyfrowania", StanKroku.Ok, opis);
    }

    private async Task<KrokDiagnostyki> SprawdzUwierzytelnienieAsync(
        IKlientKsef klient, Firma firma, string token, CancellationToken anulowanie)
    {
        try
        {
            await klient.UwierzytelnijAsync(firma.Nip, token, anulowanie);

            return new KrokDiagnostyki("Uwierzytelnienie tokenem", StanKroku.Ok,
                $"KSeF uznał token dla numeru NIP {firma.Nip}.");
        }
        catch (BladKsefException blad)
        {
            Dziennik.NieudanaDiagnostyka(dziennik, blad, "uwierzytelnienie");

            (string komunikat, string wskazowka) = Wytlumacz(blad);

            return new KrokDiagnostyki("Uwierzytelnienie tokenem", StanKroku.Blad,
                komunikat, wskazowka);
        }
    }

    private async Task<KrokDiagnostyki> SprawdzSesjeAsync(
        IKlientKsef klient, CancellationToken anulowanie)
    {
        try
        {
            string numer = await klient.OtworzSesjeAsync(anulowanie);

            // Sesja zostaje zamknięta od razu - sprawdzenie ma niczego po sobie
            // nie zostawiać, a otwarta sesja czeka na faktury i generuje UPO.
            await klient.ZamknijSesjeAsync(anulowanie);

            return new KrokDiagnostyki("Otwarcie sesji wysyłkowej", StanKroku.Ok,
                $"Sesja {numer} otwarta i zamknięta. Wysyłanie faktur zadziała.");
        }
        catch (BladKsefException blad)
        {
            Dziennik.NieudanaDiagnostyka(dziennik, blad, "otwarcie sesji");

            (string komunikat, string wskazowka) = Wytlumacz(blad);

            return new KrokDiagnostyki("Otwarcie sesji wysyłkowej", StanKroku.Blad,
                komunikat, wskazowka);
        }
    }

    private async Task<KrokDiagnostyki> SprawdzZakupyAsync(
        IKlientKsef klient, CancellationToken anulowanie)
    {
        DateOnly dzisiaj = DateOnly.FromDateTime(czas.GetUtcNow().UtcDateTime);

        try
        {
            IReadOnlyList<FakturaZakupowa> faktury =
                await klient.PobierzFakturyZakupoweAsync(
                    dzisiaj.AddDays(-7), dzisiaj, anulowanie);

            return new KrokDiagnostyki("Dostęp do faktur zakupu", StanKroku.Ok,
                $"Zapytanie o ostatnie 7 dni zwróciło {faktury.Count} faktur.");
        }
        catch (BladKsefException blad)
        {
            Dziennik.NieudanaDiagnostyka(dziennik, blad, "pobranie zakupów");

            (string komunikat, _) = Wytlumacz(blad);

            // Wystawianie faktur jest ważniejsze i działa niezależnie, więc
            // brak dostępu do zakupów nie przekreśla całego sprawdzenia.
            return new KrokDiagnostyki("Dostęp do faktur zakupu", StanKroku.Ostrzezenie,
                komunikat,
                "Wystawianie faktur zadziała mimo to. Pobieranie zakupów wymaga " +
                "osobnego uprawnienia tokena - sprawdź je w aplikacji webowej KSeF.");
        }
    }

    // ------------------------------------------------------------ pomocnicze

    /// <summary>
    /// Zamienia błąd KSeF na zdanie po polsku i podpowiedź, co z nim zrobić.
    /// </summary>
    /// <remarks>
    /// Surowy komunikat („HTTP 401") nie mówi osobie wystawiającej faktury
    /// niczego. Najczęstsze przyczyny są zawsze te same, więc warto je nazwać
    /// wprost - to one decydują, czy pierwsze uruchomienie zajmie kwadrans,
    /// czy cały dzień.
    /// </remarks>
    internal static (string Komunikat, string Wskazowka) Wytlumacz(BladKsefException blad)
    {
        ArgumentNullException.ThrowIfNull(blad);

        string szczegoly = blad.Szczegoly.Count == 0
            ? string.Empty
            : " Szczegóły: " + string.Join("; ", blad.Szczegoly);

        return blad.KodHttp switch
        {
            null => (
                "Program nie dotarł do serwera KSeF. Szczegóły techniczne: " +
                (blad.InnerException?.Message ?? blad.Message),
                "Sprawdź połączenie z internetem, a na serwerze także zaporę " +
                "i ewentualny serwer pośredniczący. Program łączy się wychodząco " +
                "po porcie 443."),

            400 => (
                "KSeF uznał żądanie za nieprawidłowe." + szczegoly,
                "Zwykle oznacza to niezgodność wersji API albo błąd w danych " +
                "firmy. Sprawdź NIP w Ustawieniach."),

            401 => (
                "KSeF nie uznał tokena." + szczegoly,
                "Trzy najczęstsze przyczyny: token wygasł albo został unieważniony, " +
                "token wygenerowano dla innego numeru NIP, albo token pochodzi " +
                "z innego środowiska niż wybrane (testowe kontra produkcyjne). " +
                "Wygeneruj nowy token i wklej go w Ustawieniach."),

            403 => (
                "Token nie ma uprawnień do tej operacji." + szczegoly,
                "W aplikacji webowej KSeF sprawdź, jakie uprawnienia nadano " +
                "temu tokenowi - do wystawiania faktur potrzebne jest " +
                "uprawnienie do wysyłki."),

            404 => (
                "KSeF nie zna wywołanego adresu." + szczegoly,
                "Zwykle znaczy to, że zmieniła się wersja API. Odnotuj to " +
                "zgłoszenie - wymaga poprawki w programie."),

            429 => (
                "KSeF odrzucił żądanie z powodu zbyt wielu prób." + szczegoly,
                "Odczekaj kilka minut i spróbuj ponownie."),

            >= 500 => (
                $"Awaria po stronie systemu KSeF (HTTP {blad.KodHttp})." + szczegoly,
                "Po stronie programu nie ma tu nic do poprawienia. Spróbuj " +
                "ponownie później."),

            _ => (blad.PelnyOpis(), "Zapisz treść komunikatu - wskazuje przyczynę.")
        };
    }

    private static IEnumerable<KrokDiagnostyki> Pominiete(params string[] nazwy) =>
        nazwy.Select(n => new KrokDiagnostyki(n, StanKroku.Pominiety,
            "Nie sprawdzano - wcześniejszy krok nie przeszedł."));

    private WynikDiagnostyki Zakoncz(List<KrokDiagnostyki> kroki, Firma firma)
    {
        var wynik = new WynikDiagnostyki(kroki, firma.Srodowisko, czas.GetUtcNow());
        Dziennik.ZakonczonaDiagnostyka(dziennik, firma.Srodowisko,
            wynik.Udalo, wynik.IleBledow);

        return wynik;
    }

    private static string OpisSrodowiska(SrodowiskoKsef srodowisko) => srodowisko switch
    {
        SrodowiskoKsef.Test => "Testowe",
        SrodowiskoKsef.Demo => "Demonstracyjne",
        SrodowiskoKsef.Produkcja => "Produkcyjne",
        _ => srodowisko.ToString()
    };
}
