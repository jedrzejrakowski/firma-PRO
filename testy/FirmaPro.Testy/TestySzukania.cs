using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Web.Uslugi;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Szukanie po całym programie.
/// </summary>
/// <remarks>
/// Wyszukiwarka sięga jednocześnie do trzech tabel, więc jest najkrótszą
/// drogą do wycieku dokumentów między firmami - stąd osobny test izolacji
/// obok testów samego dopasowania.
/// </remarks>
[Collection(KolekcjaBazy.Nazwa)]
public sealed class TestySzukania(BazaTestowa baza)
{
    private async Task<(Guid pierwsza, Guid druga)> DwieFirmyZDokumentami()
    {
        Guid pierwsza;
        Guid druga;

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(null))
        {
            var alfa = new Firma
            {
                Nazwa = "Alfa sp. z o.o.",
                Nip = "5252248481",
                AdresLinia1 = "ul. Prosta 51"
            };
            var beta = new Firma
            {
                Nazwa = "Beta S.A.",
                Nip = "7010001453",
                AdresLinia1 = "ul. Długa 1"
            };

            kontekst.Firmy.AddRange(alfa, beta);
            await kontekst.SaveChangesAsync();

            pierwsza = alfa.Id;
            druga = beta.Id;
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            var nabywca = new Kontrahent
            {
                Nazwa = "Auto-Serwis Nowak sp. j.",
                Nip = "1180000001",
                AdresLinia1 = "ul. Puławska 120"
            };

            kontekst.Kontrahenci.Add(nabywca);
            await kontekst.SaveChangesAsync();

            kontekst.FakturySprzedazy.Add(NowaFaktura(
                "FV/2026/08/7", nabywca, "1180000001-20260812-0AAAAA-BBBBBB-CC"));

            kontekst.FakturyZakupu.Add(new FakturaZakupu
            {
                Numer = "ZAK/2026/9",
                SprzedawcaNazwa = "Dostawca Prądu S.A.",
                SprzedawcaNip = "7010001453",
                DataWystawienia = new DateOnly(2026, 8, 3),
                DataWplywu = new DateOnly(2026, 8, 5),
                DataObowiazkuPodatkowego = new DateOnly(2026, 8, 3),
                DataUjecia = new DateOnly(2026, 8, 1),
                RazemNetto = 1000m,
                RazemVat = 230m,
                RazemBrutto = 1230m
            });

            await kontekst.SaveChangesAsync();
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(druga))
        {
            var nabywca = new Kontrahent
            {
                Nazwa = "Klient firmy Beta",
                Nip = "5252248481",
                AdresLinia1 = "ul. Inna 2"
            };

            kontekst.Kontrahenci.Add(nabywca);
            await kontekst.SaveChangesAsync();

            kontekst.FakturySprzedazy.Add(NowaFaktura("FV/2026/08/7", nabywca, null));
            await kontekst.SaveChangesAsync();
        }

        return (pierwsza, druga);
    }

    private static FakturaSprzedazy NowaFaktura(
        string numer, Kontrahent nabywca, string? numerKsef) => new()
        {
            Numer = numer,
            DataWystawienia = new DateOnly(2026, 8, 12),
            DataUjeciaVat = new DateOnly(2026, 8, 12),
            KontrahentId = nabywca.Id,
            NabywcaNazwa = nabywca.Nazwa,
            NabywcaNip = nabywca.Nip,
            NabywcaAdresLinia1 = nabywca.AdresLinia1,
            RazemNetto = 1000m,
            RazemVat = 230m,
            RazemBrutto = 1230m,
            NumerKsef = numerKsef
        };

    private static UslugaSzukania Usluga(FirmaProDbContext kontekst) => new(kontekst);

    [Fact]
    public async Task ZnajdujeFaktureFragmentemNumeru()
    {
        (Guid pierwsza, _) = await DwieFirmyZDokumentami();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);
        WynikiSzukania wyniki = await Usluga(kontekst).SzukajAsync("08/7");

        Assert.Equal("FV/2026/08/7", Assert.Single(wyniki.Sprzedaz).Tytul);
    }

    /// <summary>
    /// Numer KSeF przepisuje się z potwierdzenia, więc trzeba go umieć znaleźć.
    /// </summary>
    [Fact]
    public async Task ZnajdujeFaktureNumeremKsef()
    {
        (Guid pierwsza, _) = await DwieFirmyZDokumentami();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);
        WynikiSzukania wyniki = await Usluga(kontekst).SzukajAsync("0AAAAA-BBBBBB");

        Assert.Equal("FV/2026/08/7", Assert.Single(wyniki.Sprzedaz).Tytul);
    }

    /// <summary>
    /// NIP bywa przepisywany z kreskami - i wtedy też ma zadziałać.
    /// </summary>
    [Fact]
    public async Task ZnajdujeKontrahentaPoNipieZKreskami()
    {
        (Guid pierwsza, _) = await DwieFirmyZDokumentami();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);
        WynikiSzukania wyniki = await Usluga(kontekst).SzukajAsync("118-000-00-01");

        Assert.Equal("Auto-Serwis Nowak sp. j.",
            Assert.Single(wyniki.Kontrahenci).Tytul);
    }

    [Fact]
    public async Task ZnajdujeFaktureKosztowaPoNazwieDostawcy()
    {
        (Guid pierwsza, _) = await DwieFirmyZDokumentami();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);
        WynikiSzukania wyniki = await Usluga(kontekst).SzukajAsync("dostawca prądu");

        Wynik znaleziona = Assert.Single(wyniki.Zakupy);
        Assert.Equal("ZAK/2026/9", znaleziona.Tytul);
        Assert.Equal(RodzajWyniku.FakturaZakupu, znaleziona.Rodzaj);
    }

    /// <summary>Wielkość liter nie może decydować o tym, czy coś się znajdzie.</summary>
    [Fact]
    public async Task NieRozrozniaWielkosciLiter()
    {
        (Guid pierwsza, _) = await DwieFirmyZDokumentami();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);
        WynikiSzukania wyniki = await Usluga(kontekst).SzukajAsync("AUTO-serwis");

        Assert.NotEmpty(wyniki.Kontrahenci);
    }

    /// <summary>
    /// Jedna litera pasuje do wszystkiego - takiego pytania nie zadajemy bazie.
    /// </summary>
    [Fact]
    public async Task ZaKrotkieSzukanieNiczegoNieZwraca()
    {
        (Guid pierwsza, _) = await DwieFirmyZDokumentami();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);

        Assert.Equal(0, (await Usluga(kontekst).SzukajAsync("F")).Ile);
        Assert.Equal(0, (await Usluga(kontekst).SzukajAsync("  ")).Ile);
        Assert.Equal(0, (await Usluga(kontekst).SzukajAsync(null)).Ile);
    }

    /// <summary>
    /// Najważniejszy test w tym pliku: szukanie nie może pokazać dokumentu
    /// innej firmy, nawet gdy numer jest identyczny.
    /// </summary>
    [Fact]
    public async Task NieWidacDokumentowInnejFirmy()
    {
        (Guid pierwsza, Guid druga) = await DwieFirmyZDokumentami();

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            WynikiSzukania wyniki = await Usluga(kontekst).SzukajAsync("FV/2026/08/7");

            Assert.Equal("Auto-Serwis Nowak sp. j.",
                Assert.Single(wyniki.Sprzedaz).Opis.Split(" · ")[0]);
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(druga))
        {
            WynikiSzukania wyniki = await Usluga(kontekst).SzukajAsync("FV/2026/08/7");

            Assert.Equal("Klient firmy Beta",
                Assert.Single(wyniki.Sprzedaz).Opis.Split(" · ")[0]);

            // Kontrahent i faktura kosztowa należą do pierwszej firmy.
            Assert.Empty(wyniki.Kontrahenci);
            Assert.Empty(wyniki.Zakupy);
        }
    }

    /// <summary>Liczba trafień w grupie jest ograniczona.</summary>
    [Fact]
    public async Task LiczbaTrafienJestPrzycieta()
    {
        (Guid pierwsza, _) = await DwieFirmyZDokumentami();

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            Kontrahent nabywca = await kontekst.Kontrahenci.FirstAsync();

            for (int numer = 100; numer < 110; numer++)
            {
                kontekst.FakturySprzedazy.Add(
                    NowaFaktura($"FV/2026/08/{numer}", nabywca, null));
            }

            await kontekst.SaveChangesAsync();
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            WynikiSzukania wyniki = await Usluga(kontekst)
                .SzukajAsync("FV/2026/08", ileNaGrupe: 3);

            Assert.Equal(3, wyniki.Sprzedaz.Count);
        }
    }
}
