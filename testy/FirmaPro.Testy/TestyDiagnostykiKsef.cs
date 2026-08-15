using System.Net;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Sprawdzanie połączenia z KSeF krok po kroku.
/// </summary>
/// <remarks>
/// Diagnostyka ma sens tylko wtedy, gdy poprawnie nazywa przyczynę. Testy
/// wywołują więc kolejne rodzaje niepowodzeń - brak tokena, zerwaną łączność,
/// odrzucony token, brak uprawnienia do zakupów - i sprawdzają, czy program
/// wskazuje właściwy krok i nie zgaduje.
/// </remarks>
[Collection(KolekcjaBazy.Nazwa)]
public sealed class TestyDiagnostykiKsef(BazaTestowa baza)
{
    private const string Token = "TOKEN-KSEF-123";

    private static readonly OchronaTokena Ochrona =
        new(DataProtectionProvider.Create("FirmaPro.Testy"));

    private async Task<Guid> ZalozFirmeAsync(bool zTokenem = true,
                                             string nip = "5252248481",
                                             SrodowiskoKsef srodowisko = SrodowiskoKsef.Test)
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        var firma = new Firma
        {
            Nazwa = "Moja Firma sp. z o.o.",
            Nip = nip,
            AdresLinia1 = "ul. Prosta 51",
            Srodowisko = srodowisko,
            TokenKsefZaszyfrowany = zTokenem ? Ochrona.Zaszyfruj(Token) : null
        };

        kontekst.Firmy.Add(firma);
        await kontekst.SaveChangesAsync();

        return firma.Id;
    }

    private static UslugaDiagnostykiKsef Usluga(FirmaProDbContext kontekst, AtrapaKsef atrapa) =>
        new(kontekst, new FabrykaZAtrapy(atrapa), Ochrona, TimeProvider.System,
            NullLogger<UslugaDiagnostykiKsef>.Instance);

    private static KrokDiagnostyki Krok(WynikDiagnostyki wynik, string nazwa) =>
        Assert.Single(wynik.Kroki, k => k.Nazwa == nazwa);

    [Fact]
    public async Task PoprawneUstawieniaPrzechodzaWszystkieKroki()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

        Assert.True(wynik.Udalo, string.Join(" | ",
            wynik.Kroki.Select(k => $"{k.Nazwa}: {k.Stan} - {k.Komunikat}")));

        Assert.Equal(StanKroku.Ok, Krok(wynik, "Uwierzytelnienie").Stan);
        Assert.Equal(StanKroku.Ok, Krok(wynik, "Otwarcie sesji wysyłkowej").Stan);
    }

    /// <summary>
    /// Sprawdzenie niczego nie wystawia i nie zostawia otwartej sesji.
    /// </summary>
    /// <remarks>
    /// Gdyby zostawiało, pierwsza próba na produkcji kończyłaby się fakturą
    /// w obrocie prawnym albo wiszącą sesją generującą UPO.
    /// </remarks>
    [Fact]
    public async Task SprawdzenieNiczegoNieWystawia()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        await Usluga(kontekst, atrapa).SprawdzAsync();

        Assert.Empty(atrapa.OdebraneFaktury);
        Assert.True(atrapa.SesjaZamknieta, "Sesja została otwarta i nie zamknięta.");
    }

    [Fact]
    public async Task BezTokenaProgramNieProbujeSieLaczyc()
    {
        Guid firmaId = await ZalozFirmeAsync(zTokenem: false);
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

        Assert.False(wynik.Udalo);
        Assert.Equal(StanKroku.Blad, Krok(wynik, "Token KSeF").Stan);
        Assert.Equal(StanKroku.Pominiety, Krok(wynik, "Połączenie z serwerem").Stan);

        // Bez tokena nie ma po co ruszać sieci - i widać to po pustej liście.
        Assert.Empty(atrapa.Wywolania);
    }

    [Fact]
    public async Task BlednyNipZatrzymujeSprawdzenieNaDanychFirmy()
    {
        Guid firmaId = await ZalozFirmeAsync(nip: "1234567890");
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

        KrokDiagnostyki krok = Krok(wynik, "Dane firmy");

        Assert.Equal(StanKroku.Blad, krok.Stan);
        Assert.Contains("NIP", krok.Komunikat, StringComparison.Ordinal);
        Assert.Empty(atrapa.Wywolania);
    }

    /// <summary>
    /// Brak łączności wskazuje na sieć, a nie na token.
    /// </summary>
    /// <remarks>
    /// To rozróżnienie jest sednem tego ekranu: pobranie certyfikatów nie
    /// wymaga tokena, więc jego niepowodzenie na pewno nie jest winą tokena.
    /// </remarks>
    [Fact]
    public async Task ZerwanaLacznoscWskazujeNaSiecANieNaToken()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token) { ZrywajPolaczenie = true };

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

        KrokDiagnostyki krok = Krok(wynik, "Połączenie z serwerem");

        Assert.Equal(StanKroku.Blad, krok.Stan);
        Assert.Contains("nie dotarł do serwera", krok.Komunikat, StringComparison.Ordinal);
        Assert.Contains("zaporę", krok.Wskazowka!, StringComparison.Ordinal);

        Assert.Equal(StanKroku.Ok, Krok(wynik, "Token KSeF").Stan);
        Assert.Equal(StanKroku.Pominiety, Krok(wynik, "Uwierzytelnienie").Stan);
    }

    [Fact]
    public async Task OdrzuconyTokenTlumaczyTrzyNajczestszePrzyczyny()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token)
        {
            Awaria = ("auth/ksef-token", HttpStatusCode.Unauthorized)
        };

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

        KrokDiagnostyki krok = Krok(wynik, "Uwierzytelnienie");

        Assert.Equal(StanKroku.Blad, krok.Stan);
        Assert.Contains("nie uznał tokena", krok.Komunikat, StringComparison.Ordinal);

        // Podpowiedź musi wymieniać wszystkie trzy typowe przyczyny, bo bez
        // nich użytkownik zostaje z samym "401".
        Assert.Contains("wygasł", krok.Wskazowka!, StringComparison.Ordinal);
        Assert.Contains("NIP", krok.Wskazowka!, StringComparison.Ordinal);
        Assert.Contains("środowiska", krok.Wskazowka!, StringComparison.Ordinal);

        Assert.Equal(StanKroku.Pominiety, Krok(wynik, "Otwarcie sesji wysyłkowej").Stan);
    }

    /// <summary>
    /// Brak uprawnienia do zakupów nie przekreśla wystawiania faktur.
    /// </summary>
    [Fact]
    public async Task BrakDostepuDoZakupowToTylkoOstrzezenie()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token)
        {
            Awaria = ("invoices/query", HttpStatusCode.Forbidden)
        };

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

        KrokDiagnostyki krok = Krok(wynik, "Dostęp do faktur zakupu");

        Assert.Equal(StanKroku.Ostrzezenie, krok.Stan);
        Assert.Contains("uprawnień", krok.Komunikat, StringComparison.Ordinal);

        // Ostrzeżenie nie jest błędem - wysyłka faktur nadal działa.
        Assert.True(wynik.Udalo);
        Assert.Equal(StanKroku.Ok, Krok(wynik, "Otwarcie sesji wysyłkowej").Stan);
    }

    [Fact]
    public async Task ProdukcjaJestOznaczonaOstrzezeniem()
    {
        Guid firmaId = await ZalozFirmeAsync(srodowisko: SrodowiskoKsef.Produkcja);
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

        KrokDiagnostyki krok = Krok(wynik, "Środowisko");

        Assert.Equal(StanKroku.Ostrzezenie, krok.Stan);
        Assert.Contains("obrocie prawnym", krok.Wskazowka!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Token zapisany, ale nieodszyfrowywalny to inny problem niż jego brak.
    /// </summary>
    /// <remarks>
    /// Zdarza się po utracie kluczy ochrony danych - na przykład gdy kontener
    /// wystartował bez trwałego katalogu kluczy. Komunikat „brak tokena"
    /// kazałby wtedy szukać nie tam, gdzie trzeba.
    /// </remarks>
    [Fact]
    public async Task NieodszyfrowywalnyTokenWskazujeNaUtraceKluczy()
    {
        await using (FirmaProDbContext zakladanie = baza.UtworzKontekst(null))
        {
            var firma = new Firma
            {
                Nazwa = "Moja Firma sp. z o.o.",
                Nip = "5252248481",
                AdresLinia1 = "ul. Prosta 51",
                Srodowisko = SrodowiskoKsef.Test,
                TokenKsefZaszyfrowany =
                    new OchronaTokena(DataProtectionProvider.Create("Inne.Klucze"))
                        .Zaszyfruj(Token)
            };

            zakladanie.Firmy.Add(firma);
            await zakladanie.SaveChangesAsync();

            using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

            await using FirmaProDbContext kontekst = baza.UtworzKontekst(firma.Id);
            WynikDiagnostyki wynik = await Usluga(kontekst, atrapa).SprawdzAsync();

            KrokDiagnostyki krok = Krok(wynik, "Token KSeF");

            Assert.Equal(StanKroku.Blad, krok.Stan);
            Assert.Contains("nie daje się odszyfrować", krok.Komunikat, StringComparison.Ordinal);
            Assert.Contains("kluczy", krok.Wskazowka!, StringComparison.Ordinal);
        }
    }
}
