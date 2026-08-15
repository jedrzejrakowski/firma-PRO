using System.Security.Cryptography.X509Certificates;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Certyfikat zapisany przy firmie i uwierzytelnianie nim.
/// </summary>
/// <remarks>
/// Sedno tych testów: wybór metody w ustawieniach ma naprawdę zmieniać
/// sposób logowania, a nie tylko wygląd ekranu. Atrapa serwera rozróżnia
/// obie drogi, więc test widzi, którą program poszedł.
/// </remarks>
[Collection(KolekcjaBazy.Nazwa)]
public sealed class TestyCertyfikatuFirmy(BazaTestowa baza)
{
    private const string Nip = "5252248481";

    private static readonly OchronaTokena Ochrona =
        new(DataProtectionProvider.Create("FirmaPro.Testy"));

    private async Task<Guid> ZalozFirmeAsync(
        MetodaUwierzytelnieniaKsef metoda = MetodaUwierzytelnieniaKsef.Certyfikat)
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        var firma = new Firma
        {
            Nazwa = "Moja Firma sp. z o.o.",
            Nip = Nip,
            AdresLinia1 = "ul. Prosta 51",
            Srodowisko = SrodowiskoKsef.Test,
            MetodaUwierzytelnienia = metoda,
            TokenKsefZaszyfrowany = Ochrona.Zaszyfruj("TOKEN-KSEF-123")
        };

        kontekst.Firmy.Add(firma);
        await kontekst.SaveChangesAsync();

        return firma.Id;
    }

    private static UslugaCertyfikatuKsef Usluga(FirmaProDbContext kontekst,
                                                TimeProvider? czas = null) =>
        new(kontekst, Ochrona, czas ?? TimeProvider.System);

    [Fact]
    public async Task WgranyCertyfikatDaSieOdczytacZPowrotem()
    {
        Guid firmaId = await ZalozFirmeAsync();

        using X509Certificate2 zrodlowy = CertyfikatTestowy.DlaFirmy(Nip);
        byte[] pfx = zrodlowy.Export(X509ContentType.Pfx, "tajne");

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikWalidacji wynik = await Usluga(kontekst).ZapiszAsync(pfx, "tajne");

        Assert.False(wynik.SaBledy, wynik.Opis());

        Firma firma = kontekst.Firmy.Single(f => f.Id == firmaId);
        using X509Certificate2? odczytany = UslugaCertyfikatuKsef.Odczytaj(firma, Ochrona);

        Assert.NotNull(odczytany);
        Assert.Equal(zrodlowy.Thumbprint, odczytany.Thumbprint);

        // Klucz prywatny musi przetrwać zapis - bez niego nie da się podpisać.
        Assert.True(odczytany.HasPrivateKey);
    }

    [Fact]
    public async Task CertyfikatLezyWBazieWPostaciZaszyfrowanej()
    {
        Guid firmaId = await ZalozFirmeAsync();

        using X509Certificate2 zrodlowy = CertyfikatTestowy.DlaFirmy(Nip);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        await Usluga(kontekst).ZapiszAsync(zrodlowy.Export(X509ContentType.Pfx), null);

        byte[] zapisany = kontekst.Firmy.Single(f => f.Id == firmaId).CertyfikatKsefZaszyfrowany!;

        // Gdyby plik leżał otwartym tekstem, zaczynałby się od nagłówka PKCS#12.
        Assert.NotEqual(0x30, zapisany[0]);
    }

    [Fact]
    public async Task BledneHasloDajeCzytelnyKomunikat()
    {
        Guid firmaId = await ZalozFirmeAsync();

        using X509Certificate2 zrodlowy = CertyfikatTestowy.DlaFirmy(Nip);
        byte[] pfx = zrodlowy.Export(X509ContentType.Pfx, "tajne");

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikWalidacji wynik = await Usluga(kontekst).ZapiszAsync(pfx, "nie-to-haslo");

        Assert.True(wynik.SaBledy);
        Assert.Contains("hasło", wynik.Opis(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CertyfikatBezKluczaPrywatnegoJestOdrzucany()
    {
        Guid firmaId = await ZalozFirmeAsync();

        using X509Certificate2 zrodlowy = CertyfikatTestowy.DlaFirmy(Nip);
        byte[] samCertyfikat = zrodlowy.Export(X509ContentType.Cert);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        WynikWalidacji wynik = await Usluga(kontekst).ZapiszAsync(samCertyfikat, null);

        Assert.True(wynik.SaBledy);
        Assert.Contains("klucza prywatnego", wynik.Opis(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Program ostrzega, zanim certyfikat wygaśnie.
    /// </summary>
    /// <remarks>
    /// Certyfikat, w odróżnieniu od tokena, przestaje działać po cichu
    /// w środku miesiąca - i zatrzymuje wysyłkę faktur.
    /// </remarks>
    [Fact]
    public async Task ZblizajacyySieKoniecWaznosciJestWidoczny()
    {
        Guid firmaId = await ZalozFirmeAsync();

        using X509Certificate2 zrodlowy = CertyfikatTestowy.DlaFirmy(Nip);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        await Usluga(kontekst).ZapiszAsync(zrodlowy.Export(X509ContentType.Pfx), null);

        // Certyfikat testowy jest ważny dwa lata - przesuwamy zegar tak,
        // żeby zostało dziesięć dni.
        var zaChwile = new CzasTestowy(
            zrodlowy.NotAfter.ToUniversalTime().AddDays(-10));

        OpisCertyfikatu? opis = await Usluga(kontekst, zaChwile).OpisAsync();

        Assert.NotNull(opis);
        Assert.True(opis.WkrotceWygasnie, $"Zostało {opis.DniDoKonca} dni, a nie ostrzegamy.");
        Assert.False(opis.Wygasl);

        var poCzasie = new CzasTestowy(zrodlowy.NotAfter.ToUniversalTime().AddDays(1));
        Assert.True((await Usluga(kontekst, poCzasie).OpisAsync())!.Wygasl);
    }

    [Fact]
    public async Task CertyfikatuTestowegoNieDaSieWystawicNaProdukcji()
    {
        Guid firmaId = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        kontekst.Firmy.Single(f => f.Id == firmaId).Srodowisko = SrodowiskoKsef.Produkcja;
        await kontekst.SaveChangesAsync();

        WynikWalidacji wynik = await Usluga(kontekst).WystawTestowyAsync();

        Assert.True(wynik.SaBledy);
        Assert.Contains("produkcji", wynik.Opis(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WystawionyCertyfikatTestowyNiesieNumerNip()
    {
        Guid firmaId = await ZalozFirmeAsync();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        Assert.False((await Usluga(kontekst).WystawTestowyAsync()).SaBledy);

        Firma firma = kontekst.Firmy.Single(f => f.Id == firmaId);
        using X509Certificate2? certyfikat = UslugaCertyfikatuKsef.Odczytaj(firma, Ochrona);

        // To po tym polu KSeF rozpoznaje podmiot.
        Assert.Contains($"VATPL-{Nip}", certyfikat!.Subject, StringComparison.Ordinal);
        Assert.True(certyfikat.HasPrivateKey);
    }

    /// <summary>
    /// Wybór metody naprawdę zmienia sposób logowania.
    /// </summary>
    [Fact]
    public async Task FirmaZCertyfikatemLogujeSiePodpisem()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        await Usluga(kontekst).WystawTestowyAsync();

        Firma firma = kontekst.Firmy.Single(f => f.Id == firmaId);
        await UwierzytelnienieKsef.ZalogujAsync(atrapa.UtworzKlienta(), firma, Ochrona);

        Assert.True(atrapa.PodpisPoprawny, "Program nie poszedł drogą podpisu.");
        Assert.Equal(Nip, atrapa.PodpisanyNip);
    }

    [Fact]
    public async Task FirmaZTokenemLogujeSieTokenem()
    {
        Guid firmaId = await ZalozFirmeAsync(MetodaUwierzytelnieniaKsef.Token);
        using var atrapa = new AtrapaKsef();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        Firma firma = kontekst.Firmy.Single(f => f.Id == firmaId);

        await UwierzytelnienieKsef.ZalogujAsync(atrapa.UtworzKlienta(), firma, Ochrona);

        Assert.Null(atrapa.PodpisanyDokument);
        Assert.Contains("POST auth/ksef-token", atrapa.Wywolania, StringComparer.Ordinal);
    }

    /// <summary>
    /// Wybór certyfikatu bez wgrania go mówi wprost, czego brakuje.
    /// </summary>
    [Fact]
    public async Task BrakCertyfikatuPrzyWybranejMetodzieJestNazwany()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        Firma firma = kontekst.Firmy.Single(f => f.Id == firmaId);

        BladKsefException blad = await Assert.ThrowsAsync<BladKsefException>(() =>
            UwierzytelnienieKsef.ZalogujAsync(atrapa.UtworzKlienta(), firma, Ochrona));

        Assert.Contains("nie ma zapisanego certyfikatu", blad.Message, StringComparison.Ordinal);
    }

    /// <summary>Diagnostyka opisuje tę metodę, która jest wybrana.</summary>
    [Fact]
    public async Task DiagnostykaSprawdzaCertyfikatGdyGoWybrano()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        await Usluga(kontekst).WystawTestowyAsync();

        var diagnostyka = new UslugaDiagnostykiKsef(kontekst, new FabrykaZAtrapy(atrapa),
            Ochrona, TimeProvider.System, NullLogger<UslugaDiagnostykiKsef>.Instance);

        WynikDiagnostyki wynik = await diagnostyka.SprawdzAsync();

        Assert.True(wynik.Udalo, string.Join(" | ",
            wynik.Kroki.Select(k => $"{k.Nazwa}: {k.Stan} - {k.Komunikat}")));

        Assert.Contains(wynik.Kroki, k => k.Nazwa == "Certyfikat KSeF");
        Assert.DoesNotContain(wynik.Kroki, k => k.Nazwa == "Token KSeF");
        Assert.True(atrapa.PodpisPoprawny);
    }
}
