using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Izolacja danych między firmami.
/// </summary>
/// <remarks>
/// Najważniejszy zestaw testów w warstwie danych. Wyciek dokumentów jednej
/// firmy do drugiej byłby w systemie księgowym katastrofą - i to taką, której
/// nie da się cofnąć.
/// </remarks>
[Collection(KolekcjaBazy.Nazwa)]
public sealed class TestyWielofirmowosci(BazaTestowa baza)
{
    private async Task<(Guid pierwsza, Guid druga)> DwieFirmy()
    {
        await using FirmaProDbContext kontekst = baza.UtworzKontekst(null);

        var pierwsza = new Firma
        {
            Nazwa = "Alfa sp. z o.o.",
            Nip = "5252248481",
            AdresLinia1 = "ul. Prosta 51"
        };
        var druga = new Firma
        {
            Nazwa = "Beta S.A.",
            Nip = "7010001453",
            AdresLinia1 = "ul. Długa 1"
        };

        kontekst.Firmy.AddRange(pierwsza, druga);
        await kontekst.SaveChangesAsync();

        return (pierwsza.Id, druga.Id);
    }

    private static Kontrahent NowyKontrahent(string nazwa) => new()
    {
        Nazwa = nazwa,
        Nip = "1180000001",
        AdresLinia1 = "ul. Testowa 1"
    };

    [Fact]
    public async Task KazdaFirmaWidziTylkoSwoichKontrahentow()
    {
        (Guid pierwsza, Guid druga) = await DwieFirmy();

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            kontekst.Kontrahenci.Add(NowyKontrahent("Klient firmy Alfa"));
            await kontekst.SaveChangesAsync();
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(druga))
        {
            kontekst.Kontrahenci.Add(NowyKontrahent("Klient firmy Beta"));
            await kontekst.SaveChangesAsync();
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            List<Kontrahent> widoczni = await kontekst.Kontrahenci.ToListAsync();

            Assert.Single(widoczni);
            Assert.Equal("Klient firmy Alfa", widoczni[0].Nazwa);
        }
    }

    [Fact]
    public async Task BezWybranejFirmyNieWidacNiczego()
    {
        (Guid pierwsza, _) = await DwieFirmy();

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            kontekst.Kontrahenci.Add(NowyKontrahent("Klient"));
            await kontekst.SaveChangesAsync();
        }

        // Brak informacji o firmie ma oznaczać "nic nie widać", a nie
        // "widać wszystko" - filtr działa domyślnie na korzyść bezpieczeństwa.
        await using FirmaProDbContext bezFirmy = baza.UtworzKontekst(null);
        Assert.Empty(await bezFirmy.Kontrahenci.ToListAsync());
    }

    [Fact]
    public async Task IdentyfikatorFirmyUstawiaSieSam()
    {
        (Guid pierwsza, _) = await DwieFirmy();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);
        var kontrahent = NowyKontrahent("Klient bez podanej firmy");
        kontekst.Kontrahenci.Add(kontrahent);
        await kontekst.SaveChangesAsync();

        // Nie trzeba pamiętać o ustawieniu FirmaId - gdyby trzeba było,
        // prędzej czy później ktoś by zapomniał.
        Assert.Equal(pierwsza, kontrahent.FirmaId);
    }

    [Fact]
    public async Task ZapisDoObcejFirmyJestOdrzucany()
    {
        (Guid pierwsza, Guid druga) = await DwieFirmy();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza);
        Kontrahent kontrahent = NowyKontrahent("Podszywacz");
        kontrahent.FirmaId = druga;
        kontekst.Kontrahenci.Add(kontrahent);

        await Assert.ThrowsAsync<BladIzolacjiFirmException>(
            () => kontekst.SaveChangesAsync());
    }

    [Fact]
    public async Task NieDaSiePrzeniescRekorduDoInnejFirmy()
    {
        (Guid pierwsza, Guid druga) = await DwieFirmy();

        Guid kontrahentId;
        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            Kontrahent kontrahent = NowyKontrahent("Klient firmy Alfa");
            kontekst.Kontrahenci.Add(kontrahent);
            await kontekst.SaveChangesAsync();
            kontrahentId = kontrahent.Id;
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            Kontrahent kontrahent = await kontekst.Kontrahenci.SingleAsync(k => k.Id == kontrahentId);
            kontrahent.FirmaId = druga;

            await Assert.ThrowsAsync<BladIzolacjiFirmException>(
                () => kontekst.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ObcaFirmaNieZnajdzieRekorduPoIdentyfikatorze()
    {
        (Guid pierwsza, Guid druga) = await DwieFirmy();

        Guid kontrahentId;
        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(pierwsza))
        {
            Kontrahent kontrahent = NowyKontrahent("Klient firmy Alfa");
            kontekst.Kontrahenci.Add(kontrahent);
            await kontekst.SaveChangesAsync();
            kontrahentId = kontrahent.Id;
        }

        // Nawet znając identyfikator, druga firma nie zobaczy rekordu -
        // filtr działa również przy wyszukiwaniu po kluczu.
        await using FirmaProDbContext obca = baza.UtworzKontekst(druga);
        Assert.Null(await obca.Kontrahenci.FirstOrDefaultAsync(k => k.Id == kontrahentId));
    }
}

/// <summary>Reguły zapisu faktur sprzedaży.</summary>
[Collection(KolekcjaBazy.Nazwa)]
public sealed class TestyFakturWBazie(BazaTestowa baza)
{
    private async Task<(Guid firmaId, Guid kontrahentId)> PrzygotujFirme()
    {
        var firma = new Firma
        {
            Nazwa = "Alfa sp. z o.o.",
            Nip = "5252248481",
            AdresLinia1 = "ul. Prosta 51"
        };

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(null))
        {
            kontekst.Firmy.Add(firma);
            await kontekst.SaveChangesAsync();
        }

        var kontrahent = new Kontrahent
        {
            Nazwa = "Klient S.A.",
            Nip = "7010001453",
            AdresLinia1 = "ul. Długa 1"
        };

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firma.Id))
        {
            kontekst.Kontrahenci.Add(kontrahent);
            await kontekst.SaveChangesAsync();
        }

        return (firma.Id, kontrahent.Id);
    }

    private static FakturaSprzedazy NowaFaktura(Guid kontrahentId, string numer) => new()
    {
        Numer = numer,
        DataWystawienia = new DateOnly(2026, 8, 8),
        KontrahentId = kontrahentId,
        NabywcaNazwa = "Klient S.A.",
        NabywcaNip = "7010001453",
        NabywcaAdresLinia1 = "ul. Długa 1",
        RazemNetto = 1500.00m,
        RazemVat = 345.00m,
        RazemBrutto = 1845.00m,
        Pozycje =
        [
            new PozycjaFakturySprzedazy
            {
                NrWiersza = 1,
                Nazwa = "Usługa programistyczna",
                Jednostka = "godz.",
                Ilosc = 10m,
                CenaNetto = 150.00m,
                KodStawki = StawkaVat.Vat23.Kod,
                WartoscNetto = 1500.00m,
                KwotaVat = 345.00m
            }
        ]
    };

    [Fact]
    public async Task NumerFakturyJestNiepowtarzalnyWObrebieFirmy()
    {
        (Guid firmaId, Guid kontrahentId) = await PrzygotujFirme();

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            kontekst.FakturySprzedazy.Add(NowaFaktura(kontrahentId, "FV/2026/08/1"));
            await kontekst.SaveChangesAsync();
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            kontekst.FakturySprzedazy.Add(NowaFaktura(kontrahentId, "FV/2026/08/1"));

            // Indeks unikalny w bazie to ostateczna gwarancja - działa nawet
            // gdy dwie osoby wystawiają fakturę w tej samej chwili.
            await Assert.ThrowsAsync<DbUpdateException>(() => kontekst.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task KwotyZachowujaDokladnoscDoGrosza()
    {
        (Guid firmaId, Guid kontrahentId) = await PrzygotujFirme();

        Guid fakturaId;
        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = NowaFaktura(kontrahentId, "FV/2026/08/2");
            faktura.RazemNetto = 0.30m;
            faktura.RazemVat = 0.07m;
            faktura.RazemBrutto = 0.37m;
            faktura.Pozycje.First().CenaNetto = 133.33333333m;
            kontekst.FakturySprzedazy.Add(faktura);
            await kontekst.SaveChangesAsync();
            fakturaId = faktura.Id;
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy odczytana = await kontekst.FakturySprzedazy
                .Include(f => f.Pozycje)
                .SingleAsync(f => f.Id == fakturaId);

            // Kolumny są typu numeric, więc wartości wracają bez zaokrągleń
            // charakterystycznych dla liczb zmiennoprzecinkowych.
            Assert.Equal(0.30m, odczytana.RazemNetto);
            Assert.Equal(0.07m, odczytana.RazemVat);
            Assert.Equal(133.33333333m, odczytana.Pozycje.First().CenaNetto);
        }
    }

    [Fact]
    public async Task FakturaPrzyjetaPrzezKsefNieDaSieZmienic()
    {
        (Guid firmaId, Guid kontrahentId) = await PrzygotujFirme();

        Guid fakturaId;
        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = NowaFaktura(kontrahentId, "FV/2026/08/3");
            faktura.Status = StatusKsef.Przyjeta;
            faktura.NumerKsef = "5252248481-20260808-010080DD2B5E-26";
            kontekst.FakturySprzedazy.Add(faktura);
            await kontekst.SaveChangesAsync();
            fakturaId = faktura.Id;
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = await kontekst.FakturySprzedazy.SingleAsync(f => f.Id == fakturaId);
            faktura.RazemBrutto = 999.99m;

            DokumentZamknietyException blad =
                await Assert.ThrowsAsync<DokumentZamknietyException>(
                    () => kontekst.SaveChangesAsync());

            // Komunikat ma tłumaczyć, co zrobić zamiast tego.
            Assert.Contains("korygując", blad.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task WyslanaFakturaMozeOdnotowacWynikZKsefIZaplate()
    {
        (Guid firmaId, Guid kontrahentId) = await PrzygotujFirme();

        Guid fakturaId;
        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = NowaFaktura(kontrahentId, "FV/2026/08/4");
            faktura.Status = StatusKsef.Wyslana;
            kontekst.FakturySprzedazy.Add(faktura);
            await kontekst.SaveChangesAsync();
            fakturaId = faktura.Id;
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = await kontekst.FakturySprzedazy.SingleAsync(f => f.Id == fakturaId);
            faktura.Status = StatusKsef.Przyjeta;
            faktura.NumerKsef = "5252248481-20260808-010080DD2B5E-26";
            faktura.Zaplacono = true;
            faktura.DataZaplaty = new DateOnly(2026, 8, 20);

            // Obieg dokumentu i rozliczenie płatności to nie zmiana treści
            // faktury - te pola muszą pozostać zapisywalne.
            await kontekst.SaveChangesAsync();
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = await kontekst.FakturySprzedazy.SingleAsync(f => f.Id == fakturaId);
            Assert.Equal(StatusKsef.Przyjeta, faktura.Status);
            Assert.True(faktura.Zaplacono);
        }
    }

    [Fact]
    public async Task FakturaRoboczaDaSieSwobodniePoprawiac()
    {
        (Guid firmaId, Guid kontrahentId) = await PrzygotujFirme();

        Guid fakturaId;
        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = NowaFaktura(kontrahentId, "FV/2026/08/5");
            kontekst.FakturySprzedazy.Add(faktura);
            await kontekst.SaveChangesAsync();
            fakturaId = faktura.Id;
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            FakturaSprzedazy faktura = await kontekst.FakturySprzedazy.SingleAsync(f => f.Id == fakturaId);
            faktura.RazemBrutto = 2000.00m;
            await kontekst.SaveChangesAsync();
        }

        await using (FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId))
        {
            Assert.Equal(2000.00m,
                (await kontekst.FakturySprzedazy.SingleAsync(f => f.Id == fakturaId)).RazemBrutto);
        }
    }

    [Fact]
    public async Task ZapisUstawiaZnacznikiCzasu()
    {
        (Guid firmaId, Guid kontrahentId) = await PrzygotujFirme();

        await using FirmaProDbContext kontekst = baza.UtworzKontekst(firmaId);
        FakturaSprzedazy faktura = NowaFaktura(kontrahentId, "FV/2026/08/6");
        kontekst.FakturySprzedazy.Add(faktura);
        await kontekst.SaveChangesAsync();

        Assert.NotEqual(default, faktura.UtworzonoUtc);
        Assert.Null(faktura.ZmienionoUtc);
    }
}
