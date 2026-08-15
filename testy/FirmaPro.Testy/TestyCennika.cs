using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Web.Uslugi;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Kartoteka towarów i usług.
/// </summary>
/// <remarks>
/// Cennik podaje wartości początkowe wiersza faktury, więc jego błąd nie
/// psuje wystawionych dokumentów - ale wpisana raz zła stawka wracałaby na
/// każdą kolejną fakturę. Stąd sprawdzanie stawki przy zapisie.
/// </remarks>
[Collection(KolekcjaBazy.Nazwa)]
public sealed class TestyCennika(BazaTestowa baza)
{
    private async Task<Guid> FirmaAsync()
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        var firma = new Firma
        {
            Nazwa = "Alfa sp. z o.o.",
            Nip = "5252248481",
            AdresLinia1 = "ul. Prosta 51"
        };

        kontekst.Firmy.Add(firma);
        await kontekst.SaveChangesAsync();

        return firma.Id;
    }

    [Fact]
    public async Task NowaPozycjaTrafiaDoKartoteki()
    {
        Guid firma = await FirmaAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firma);
        var usluga = new UslugaCennika(kontekst);

        WynikZapisuCennika wynik = await usluga.ZapiszAsync(
            null, "Przegląd instalacji", "usł.", 350m, "23",
            null, null, null, "PRZ-01", true);

        Assert.True(wynik.Udalo);
        Assert.Equal("Przegląd instalacji", wynik.Pozycja!.Nazwa);
        Assert.Equal(350m, wynik.Pozycja.CenaNetto);
        Assert.Single(await usluga.DoWyboruAsync());
    }

    [Fact]
    public async Task NazwaJestWymagana()
    {
        Guid firma = await FirmaAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firma);
        var usluga = new UslugaCennika(kontekst);

        WynikZapisuCennika wynik = await usluga.ZapiszAsync(
            null, "   ", "szt.", 10m, "23", null, null, null, null, true);

        Assert.False(wynik.Udalo);
        Assert.Contains(wynik.Walidacja.Problemy, p => p.Pole == "Nazwa");
    }

    /// <summary>
    /// Zła stawka wracałaby na każdą kolejną fakturę, więc odsiewamy ją tutaj.
    /// </summary>
    [Fact]
    public async Task NieznanaStawkaNiePrzechodzi()
    {
        Guid firma = await FirmaAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firma);
        var usluga = new UslugaCennika(kontekst);

        WynikZapisuCennika wynik = await usluga.ZapiszAsync(
            null, "Usługa", "usł.", 100m, "17", null, null, null, null, true);

        Assert.False(wynik.Udalo);
        Assert.Contains(wynik.Walidacja.Problemy, p => p.Pole == "KodStawki");
    }

    [Fact]
    public async Task CenaNieMozeBycUjemna()
    {
        Guid firma = await FirmaAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firma);
        var usluga = new UslugaCennika(kontekst);

        WynikZapisuCennika wynik = await usluga.ZapiszAsync(
            null, "Usługa", "usł.", -1m, "23", null, null, null, null, true);

        Assert.False(wynik.Udalo);
        Assert.Contains(wynik.Walidacja.Problemy, p => p.Pole == "CenaNetto");
    }

    /// <summary>
    /// Wycofana pozycja znika z podpowiedzi, ale zostaje w kartotece.
    /// </summary>
    /// <remarks>
    /// Kasowanie zabrałoby ze sobą ślad tego, co i po ile sprzedawano.
    /// </remarks>
    [Fact]
    public async Task WycofanaPozycjaZnikaZPodpowiedziAleZostajeWKartotece()
    {
        Guid firma = await FirmaAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firma);
        var usluga = new UslugaCennika(kontekst);

        WynikZapisuCennika dodana = await usluga.ZapiszAsync(
            null, "Towar wycofany", "szt.", 20m, "23", null, null, null, null, true);

        Assert.True(await usluga.PrzelaczAktywnoscAsync(dodana.Pozycja!.Id));

        Assert.Empty(await usluga.DoWyboruAsync());
        Assert.Single(await usluga.ListaAsync());
    }

    /// <summary>Poprawienie pozycji nie zakłada drugiej.</summary>
    [Fact]
    public async Task PoprawienieZmieniaIstniejacaPozycje()
    {
        Guid firma = await FirmaAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firma);
        var usluga = new UslugaCennika(kontekst);

        WynikZapisuCennika dodana = await usluga.ZapiszAsync(
            null, "Usługa", "usł.", 100m, "23", null, null, null, null, true);

        WynikZapisuCennika poprawiona = await usluga.ZapiszAsync(
            dodana.Pozycja!.Id, "Usługa po podwyżce", "usł.", 120m, "23",
            null, null, null, null, true);

        Assert.True(poprawiona.Udalo);
        Assert.Equal(dodana.Pozycja.Id, poprawiona.Pozycja!.Id);
        Assert.Single(await usluga.ListaAsync());
        Assert.Equal(120m, (await usluga.ListaAsync())[0].CenaNetto);
    }

    /// <summary>
    /// Najważniejszy test: cennik jednej firmy nie może wyciec do drugiej.
    /// </summary>
    [Fact]
    public async Task CennikNieWyciekaMiedzyFirmami()
    {
        Guid pierwsza = await FirmaAsync();
        Guid druga = await FirmaAsync();

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            await new UslugaCennika(kontekst).ZapiszAsync(
                null, "Towar firmy Alfa", "szt.", 10m, "23", null, null, null, null, true);
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(druga))
        {
            Assert.Empty(await new UslugaCennika(kontekst).ListaAsync());
        }
    }

    /// <summary>Pozycji innej firmy nie da się poprawić po samym numerze.</summary>
    [Fact]
    public async Task NieDaSiePoprawicPozycjiInnejFirmy()
    {
        Guid pierwsza = await FirmaAsync();
        Guid druga = await FirmaAsync();
        Guid pozycjaId;

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            WynikZapisuCennika dodana = await new UslugaCennika(kontekst).ZapiszAsync(
                null, "Towar firmy Alfa", "szt.", 10m, "23", null, null, null, null, true);

            pozycjaId = dodana.Pozycja!.Id;
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(druga))
        {
            WynikZapisuCennika proba = await new UslugaCennika(kontekst).ZapiszAsync(
                pozycjaId, "Podmienione", "szt.", 1m, "23", null, null, null, null, true);

            Assert.False(proba.Udalo);
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            PozycjaCennika pozycja = await kontekst.Cennik.SingleAsync();
            Assert.Equal("Towar firmy Alfa", pozycja.Nazwa);
        }
    }
}
