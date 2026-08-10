using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using FirmaPro.Wydruk;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Wynik próby wystawienia faktury.</summary>
public sealed record WynikWystawienia(FakturaSprzedazy? Faktura, WynikWalidacji Walidacja)
{
    public bool Udalo => Faktura is not null;
}

/// <summary>Wynik próby wysłania faktury do KSeF.</summary>
public sealed record WynikWysylki(bool Udalo, string Komunikat, string? NumerKsef);

/// <summary>
/// Wystawianie faktur i wysyłanie ich do KSeF.
/// </summary>
/// <remarks>
/// Usługa spina warstwy: bierze dane z formularza, buduje model dziedziny,
/// sprawdza go, zapisuje w bazie i - na osobne żądanie - wysyła do KSeF.
/// Strony Razor nie znają ani generatora XML, ani klienta KSeF.
/// </remarks>
public sealed class UslugaFaktur(
    FirmaProDbContext baza,
    UslugaNumeracji numeracja,
    IFabrykaKlientowKsef fabrykaKlientow,
    IOchronaTokena ochronaTokena,
    ILogger<UslugaFaktur> dziennik)
{
    /// <summary>
    /// Wystawia fakturę na podstawie danych z formularza.
    /// </summary>
    /// <remarks>
    /// Dokument zapisywany jest dopiero po pomyślnej walidacji - nie chcemy
    /// trzymać w bazie faktur, których i tak nie da się wysłać.
    /// </remarks>
    public async Task<WynikWystawienia> WystawAsync(
        Guid kontrahentId,
        DateOnly dataWystawienia,
        DateOnly? dataSprzedazy,
        DateOnly? terminPlatnosci,
        FormaPlatnosci? formaPlatnosci,
        string? podstawaZwolnienia,
        IReadOnlyList<(string Nazwa, string Jednostka, decimal Ilosc,
                       decimal CenaNetto, string KodStawki, string? Gtu)> pozycje,
        CancellationToken anulowanie = default)
    {
        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        Kontrahent? kontrahent = await baza.Kontrahenci
            .FirstOrDefaultAsync(k => k.Id == kontrahentId, anulowanie);

        if (kontrahent is null)
        {
            // Filtr firmy nie przepuścił kontrahenta - albo nie istnieje,
            // albo należy do innej firmy. Z punktu widzenia użytkownika
            // to ta sama sytuacja.
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Nabywca", "nie znaleziono wskazanego kontrahenta"));
        }

        Faktura model = ZbudujModel(firma, kontrahent, dataWystawienia, dataSprzedazy,
            terminPlatnosci, formaPlatnosci, podstawaZwolnienia, pozycje);

        // Numer nadajemy dopiero po sprawdzeniu reszty, żeby nieudana próba
        // nie zużywała kolejnych numerów z serii.
        model.Numer = "FV/ROBOCZA";
        WynikWalidacji walidacja = Walidator.SprawdzFakture(model);
        if (walidacja.SaBledy)
        {
            return new WynikWystawienia(null, walidacja);
        }

        model.Numer = await numeracja.NastepnyNumerAsync(dataWystawienia, anulowanie);

        FakturaSprzedazy encja = NaEncje(model, kontrahent);
        baza.FakturySprzedazy.Add(encja);
        await baza.SaveChangesAsync(anulowanie);

        Dziennik.WystawionoFakture(dziennik, encja.Numer, encja.RazemBrutto);

        return new WynikWystawienia(encja, walidacja);
    }

    /// <summary>
    /// Wystawia korektę do wskazanej faktury.
    /// </summary>
    /// <remarks>
    /// Faktura przyjęta przez KSeF jest niezmienna, więc korekta to jedyny
    /// sposób poprawienia błędu. Dokument niesie obie wersje pozycji - sprzed
    /// zmiany i po niej - a do rejestru VAT trafia różnica między nimi.
    /// </remarks>
    /// <param name="korygowanaId">Faktura, której dotyczy korekta.</param>
    /// <param name="typKorekty">
    /// Okres, w którym korekta ma skutek: w dacie faktury pierwotnej
    /// (błąd istniejący od początku) albo w dacie korekty (rabat, zwrot).
    /// </param>
    public async Task<WynikWystawienia> WystawKorekteAsync(
        Guid korygowanaId,
        DateOnly dataWystawienia,
        string przyczynaKorekty,
        TypKorektyVat typKorekty,
        IReadOnlyList<(string Nazwa, string Jednostka, decimal Ilosc,
                       decimal CenaNetto, string KodStawki, string? Gtu)> pozycjePoKorekcie,
        CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(pozycjePoKorekcie);

        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        FakturaSprzedazy? korygowana = await baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .FirstOrDefaultAsync(f => f.Id == korygowanaId, anulowanie);

        if (korygowana is null)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Faktura", "nie znaleziono faktury do skorygowania"));
        }

        if (korygowana.CzyKorekta)
        {
            // Korekta korekty jest dopuszczalna, ale wymaga wskazania
            // faktury pierwotnej i osobnego przemyślenia kwot - na razie
            // wolimy powiedzieć wprost, że tego nie obsługujemy, niż
            // wystawić dokument, którego nikt nie sprawdził.
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Faktura", "korygowanie faktury korygującej nie jest jeszcze obsługiwane"));
        }

        if (string.IsNullOrWhiteSpace(przyczynaKorekty))
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "PrzyczynaKorekty", "przyczyna korekty jest wymagana"));
        }

        Faktura model = ZbudujModel(firma, KontrahentZFaktury(korygowana),
            dataWystawienia, dataWystawienia, null, korygowana.FormaPlatnosci,
            korygowana.PodstawaZwolnienia, pozycjePoKorekcie);

        model.Rodzaj = RodzajFaktury.Korygujaca;
        model.PrzyczynaKorekty = przyczynaKorekty.Trim();
        model.TypKorekty = typKorekty;
        model.Korygowane =
        [
            new DaneFakturyKorygowanej(korygowana.Numer, korygowana.DataWystawienia,
                korygowana.NumerKsef)
        ];

        model.PozycjePrzedKorekta = korygowana.Pozycje
            .Where(p => !p.StanPrzed)
            .OrderBy(p => p.NrWiersza)
            .Select(p => new PozycjaFaktury
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                Stawka = StawkaVat.ZKodu(p.KodStawki),
                Gtu = p.Gtu
            }).ToList();

        model.Numer = "FK/ROBOCZA";
        WynikWalidacji walidacja = Walidator.SprawdzFakture(model);
        if (walidacja.SaBledy)
        {
            return new WynikWystawienia(null, walidacja);
        }

        model.Numer = await numeracja.NastepnyNumerAsync(dataWystawienia, anulowanie);

        FakturaSprzedazy encja = NaEncje(model, KontrahentZFaktury(korygowana));
        encja.KontrahentId = korygowana.KontrahentId;
        encja.FakturaKorygowanaId = korygowana.Id;
        encja.KorygowanaNumer = korygowana.Numer;
        encja.KorygowanaDataWystawienia = korygowana.DataWystawienia;
        encja.KorygowanaNumerKsef = korygowana.NumerKsef;
        encja.PrzyczynaKorekty = model.PrzyczynaKorekty;
        encja.TypKorekty = typKorekty;

        // Okres rejestru zależy od typu skutku korekty: błąd istniejący od
        // początku wraca do okresu faktury pierwotnej, rabat wchodzi na
        // bieżąco (art. 29a ust. 13 i 17).
        encja.DataUjeciaVat = typKorekty == TypKorektyVat.WDaciePierwotnej
            ? korygowana.DataUjeciaVat
            : dataWystawienia;

        baza.FakturySprzedazy.Add(encja);
        await baza.SaveChangesAsync(anulowanie);

        Dziennik.WystawionoFakture(dziennik, encja.Numer, encja.RazemBrutto);

        return new WynikWystawienia(encja, walidacja);
    }

    /// <summary>Odtwarza dane nabywcy zapisane na fakturze korygowanej.</summary>
    private static Kontrahent KontrahentZFaktury(FakturaSprzedazy faktura) => new()
    {
        Id = faktura.KontrahentId,
        Nazwa = faktura.NabywcaNazwa,
        Nip = faktura.NabywcaNip,
        KodKraju = faktura.NabywcaKodKraju,
        AdresLinia1 = faktura.NabywcaAdresLinia1,
        AdresLinia2 = faktura.NabywcaAdresLinia2,
        KodUe = faktura.NabywcaKodUe,
        NrVatUe = faktura.NabywcaNrVatUe
    };

    /// <summary>Wysyła zapisaną fakturę do KSeF i zapisuje wynik.</summary>
    public async Task<WynikWysylki> WyslijAsync(Guid fakturaId,
                                                CancellationToken anulowanie = default)
    {
        FakturaSprzedazy? faktura = await baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .FirstOrDefaultAsync(f => f.Id == fakturaId, anulowanie);

        if (faktura is null)
        {
            return new WynikWysylki(false, "Nie znaleziono faktury.", null);
        }

        if (faktura.Status == StatusKsef.Przyjeta)
        {
            return new WynikWysylki(false,
                $"Faktura została już przyjęta przez KSeF (numer {faktura.NumerKsef}).",
                faktura.NumerKsef);
        }

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == faktura.FirmaId, anulowanie);
        string? token = OdczytajToken(firma, ochronaTokena);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new WynikWysylki(false,
                "Brak tokena KSeF. Uzupełnij go w Ustawieniach firmy.", null);
        }

        byte[] xml = Fa3Generator.ZbudujXml(NaModel(faktura, firma));

        // Klient powstaje dla środowiska tej konkretnej firmy - jedna może
        // jeszcze testować integrację, druga wystawiać faktury produkcyjne.
        IKlientKsef klientKsef = fabrykaKlientow.Utworz(firma.Srodowisko);

        try
        {
            await klientKsef.UwierzytelnijAsync(firma.Nip, token, anulowanie);
            await klientKsef.OtworzSesjeAsync(anulowanie);

            string numerReferencyjny = await klientKsef.WyslijFaktureAsync(xml, anulowanie);
            faktura.Status = StatusKsef.Wyslana;
            faktura.SkrotXml = Kryptografia.SkrotBase64(xml);
            await baza.SaveChangesAsync(anulowanie);

            WynikWeryfikacji wynik =
                await klientKsef.PoczekajNaWynikAsync(numerReferencyjny, anulowanie);

            if (wynik.Przyjeta)
            {
                faktura.Status = StatusKsef.Przyjeta;
                faktura.NumerKsef = wynik.NumerKsef;
                faktura.DataPrzyjeciaKsef = wynik.DataPrzyjecia;
                faktura.UwagiKsef = wynik.Opis;
                await baza.SaveChangesAsync(anulowanie);

                return new WynikWysylki(true,
                    $"Faktura przyjęta. Numer KSeF: {wynik.NumerKsef}", wynik.NumerKsef);
            }

            faktura.Status = StatusKsef.Odrzucona;
            faktura.UwagiKsef = string.Join("; ", new[] { wynik.Opis }.Concat(wynik.Szczegoly));
            await baza.SaveChangesAsync(anulowanie);

            return new WynikWysylki(false, "KSeF odrzucił fakturę: " + faktura.UwagiKsef, null);
        }
        catch (BladKsefException blad)
        {
            Dziennik.NieudanaWysylka(dziennik, blad, faktura.Numer);

            // Status wraca na roboczy, żeby dało się poprawić i spróbować
            // ponownie - dokument nie trafił do systemu.
            faktura.Status = StatusKsef.Robocza;
            faktura.UwagiKsef = blad.PelnyOpis();
            await baza.SaveChangesAsync(anulowanie);

            return new WynikWysylki(false, blad.PelnyOpis(), null);
        }
        finally
        {
            try
            {
                await klientKsef.ZamknijSesjeAsync(anulowanie);
            }
            catch (BladKsefException)
            {
                // Zamknięcie sesji to sprzątanie - błąd na tym etapie nie może
                // przesłonić właściwego wyniku wysyłki.
            }
        }
    }

    /// <summary>Buduje dokument XML bez wysyłania go - do podglądu i kontroli.</summary>
    public async Task<byte[]> ZbudujXmlAsync(Guid fakturaId,
                                             CancellationToken anulowanie = default)
    {
        FakturaSprzedazy faktura = await baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .SingleAsync(f => f.Id == fakturaId, anulowanie);

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == faktura.FirmaId, anulowanie);
        return Fa3Generator.ZbudujXml(NaModel(faktura, firma));
    }

    /// <summary>
    /// Buduje wizualizację faktury w PDF - do wysłania kontrahentowi.
    /// </summary>
    /// <remarks>
    /// Kod QR trafia na wydruk wyłącznie wtedy, gdy faktura jest już w KSeF.
    /// Powstaje z zapisanego skrótu przesłanego pliku, a nie z dokumentu
    /// budowanego na nowo: XML niesie znacznik czasu wytworzenia, więc jego
    /// ponowne złożenie dałoby inny skrót i kod prowadzący donikąd.
    /// </remarks>
    public async Task<byte[]> ZbudujPdfAsync(Guid fakturaId,
                                             CancellationToken anulowanie = default)
    {
        FakturaSprzedazy faktura = await baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .SingleAsync(f => f.Id == fakturaId, anulowanie);

        Firma firma = await baza.Firmy.SingleAsync(f => f.Id == faktura.FirmaId, anulowanie);

        OpcjeWydruku opcje =
            faktura.Status == StatusKsef.Przyjeta
            && !string.IsNullOrWhiteSpace(faktura.NumerKsef)
            && !string.IsNullOrWhiteSpace(faktura.SkrotXml)
                ? OpcjeWydruku.DlaPrzyjetejZeSkrotu(
                    faktura.NumerKsef!, firma.Nip, faktura.DataWystawienia,
                    faktura.SkrotXml!, firma.Srodowisko)
                : OpcjeWydruku.DlaProjektu();

        return WydrukFaktury.Utworz(NaModel(faktura, firma), opcje);
    }

    // ------------------------------------------------------------ pomocnicze

    private static Faktura ZbudujModel(
        Firma firma, Kontrahent kontrahent,
        DateOnly dataWystawienia, DateOnly? dataSprzedazy, DateOnly? terminPlatnosci,
        FormaPlatnosci? formaPlatnosci, string? podstawaZwolnienia,
        IReadOnlyList<(string Nazwa, string Jednostka, decimal Ilosc,
                       decimal CenaNetto, string KodStawki, string? Gtu)> pozycje) =>
        new()
        {
            DataWystawienia = dataWystawienia,
            DataSprzedazy = dataSprzedazy,
            MiejsceWystawienia = firma.MiejsceWystawienia,
            Sprzedawca = NaPodmiot(firma),
            Nabywca = kontrahent.NaPodmiot(),
            Stopka = firma.StopkaFaktury,
            PodstawaZwolnienia = podstawaZwolnienia,
            Platnosc = new WarunkiPlatnosci
            {
                Forma = formaPlatnosci,
                Termin = terminPlatnosci,
                Rachunek = firma.RachunekBankowy,
                NazwaBanku = firma.NazwaBanku
            },
            Pozycje = pozycje.Select(p => new PozycjaFaktury
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                Stawka = StawkaVat.TryZKodu(p.KodStawki, out StawkaVat? stawka)
                    ? stawka
                    : StawkaVat.Vat23,
                Gtu = string.IsNullOrWhiteSpace(p.Gtu) ? null : p.Gtu
            }).ToList()
        };

    private static Podmiot NaPodmiot(Firma firma) => new()
    {
        Nazwa = firma.Nazwa,
        Nip = firma.Nip,
        Adres = new Adres
        {
            KodKraju = firma.KodKraju,
            Linia1 = firma.AdresLinia1,
            Linia2 = firma.AdresLinia2
        },
        Email = firma.Email,
        Telefon = firma.Telefon
    };

    /// <summary>Zamienia model dziedziny na encję zapisywaną w bazie.</summary>
    private static FakturaSprzedazy NaEncje(Faktura model, Kontrahent kontrahent)
    {
        PodsumowanieFaktury podsumowanie = model.Podsumowanie();

        var encja = new FakturaSprzedazy
        {
            Numer = model.Numer,
            DataWystawienia = model.DataWystawienia,
            DataSprzedazy = model.DataSprzedazy,
            // Okres rejestru VAT wynika z daty sprzedaży, nie wystawienia -
            // zapisujemy go od razu, żeby rejestr nie musiał go zgadywać.
            DataUjeciaVat = TerminyVat.DataUjeciaSprzedazy(
                model.DataWystawienia, model.DataSprzedazy),
            MiejsceWystawienia = model.MiejsceWystawienia,
            Waluta = model.Waluta,
            Rodzaj = model.Rodzaj,
            KontrahentId = kontrahent.Id,
            // Dane nabywcy przepisujemy w chwili wystawienia - późniejsza
            // zmiana w kartotece nie może zmieniać treści wystawionych faktur.
            NabywcaNazwa = model.Nabywca.Nazwa,
            NabywcaNip = model.Nabywca.Nip,
            NabywcaKodKraju = model.Nabywca.Adres.KodKraju,
            NabywcaAdresLinia1 = model.Nabywca.Adres.Linia1,
            NabywcaAdresLinia2 = model.Nabywca.Adres.Linia2,
            NabywcaKodUe = model.Nabywca.KodUe,
            NabywcaNrVatUe = model.Nabywca.NrVatUe,
            NabywcaJst = model.Nabywca.JednostkaPodrzednaJst,
            NabywcaGrupaVat = model.Nabywca.CzlonekGrupyVat,
            FormaPlatnosci = model.Platnosc.Forma,
            TerminPlatnosci = model.Platnosc.Termin,
            RachunekBankowy = model.Platnosc.Rachunek,
            NazwaBanku = model.Platnosc.NazwaBanku,
            Stopka = model.Stopka,
            PodstawaZwolnienia = model.PodstawaZwolnienia,
            RazemNetto = podsumowanie.RazemNetto,
            RazemVat = podsumowanie.RazemVat,
            RazemBrutto = podsumowanie.RazemBrutto
        };

        int numerWiersza = 1;
        foreach (PozycjaFaktury pozycja in model.PozycjePrzedKorekta)
        {
            encja.Pozycje.Add(new PozycjaFakturySprzedazy
            {
                NrWiersza = numerWiersza++,
                StanPrzed = true,
                Nazwa = pozycja.Nazwa,
                Jednostka = pozycja.Jednostka,
                Ilosc = pozycja.Ilosc,
                CenaNetto = pozycja.CenaNetto,
                KodStawki = pozycja.Stawka.Kod,
                Gtu = pozycja.Gtu,
                WartoscNetto = pozycja.WartoscNetto,
                KwotaVat = pozycja.KwotaVat
            });
        }

        foreach (PozycjaFaktury pozycja in model.Pozycje)
        {
            encja.Pozycje.Add(new PozycjaFakturySprzedazy
            {
                NrWiersza = numerWiersza++,
                Nazwa = pozycja.Nazwa,
                Jednostka = pozycja.Jednostka,
                Ilosc = pozycja.Ilosc,
                CenaNetto = pozycja.CenaNetto,
                KodStawki = pozycja.Stawka.Kod,
                Gtu = pozycja.Gtu,
                WartoscNetto = pozycja.WartoscNetto,
                KwotaVat = pozycja.KwotaVat
            });
        }

        return encja;
    }

    /// <summary>Odtwarza model dziedziny z zapisanej encji.</summary>
    private static Faktura NaModel(FakturaSprzedazy encja, Firma firma) => new()
    {
        Numer = encja.Numer,
        DataWystawienia = encja.DataWystawienia,
        DataSprzedazy = encja.DataSprzedazy,
        MiejsceWystawienia = encja.MiejsceWystawienia,
        Waluta = encja.Waluta,
        Rodzaj = encja.Rodzaj,
        Sprzedawca = NaPodmiot(firma),
        Nabywca = new Podmiot
        {
            Nazwa = encja.NabywcaNazwa,
            Nip = encja.NabywcaNip,
            Adres = new Adres
            {
                KodKraju = encja.NabywcaKodKraju,
                Linia1 = encja.NabywcaAdresLinia1,
                Linia2 = encja.NabywcaAdresLinia2
            },
            KodUe = encja.NabywcaKodUe,
            NrVatUe = encja.NabywcaNrVatUe,
            JednostkaPodrzednaJst = encja.NabywcaJst,
            CzlonekGrupyVat = encja.NabywcaGrupaVat
        },
        Platnosc = new WarunkiPlatnosci
        {
            Forma = encja.FormaPlatnosci,
            Termin = encja.TerminPlatnosci,
            Rachunek = encja.RachunekBankowy,
            NazwaBanku = encja.NazwaBanku,
            Zaplacono = encja.Zaplacono,
            DataZaplaty = encja.DataZaplaty
        },
        Stopka = encja.Stopka,
        PodstawaZwolnienia = encja.PodstawaZwolnienia,
        NumerKsef = encja.NumerKsef,
        PrzyczynaKorekty = encja.PrzyczynaKorekty,
        TypKorekty = encja.TypKorekty,
        Korygowane = encja.KorygowanaNumer is null || encja.KorygowanaDataWystawienia is null
            ? []
            : [new DaneFakturyKorygowanej(encja.KorygowanaNumer,
                encja.KorygowanaDataWystawienia.Value, encja.KorygowanaNumerKsef)],
        PozycjePrzedKorekta = encja.Pozycje
            .Where(p => p.StanPrzed)
            .OrderBy(p => p.NrWiersza)
            .Select(p => new PozycjaFaktury
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                Stawka = StawkaVat.ZKodu(p.KodStawki),
                Gtu = p.Gtu,
                Pkwiu = p.Pkwiu,
                Cn = p.Cn,
                Indeks = p.Indeks
            }).ToList(),
        Pozycje = encja.Pozycje
            .Where(p => !p.StanPrzed)
            .OrderBy(p => p.NrWiersza)
            .Select(p => new PozycjaFaktury
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                Stawka = StawkaVat.ZKodu(p.KodStawki),
                Gtu = p.Gtu,
                Pkwiu = p.Pkwiu,
                Cn = p.Cn,
                Indeks = p.Indeks
            }).ToList()
    };

    /// <summary>
    /// Odczytuje token KSeF firmy.
    /// </summary>
    /// <remarks>
    /// Na razie token trzymany jest w postaci zaszyfrowanej kluczem aplikacji;
    /// docelowo trafi do magazynu sekretów. Zmienna środowiskowa ma
    /// pierwszeństwo, żeby dało się pracować bez zapisywania sekretu w bazie.
    /// </remarks>
    private static string? OdczytajToken(Firma firma, IOchronaTokena ochrona)
    {
        string? zeSrodowiska = Environment.GetEnvironmentVariable("KSEF_TOKEN");
        if (!string.IsNullOrWhiteSpace(zeSrodowiska))
        {
            return zeSrodowiska;
        }

        return firma.TokenKsefZaszyfrowany is { Length: > 0 } zaszyfrowany
            ? ochrona.Odszyfruj(zaszyfrowany)
            : null;
    }
}
