using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>Fabryka oddająca zawsze tego samego klienta - podpiętego do atrapy.</summary>
internal sealed class FabrykaZAtrapy(AtrapaKsef atrapa) : IFabrykaKlientowKsef
{
    public IKlientKsef Utworz(SrodowiskoKsef srodowisko) => atrapa.UtworzKlienta();
}

/// <summary>
/// Pobieranie faktur zakupu z KSeF do rejestru VAT.
/// </summary>
/// <remarks>
/// Testy rozmawiają z atrapą KSeF, ale zapisują do prawdziwego PostgreSQL -
/// bo najważniejsza reguła tego mechanizmu (żadna faktura nie może trafić do
/// rejestru dwa razy) opiera się na więzie unikalności w bazie.
/// </remarks>
[Collection(KolekcjaBazy.Nazwa)]
public sealed class TestyImportuZakupow(BazaTestowa baza)
{
    private const string Token = "TOKEN-KSEF-123";
    private static readonly DateOnly Od = new(2026, 8, 1);
    private static readonly DateOnly Do = new(2026, 8, 31);

    private static readonly OchronaTokena Ochrona =
        new OchronaTokena(DataProtectionProvider.Create("FirmaPro.Testy"));

    /// <summary>Zakłada firmę z zapisanym tokenem KSeF i zwraca jej identyfikator.</summary>
    private async Task<Guid> ZalozFirmeAsync(TypOkresu typOkresu = TypOkresu.Miesieczny)
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        var firma = new Firma
        {
            Nazwa = "Nabywca sp. z o.o.",
            Nip = "5252248481",
            AdresLinia1 = "ul. Prosta 51",
            Srodowisko = SrodowiskoKsef.Test,
            TypOkresuVat = typOkresu,
            TokenKsefZaszyfrowany = Ochrona.Zaszyfruj(Token)
        };

        kontekst.Firmy.Add(firma);
        await kontekst.SaveChangesAsync();

        return firma.Id;
    }

    private static UslugaImportuZakupow Usluga(FirmaProDbContext kontekst, AtrapaKsef atrapa) =>
        new(kontekst, new FabrykaZAtrapy(atrapa), Ochrona);

    private static DecyzjaImportu Decyzja(ZnalezionaFaktura znaleziona,
                                          RodzajZakupu rodzaj = RodzajZakupu.TowaryIUslugi,
                                          bool odliczany = true) =>
        new(znaleziona.Dane.NumerKsef, rodzaj, odliczany);

    [Fact]
    public async Task ZnalezionaFakturaTrafiaDoRejestruZDanymiZKsef()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        UslugaImportuZakupow usluga = Usluga(kontekst, atrapa);

        IReadOnlyList<ZnalezionaFaktura> znalezione = await usluga.SzukajAsync(Od, Do);
        ZnalezionaFaktura znaleziona = Assert.Single(znalezione);
        Assert.False(znaleziona.JuzWRejestrze);

        WynikImportu wynik = await usluga.ImportujAsync(Od, Do, [Decyzja(znaleziona)]);

        Assert.Null(wynik.Blad);
        Assert.Equal(1, wynik.Zaimportowano);
        Assert.Equal(0, wynik.Pominieto);

        FakturaZakupu faktura = await kontekst.FakturyZakupu.SingleAsync();
        Assert.Equal("FS/7/2026", faktura.Numer);
        Assert.Equal("Dostawca sp. z o.o.", faktura.SprzedawcaNazwa);
        Assert.Equal("7010001453", faktura.SprzedawcaNip);
        Assert.Equal(1000.00m, faktura.RazemNetto);
        Assert.Equal(230.00m, faktura.RazemVat);
        Assert.Equal(1230.00m, faktura.RazemBrutto);
        Assert.Equal("7010001453-20260807-0AAAAA-BBBBBB-CC", faktura.NumerKsef);
        Assert.Equal(new DateOnly(2026, 8, 7), faktura.DataWystawienia);
    }

    [Fact]
    public async Task DataPrzyjeciaPrzezKsefWyznaczaOkresOdliczenia()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        // Faktura wystawiona w sierpniu, ale przyjęta przez KSeF we wrześniu.
        // Nabywca ma do niej dostęp dopiero od dnia przyjęcia, więc wcześniej
        // niż we wrześniu odliczyć się nie da (art. 86 ust. 10b pkt 1).
        atrapa.FakturyZakupowe[0] = atrapa.FakturyZakupowe[0] with
        {
            DataPrzyjecia = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero)
        };

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        UslugaImportuZakupow usluga = Usluga(kontekst, atrapa);

        IReadOnlyList<ZnalezionaFaktura> znalezione = await usluga.SzukajAsync(Od, Do);
        await usluga.ImportujAsync(Od, Do, [Decyzja(Assert.Single(znalezione))]);

        FakturaZakupu faktura = await kontekst.FakturyZakupu.SingleAsync();
        Assert.Equal(new DateOnly(2026, 9, 2), faktura.DataWplywu);
        Assert.Equal(new OkresRozliczeniowy(2026, 9, TypOkresu.Miesieczny),
                     OkresRozliczeniowy.Dla(faktura.DataUjecia, TypOkresu.Miesieczny));
    }

    [Fact]
    public async Task PowtorzonyImportNieDodajeFakturyDrugiRaz()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        UslugaImportuZakupow usluga = Usluga(kontekst, atrapa);

        IReadOnlyList<ZnalezionaFaktura> pierwsze = await usluga.SzukajAsync(Od, Do);
        await usluga.ImportujAsync(Od, Do, [Decyzja(Assert.Single(pierwsze))]);

        // Drugie wejście na stronę: faktura jest już w rejestrze i program
        // ma to pokazać, a nie ukryć.
        IReadOnlyList<ZnalezionaFaktura> drugie = await usluga.SzukajAsync(Od, Do);
        Assert.True(Assert.Single(drugie).JuzWRejestrze);

        WynikImportu wynik = await usluga.ImportujAsync(Od, Do, [Decyzja(drugie[0])]);

        Assert.Equal(0, wynik.Zaimportowano);
        Assert.Equal(1, wynik.Pominieto);
        Assert.Equal(1, await kontekst.FakturyZakupu.CountAsync());
    }

    [Fact]
    public async Task KwalifikacjaPochodziOdUzytkownikaANieZKsef()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        UslugaImportuZakupow usluga = Usluga(kontekst, atrapa);

        IReadOnlyList<ZnalezionaFaktura> znalezione = await usluga.SzukajAsync(Od, Do);

        await usluga.ImportujAsync(Od, Do,
            [Decyzja(Assert.Single(znalezione), RodzajZakupu.SrodkiTrwale, odliczany: false)]);

        FakturaZakupu faktura = await kontekst.FakturyZakupu.SingleAsync();
        Assert.Equal(RodzajZakupu.SrodkiTrwale, faktura.Rodzaj);
        Assert.False(faktura.Odliczany);
    }

    [Fact]
    public async Task FakturaSpozaListyZKsefJestPomijana()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        UslugaImportuZakupow usluga = Usluga(kontekst, atrapa);

        // Numer podrzucony w formularzu, którego nie było w odpowiedzi KSeF.
        // Kwoty biorą się wyłącznie z KSeF, więc bez dopasowania nie ma czego
        // zapisać - i dobrze, bo inaczej przeglądarka dyktowałaby księgowania.
        WynikImportu wynik = await usluga.ImportujAsync(Od, Do,
            [new DecyzjaImportu("PODROBIONY-NUMER", RodzajZakupu.TowaryIUslugi, true)]);

        Assert.Equal(0, wynik.Zaimportowano);
        Assert.Equal(1, wynik.Pominieto);
        Assert.Equal(0, await kontekst.FakturyZakupu.CountAsync());
    }

    [Fact]
    public async Task BrakTokenaKonczySieCzytelnymBledem()
    {
        Guid firmaId;
        await using (FirmaProDbContext zakladanie = baza.UtworzKontekst(null))
        {
            var firma = new Firma
            {
                Nazwa = "Firma bez tokena",
                Nip = "7010001453",
                AdresLinia1 = "ul. Długa 1",
                Srodowisko = SrodowiskoKsef.Test
            };

            zakladanie.Firmy.Add(firma);
            await zakladanie.SaveChangesAsync();
            firmaId = firma.Id;
        }

        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);

        BladKsefException blad = await Assert.ThrowsAsync<BladKsefException>(
            () => Usluga(kontekst, atrapa).SzukajAsync(Od, Do));

        Assert.Contains("token", blad.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OdpytanieOMetadaneNieOtwieraSesji()
    {
        Guid firmaId = await ZalozFirmeAsync();
        using var atrapa = new AtrapaKsef(oczekiwanyToken: Token);

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        await Usluga(kontekst, atrapa).SzukajAsync(Od, Do);

        // Odpytanie o metadane nie otwiera sesji interaktywnej, więc żadne
        // zamknięcie nie jest wysyłane - i nie może się przy tym wywrócić.
        Assert.DoesNotContain(atrapa.Wywolania,
            w => w.EndsWith("/close", StringComparison.Ordinal));
    }
}
