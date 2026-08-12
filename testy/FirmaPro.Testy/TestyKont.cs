using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Zakładanie kont, zapraszanie do firmy i odbieranie dostępu.
/// </summary>
/// <remarks>
/// Każdy test dostaje własną bazę: wszystkie kręcą się wokół tego, kto jest
/// w firmie, więc na wspólnej bazie sprawdzałyby głównie kolejność swojego
/// wykonania.
/// </remarks>
public sealed class TestyKont : IAsyncLifetime
{
    private readonly BazaTestowa _baza = new();
    private readonly CzasTestowy _czas =
        new(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => _baza.InitializeAsync();

    public Task DisposeAsync() => _baza.DisposeAsync();

    private UslugaKont Usluga(FirmaProDbContext kontekst) =>
        new(kontekst, new PasswordHasher<object>(), _czas);

    /// <summary>Zakłada firmę i zwraca ją wraz z identyfikatorem właściciela.</summary>
    private async Task<(Guid FirmaId, Guid WlascicielId)> ZalozFirmeAsync()
    {
        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(null);

        WynikKonta<CzlonkostwoWFirmie> wynik = await Usluga(kontekst).ZarejestrujAsync(
            "wlasciciel@example.pl", "bardzodlugiehaslo", "bardzodlugiehaslo",
            "Alfa sp. z o.o.", "5252248481");

        Assert.True(wynik.Udalo);
        return (wynik.Dane!.FirmaId, wynik.Dane.UzytkownikId);
    }

    // ------------------------------------------------------------ rejestracja

    [Fact]
    public async Task RejestracjaZakladaFirmeIczyniZakladajacegoWlascicielem()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);

        CzlonkostwoWFirmie czlonkostwo = await kontekst.Czlonkostwa
            .Include(c => c.Uzytkownik)
            .SingleAsync(c => c.UzytkownikId == wlascicielId);

        Assert.Equal(RolaWFirmie.Wlasciciel, czlonkostwo.Rola);
        Assert.NotEqual("bardzodlugiehaslo", czlonkostwo.Uzytkownik!.HaszHasla);
    }

    [Fact]
    public async Task DrugieKontoNaTenSamAdresNiePowstaje()
    {
        await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(null);

        // Wielkość liter w adresie nie ma znaczenia - inaczej dałoby się
        // założyć drugie konto "obok" istniejącego.
        WynikKonta<CzlonkostwoWFirmie> wynik = await Usluga(kontekst).ZarejestrujAsync(
            "WLASCICIEL@example.pl", "innebardzodlugie", "innebardzodlugie",
            "Beta S.A.", "7010001453");

        Assert.False(wynik.Udalo);
        Assert.Contains(wynik.Walidacja.Problemy, p => p.Pole == "Email");
    }

    [Theory]
    [InlineData("krotkie", "krotkie", "Haslo")]
    [InlineData("bardzodlugiehaslo", "cosinnego", "PowtorzHaslo")]
    public async Task SlabeAlboNiezgodneHasloNiePrzechodzi(string haslo, string powtorzenie,
                                                           string pole)
    {
        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(null);

        WynikKonta<CzlonkostwoWFirmie> wynik = await Usluga(kontekst).ZarejestrujAsync(
            "nowy@example.pl", haslo, powtorzenie, "Gamma sp. j.", "5252248481");

        Assert.False(wynik.Udalo);
        Assert.Contains(wynik.Walidacja.Problemy, p => p.Pole == pole);
    }

    [Fact]
    public async Task BlednyNipZatrzymujeRejestracje()
    {
        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(null);

        WynikKonta<CzlonkostwoWFirmie> wynik = await Usluga(kontekst).ZarejestrujAsync(
            "nowy@example.pl", "bardzodlugiehaslo", "bardzodlugiehaslo",
            "Delta sp. z o.o.", "1234567890");

        Assert.False(wynik.Udalo);
        Assert.Contains(wynik.Walidacja.Problemy, p => p.Pole == "Nip");
    }

    // ------------------------------------------------------------ zaproszenia

    [Fact]
    public async Task ZaproszonyDolaczaDoFirmyZNadanaRola()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();
        string kod;

        await using (FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId))
        {
            WynikKonta<Zaproszenie> zaproszenie = await Usluga(kontekst).ZaproscAsync(
                "ksiegowa@example.pl", RolaWFirmie.Ksiegowy, wlascicielId);

            Assert.True(zaproszenie.Udalo);
            kod = zaproszenie.Dane!.Kod;
        }

        // Zaproszony jeszcze do firmy nie należy, więc przyjmuje zaproszenie
        // bez kontekstu firmy - tak jak przez przeglądarkę.
        await using (FirmaProDbContext kontekst = _baza.UtworzKontekst(null))
        {
            WynikKonta<CzlonkostwoWFirmie> wynik = await Usluga(kontekst)
                .PrzyjmijZaproszenieAsync(kod, "Anna Nowak", "bardzodlugiehaslo");

            Assert.True(wynik.Udalo);
            Assert.Equal(firmaId, wynik.Dane!.FirmaId);
            Assert.Equal(RolaWFirmie.Ksiegowy, wynik.Dane.Rola);
        }

        await using (FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId))
        {
            Assert.Equal(2, await kontekst.Czlonkostwa.CountAsync());
        }
    }

    [Fact]
    public async Task ZaproszenieDzialaTylkoRaz()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        string kod = (await usluga.ZaproscAsync(
            "ksiegowa@example.pl", RolaWFirmie.Ksiegowy, wlascicielId)).Dane!.Kod;

        Assert.True((await usluga.PrzyjmijZaproszenieAsync(kod, "Anna", "bardzodlugiehaslo")).Udalo);

        WynikKonta<CzlonkostwoWFirmie> druga = await usluga.PrzyjmijZaproszenieAsync(
            kod, "Ktoś inny", "bardzodlugiehaslo");

        Assert.False(druga.Udalo);
        Assert.Null(await usluga.ZnajdzZaproszenieAsync(kod));
    }

    [Fact]
    public async Task ZaproszeniePrzestajeDzialacPoTerminie()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        string kod = (await usluga.ZaproscAsync(
            "spozniony@example.pl", RolaWFirmie.Ksiegowy, wlascicielId)).Dane!.Kod;

        _czas.Przesun(TimeSpan.FromDays(UslugaKont.DniWaznosciZaproszenia) + TimeSpan.FromMinutes(1));

        Assert.Null(await usluga.ZnajdzZaproszenieAsync(kod));
        Assert.False((await usluga.PrzyjmijZaproszenieAsync(kod, "Kto", "bardzodlugiehaslo")).Udalo);
    }

    /// <summary>
    /// Zaproszenie na adres istniejącego konta wymaga hasła do tego konta.
    /// </summary>
    /// <remarks>
    /// Inaczej wystawienie zaproszenia na cudzy adres byłoby sposobem na
    /// przejęcie konta razem z wszystkimi firmami, do których należy.
    /// </remarks>
    [Fact]
    public async Task DolaczenieIstniejacymKontemWymagaJegoHasla()
    {
        (Guid pierwsza, Guid wlascicielId) = await ZalozFirmeAsync();

        Guid druga;
        await using (FirmaProDbContext kontekst = _baza.UtworzKontekst(null))
        {
            druga = (await Usluga(kontekst).ZarejestrujAsync(
                "ksiegowa@example.pl", "hasloksiegowej1", "hasloksiegowej1",
                "Beta S.A.", "7010001453")).Dane!.FirmaId;
        }

        await using FirmaProDbContext wPierwszej = _baza.UtworzKontekst(pierwsza);
        UslugaKont usluga = Usluga(wPierwszej);

        string kod = (await usluga.ZaproscAsync(
            "ksiegowa@example.pl", RolaWFirmie.Ksiegowy, wlascicielId)).Dane!.Kod;

        Assert.False((await usluga.PrzyjmijZaproszenieAsync(kod, null, "zlehaslodlugie")).Udalo);

        WynikKonta<CzlonkostwoWFirmie> wynik =
            await usluga.PrzyjmijZaproszenieAsync(kod, null, "hasloksiegowej1");

        Assert.True(wynik.Udalo);
        Assert.Equal(pierwsza, wynik.Dane!.FirmaId);

        // Jedno konto, dwie firmy - tak pracują biura rachunkowe.
        IReadOnlyList<CzlonkostwoWFirmie> firmy =
            await usluga.FirmyUzytkownikaAsync(wynik.Dane.UzytkownikId);

        Assert.Equal(2, firmy.Count);
        Assert.Contains(firmy, c => c.FirmaId == druga);
    }

    [Fact]
    public async Task NieMoznaZaprosicKogosKtoJuzJestWFirmie()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);

        WynikKonta<Zaproszenie> wynik = await Usluga(kontekst).ZaproscAsync(
            "wlasciciel@example.pl", RolaWFirmie.Ksiegowy, wlascicielId);

        Assert.False(wynik.Udalo);
    }

    /// <summary>Zaproszenie z jednej firmy nie może otworzyć drugiej.</summary>
    [Fact]
    public async Task ZaproszenieNieWidacZInnejFirmy()
    {
        (Guid pierwsza, Guid wlascicielId) = await ZalozFirmeAsync();

        Guid druga;
        await using (FirmaProDbContext kontekst = _baza.UtworzKontekst(null))
        {
            druga = (await Usluga(kontekst).ZarejestrujAsync(
                "obcy@example.pl", "bardzodlugiehaslo", "bardzodlugiehaslo",
                "Beta S.A.", "7010001453")).Dane!.FirmaId;
        }

        await using (FirmaProDbContext wPierwszej = _baza.UtworzKontekst(pierwsza))
        {
            await Usluga(wPierwszej).ZaproscAsync(
                "ktos@example.pl", RolaWFirmie.Ksiegowy, wlascicielId);
        }

        await using FirmaProDbContext wDrugiej = _baza.UtworzKontekst(druga);
        Assert.Empty(await Usluga(wDrugiej).ZaproszeniaAsync());
    }

    // ---------------------------------------------------------------- dostęp

    [Fact]
    public async Task NieMoznaOdebracDostepuJedynemuWlascicielowi()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        Guid czlonkostwoId = (await usluga.CzlonkowieAsync())
            .Single(c => c.UzytkownikId == wlascicielId).Id;

        Assert.False((await usluga.OdbierzDostepAsync(czlonkostwoId)).Udalo);
        Assert.False((await usluga.ZmienRoleAsync(czlonkostwoId, RolaWFirmie.Podglad)).Udalo);
        Assert.Single(await usluga.CzlonkowieAsync());
    }

    [Fact]
    public async Task PoWskazaniuDrugiegoWlascicielaPierwszyMozeOdejsc()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        string kod = (await usluga.ZaproscAsync(
            "wspolnik@example.pl", RolaWFirmie.Wlasciciel, wlascicielId)).Dane!.Kod;

        await usluga.PrzyjmijZaproszenieAsync(kod, "Wspólnik", "bardzodlugiehaslo");

        Guid czlonkostwoId = (await usluga.CzlonkowieAsync())
            .Single(c => c.UzytkownikId == wlascicielId).Id;

        Assert.True((await usluga.OdbierzDostepAsync(czlonkostwoId)).Udalo);
        Assert.Single(await usluga.CzlonkowieAsync());
    }

    // ------------------------------------------------------------ własne konto

    [Fact]
    public async Task ZmianaHaslaWymagaObecnegoHasla()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        WynikKonta<Uzytkownik> zleObecne = await usluga.ZmienHasloAsync(
            wlascicielId, "nieprawidlowehaslo", "nowedlugiehaslo1", "nowedlugiehaslo1");

        Assert.False(zleObecne.Udalo);
        Assert.Contains(zleObecne.Walidacja.Problemy, p => p.Pole == "ObecneHaslo");

        WynikKonta<Uzytkownik> krotkie = await usluga.ZmienHasloAsync(
            wlascicielId, "bardzodlugiehaslo", "krotkie", "krotkie");

        Assert.False(krotkie.Udalo);
        Assert.Contains(krotkie.Walidacja.Problemy, p => p.Pole == "Haslo");
    }

    /// <summary>
    /// Zmiana hasła zmienia stempel bezpieczeństwa.
    /// </summary>
    /// <remarks>
    /// Stempel siedzi w ciasteczku logowania i jest sprawdzany przy każdym
    /// żądaniu. Gdyby się nie zmieniał, ciasteczko wykradzione przed zmianą
    /// hasła działałoby dalej - a zmiana hasła robiona jest zwykle właśnie
    /// dlatego, że coś wyciekło.
    /// </remarks>
    [Fact]
    public async Task ZmianaHaslaUniewazniaWczesniejszeSesje()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        Guid przed = (await kontekst.Uzytkownicy.SingleAsync(u => u.Id == wlascicielId))
            .StempelBezpieczenstwa;

        Assert.True((await usluga.ZmienHasloAsync(
            wlascicielId, "bardzodlugiehaslo", "zupelnienowehaslo", "zupelnienowehaslo")).Udalo);

        Guid po = (await kontekst.Uzytkownicy.SingleAsync(u => u.Id == wlascicielId))
            .StempelBezpieczenstwa;

        Assert.NotEqual(przed, po);
    }

    // ------------------------------------------------------------ reset hasła

    [Fact]
    public async Task OdnosnikDoZmianyHaslaDzialaRazIWygasa()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        string kod = (await usluga.WystawResetAsync(wlascicielId)).Kod;

        Assert.True((await usluga.UstawNoweHasloAsync(
            kod, "calkiemnowehaslo", "calkiemnowehaslo")).Udalo);

        // Drugie użycie tego samego odnośnika już nie przechodzi.
        Assert.False((await usluga.UstawNoweHasloAsync(
            kod, "jeszczeinnehaslo", "jeszczeinnehaslo")).Udalo);

        // Nowy odnośnik przestaje działać po upływie terminu.
        string drugi = (await usluga.WystawResetAsync(wlascicielId)).Kod;
        _czas.Przesun(TimeSpan.FromHours(UslugaKont.GodzinWaznosciResetu) + TimeSpan.FromMinutes(1));

        Assert.Null(await usluga.ZnajdzResetAsync(drugi));
        Assert.False((await usluga.UstawNoweHasloAsync(drugi, "jeszczeinne1234", "jeszczeinne1234")).Udalo);
    }

    [Fact]
    public async Task NowyOdnosnikUniewazniaPoprzedni()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        string pierwszy = (await usluga.WystawResetAsync(wlascicielId)).Kod;
        string drugi = (await usluga.WystawResetAsync(wlascicielId)).Kod;

        // W obiegu ma być najwyżej jeden odnośnik: stary, wykradziony, nie
        // może czekać na swoją okazję.
        Assert.Null(await usluga.ZnajdzResetAsync(pierwszy));
        Assert.NotNull(await usluga.ZnajdzResetAsync(drugi));
    }

    [Fact]
    public async Task PoZmianieHaslaLogujeSieNowym()
    {
        (Guid firmaId, Guid wlascicielId) = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = _baza.UtworzKontekst(firmaId);
        UslugaKont usluga = Usluga(kontekst);

        string kod = (await usluga.WystawResetAsync(wlascicielId)).Kod;
        await usluga.UstawNoweHasloAsync(kod, "calkiemnowehaslo", "calkiemnowehaslo");

        Uzytkownik uzytkownik = await kontekst.Uzytkownicy.SingleAsync(u => u.Id == wlascicielId);
        var haszowanie = new PasswordHasher<object>();

        Assert.Equal(PasswordVerificationResult.Success,
            haszowanie.VerifyHashedPassword(new object(), uzytkownik.HaszHasla, "calkiemnowehaslo"));
        Assert.Equal(PasswordVerificationResult.Failed,
            haszowanie.VerifyHashedPassword(new object(), uzytkownik.HaszHasla, "bardzodlugiehaslo"));
    }
}
