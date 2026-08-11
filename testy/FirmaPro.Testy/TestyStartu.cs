using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Decyzje podejmowane przy starcie programu.
/// </summary>
/// <remarks>
/// Wdrożenie psuje się inaczej niż kod: nie wywala się przy kompilacji, tylko
/// po cichu wpuszcza kogoś do ksiąg albo gubi zaszyfrowany token. Te reguły
/// mają więc własne testy.
/// </remarks>
public class TestyUstawienStartu
{
    private static IConfiguration Ustawienia(params (string Klucz, string Wartosc)[] pary) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pary.Select(p =>
                new KeyValuePair<string, string?>(p.Klucz, p.Wartosc)))
            .Build();

    [Fact]
    public void DaneDemonstracyjnePowstajaTylkoPrzyPracyNadProgramem()
    {
        IConfiguration puste = Ustawienia();

        Assert.True(UstawieniaStartu.CzyZakladacDaneDemonstracyjne(puste, czyDeweloperskie: true));
        Assert.False(UstawieniaStartu.CzyZakladacDaneDemonstracyjne(puste, czyDeweloperskie: false));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("cokolwiek", false)]
    public void JawneUstawienieRozstrzygaOSprawieDanychDemonstracyjnych(
        string wartosc, bool oczekiwane)
    {
        IConfiguration ustawienia = Ustawienia(("Aplikacja:DaneDemonstracyjne", wartosc));

        Assert.Equal(oczekiwane,
            UstawieniaStartu.CzyZakladacDaneDemonstracyjne(ustawienia, czyDeweloperskie: false));
        Assert.Equal(oczekiwane,
            UstawieniaStartu.CzyZakladacDaneDemonstracyjne(ustawienia, czyDeweloperskie: true));
    }

    /// <summary>
    /// Brak katalogu kluczy zatrzymuje start programu poza trybem roboczym.
    /// </summary>
    /// <remarks>
    /// Skutek przeoczenia byłby cichy: po wymianie kontenera token KSeF
    /// przestaje się odczytywać, a program mówi tylko „brak tokena". Lepiej,
    /// żeby nie wstał w ogóle.
    /// </remarks>
    [Fact]
    public void BrakKatalogaKluczyZatrzymujeStartNaSerwerze()
    {
        InvalidOperationException blad = Assert.Throws<InvalidOperationException>(
            () => UstawieniaStartu.KatalogKluczy(Ustawienia(), czyDeweloperskie: false));

        Assert.Contains("KatalogKluczy", blad.Message, StringComparison.Ordinal);
        Assert.Null(UstawieniaStartu.KatalogKluczy(Ustawienia(), czyDeweloperskie: true));
    }

    [Fact]
    public void BrakUstawienPierwszegoKontaNieJestBledem()
    {
        Assert.Null(UstawieniaStartu.PierwszeKontoZUstawien(Ustawienia()));
    }

    [Fact]
    public void PierwszeKontoWymagaDlugiegoHasla()
    {
        IConfiguration ustawienia = Ustawienia(
            ("Aplikacja:PierwszeKonto:Email", "wlasciciel@example.pl"),
            ("Aplikacja:PierwszeKonto:Haslo", "krotkie"),
            ("Aplikacja:PierwszeKonto:Firma", "Firma sp. z o.o."),
            ("Aplikacja:PierwszeKonto:Nip", "5252248481"));

        InvalidOperationException blad = Assert.Throws<InvalidOperationException>(
            () => UstawieniaStartu.PierwszeKontoZUstawien(ustawienia));

        Assert.Contains("Hasło", blad.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PierwszeKontoWymagaAdresuINazwyFirmy()
    {
        Assert.Throws<InvalidOperationException>(() => UstawieniaStartu.PierwszeKontoZUstawien(
            Ustawienia(
                ("Aplikacja:PierwszeKonto:Email", "bez-malpy"),
                ("Aplikacja:PierwszeKonto:Haslo", "dostateczniedlugie"))));

        Assert.Throws<InvalidOperationException>(() => UstawieniaStartu.PierwszeKontoZUstawien(
            Ustawienia(
                ("Aplikacja:PierwszeKonto:Email", "wlasciciel@example.pl"),
                ("Aplikacja:PierwszeKonto:Haslo", "dostateczniedlugie"))));
    }

    [Fact]
    public void PoprawneUstawieniaDajaDaneKonta()
    {
        PierwszeKonto? konto = UstawieniaStartu.PierwszeKontoZUstawien(Ustawienia(
            ("Aplikacja:PierwszeKonto:Email", " wlasciciel@example.pl "),
            ("Aplikacja:PierwszeKonto:Haslo", "dostateczniedlugie"),
            ("Aplikacja:PierwszeKonto:Firma", " Firma sp. z o.o. "),
            ("Aplikacja:PierwszeKonto:Nip", "525-224-84-81")));

        Assert.NotNull(konto);
        Assert.Equal("wlasciciel@example.pl", konto.Email);
        Assert.Equal("Firma sp. z o.o.", konto.Firma);
        Assert.Equal("5252248481", konto.Nip);
    }
}

/// <summary>
/// Trwałość kluczy ochrony danych.
/// </summary>
/// <remarks>
/// To jest istota poprawki: token KSeF zaszyfrowany przed restartem musi dać
/// się odczytać po restarcie. Bez tego program po wymianie kontenera zgłasza
/// zwyczajny „brak tokena" i nikt nie kojarzy przyczyny ze skutkiem.
/// </remarks>
public class TestyTrwalosciKluczy : IDisposable
{
    private readonly string _katalog = Path.Combine(
        Path.GetTempPath(), "firmapro-klucze-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose()
    {
        if (Directory.Exists(_katalog))
        {
            Directory.Delete(_katalog, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Buduje ochronę tak, jak robi to program po starcie.</summary>
    private static OchronaTokena Ochrona(string? katalog)
    {
        var uslugi = new ServiceCollection();
        IDataProtectionBuilder ochrona = uslugi.AddDataProtection().SetApplicationName("FirmaPro");

        if (katalog is not null)
        {
            Directory.CreateDirectory(katalog);
            ochrona.PersistKeysToFileSystem(new DirectoryInfo(katalog));
        }

        return new OchronaTokena(
            uslugi.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>());
    }

    [Fact]
    public void TokenPrzezywaRestartGdyKluczeLezaNaDysku()
    {
        byte[] zaszyfrowany = Ochrona(_katalog).Zaszyfruj("TOKEN-KSEF-123");

        // Drugie uruchomienie programu - nowy dostawca, ten sam katalog.
        Assert.Equal("TOKEN-KSEF-123", Ochrona(_katalog).Odszyfruj(zaszyfrowany));
    }

    [Fact]
    public void BezTrwalychKluczyTokenPrzepada()
    {
        string inny = _katalog + "-inny";

        try
        {
            byte[] zaszyfrowany = Ochrona(_katalog).Zaszyfruj("TOKEN-KSEF-123");

            // Klucze z innego katalogu to sytuacja kontenera bez woluminu:
            // szyfrogram zostaje, klucz przepada. Program ma to znieść bez
            // wywrotki - i zgłosić brak tokena, a nie błąd serwera.
            Assert.Null(Ochrona(inny).Odszyfruj(zaszyfrowany));
        }
        finally
        {
            if (Directory.Exists(inny))
            {
                Directory.Delete(inny, recursive: true);
            }
        }
    }
}

/// <summary>
/// Zakładanie pierwszego konta właściciela.
/// </summary>
/// <remarks>
/// Każdy z tych testów dostaje własną bazę, bo wszystkie zależą od tego, czy
/// w bazie jest już jakikolwiek użytkownik - na wspólnej bazie sprawdzałyby
/// głównie kolejność swojego wykonania.
/// </remarks>
public sealed class TestyPierwszegoKonta : IAsyncLifetime
{
    private readonly BazaTestowa baza = new();

    public Task InitializeAsync() => baza.InitializeAsync();

    public Task DisposeAsync() => baza.DisposeAsync();

    private static readonly PierwszeKonto Konto = new(
        "wlasciciel@example.pl", "dostateczniedlugie", "Firma sp. z o.o.", "5252248481");

    private static UslugaZakladania Usluga(FirmaProDbContext kontekst) =>
        new(kontekst, new PasswordHasher<object>(), NullLogger<UslugaZakladania>.Instance);

    [Fact]
    public async Task PierwszeKontoPowstajeZUstawien()
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        Assert.True(await Usluga(kontekst).ZalozPierwszeKontoAsync(Konto));

        Uzytkownik uzytkownik = await kontekst.Uzytkownicy
            .SingleAsync(u => u.Email == Konto.Email);

        Assert.NotEqual(Konto.Haslo, uzytkownik.HaszHasla);

        Firma firma = await kontekst.Firmy.SingleAsync(f => f.Nazwa == Konto.Firma);
        Assert.Equal("5252248481", firma.Nip);

        CzlonkostwoWFirmie czlonkostwo = await kontekst.Czlonkostwa
            .SingleAsync(c => c.UzytkownikId == uzytkownik.Id);

        Assert.Equal(RolaWFirmie.Wlasciciel, czlonkostwo.Rola);
        Assert.Equal(firma.Id, czlonkostwo.FirmaId);
    }

    /// <summary>
    /// Ponowny start nie może dołożyć drugiego konta ani nadpisać hasła.
    /// </summary>
    /// <remarks>
    /// Ustawienia wdrożenia zostają na serwerze na stałe, więc ten kod
    /// wykonuje się przy każdym uruchomieniu programu.
    /// </remarks>
    [Fact]
    public async Task PonownyStartNieDokladaDrugiegoKonta()
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        await Usluga(kontekst).ZalozPierwszeKontoAsync(Konto);
        int poPierwszym = await kontekst.Uzytkownicy.CountAsync();

        Assert.False(await Usluga(kontekst).ZalozPierwszeKontoAsync(
            Konto with { Email = "ktos.inny@example.pl" }));

        Assert.Equal(poPierwszym, await kontekst.Uzytkownicy.CountAsync());
    }

    [Fact]
    public async Task BlednyNipZatrzymujeStart()
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Usluga(kontekst).ZalozPierwszeKontoAsync(Konto with { Nip = "1234567890" }));
    }
}
