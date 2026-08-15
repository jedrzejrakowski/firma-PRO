using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using FirmaPro.Wydruk;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
    /// <param name="przedZapisem">
    /// Ostatnie poprawki na gotowej encji, wykonywane jeszcze przed zapisem.
    /// Faktura końcowa dopisuje tu rozliczane zaliczki, żeby cały dokument
    /// powstał jednym zapisem - dopisywanie dzieci do faktury już zapisanej
    /// EF Core uznałby za zmianę istniejących rekordów, bo klucze nadajemy
    /// sami i nie są puste.
    /// </param>
    public async Task<WynikWystawienia> WystawAsync(
        Guid kontrahentId,
        DateOnly dataWystawienia,
        DateOnly? dataSprzedazy,
        DateOnly? terminPlatnosci,
        FormaPlatnosci? formaPlatnosci,
        string? podstawaZwolnienia,
        IReadOnlyList<(string Nazwa, string Jednostka, decimal Ilosc,
                       decimal CenaNetto, string KodStawki, string? Gtu)> pozycje,
        Action<FakturaSprzedazy>? przedZapisem = null,
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
        przedZapisem?.Invoke(encja);

        baza.FakturySprzedazy.Add(encja);
        await baza.SaveChangesAsync(anulowanie);

        Dziennik.WystawionoFakture(dziennik, encja.Numer, encja.RazemBrutto);

        return new WynikWystawienia(encja, walidacja);
    }

    /// <summary>
    /// Wystawia fakturę zaliczkową na otrzymaną wpłatę.
    /// </summary>
    /// <remarks>
    /// Faktura zaliczkowa dokumentuje pieniądze, a nie towar: jej wiersze to
    /// rozbicie wpłaty na stawki podatku, a to, czego wpłata dotyczy, opisuje
    /// zamówienie (art. 106f ust. 1 pkt 4 ustawy). Podatek liczony jest
    /// „w stu" - zaliczka jest kwotą brutto.
    /// </remarks>
    public async Task<WynikWystawienia> WystawZaliczkowaAsync(
        Guid kontrahentId,
        DateOnly dataWystawienia,
        decimal kwotaZaliczki,
        FormaPlatnosci? formaPlatnosci,
        IReadOnlyList<(string Nazwa, string Jednostka, decimal Ilosc,
                       decimal CenaNetto, string KodStawki, string? Gtu)> zamowienie,
        CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(zamowienie);

        Firma firma = await baza.Firmy
            .SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

        Kontrahent? kontrahent = await baza.Kontrahenci
            .FirstOrDefaultAsync(k => k.Id == kontrahentId, anulowanie);

        if (kontrahent is null)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Nabywca", "nie znaleziono wskazanego kontrahenta"));
        }

        List<PozycjaZamowienia> pozycjeZamowienia = [.. zamowienie.Select(p => new PozycjaZamowienia
        {
            Nazwa = p.Nazwa?.Trim() ?? string.Empty,
            Jednostka = string.IsNullOrWhiteSpace(p.Jednostka) ? "szt." : p.Jednostka.Trim(),
            Ilosc = p.Ilosc,
            CenaNetto = p.CenaNetto,
            Stawka = StawkaVat.TryZKodu(p.KodStawki, out StawkaVat? stawka)
                ? stawka
                : StawkaVat.Vat23,
            Gtu = string.IsNullOrWhiteSpace(p.Gtu) ? null : p.Gtu
        })];

        decimal wartoscZamowienia = Zaliczka.WartoscZamowienia(pozycjeZamowienia);

        if (pozycjeZamowienia.Count == 0 || wartoscZamowienia <= 0)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Zamowienie", "zamówienie musi mieć przynajmniej jedną pozycję o wartości większej od zera"));
        }

        if (kwotaZaliczki <= 0)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "KwotaZaliczki", "kwota zaliczki musi być większa od zera"));
        }

        // Zaliczka wyższa od zamówienia to zwykle pomyłka w kwocie albo
        // w zamówieniu - a wystawiona, zawyżyłaby podstawę opodatkowania.
        if (kwotaZaliczki > wartoscZamowienia)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "KwotaZaliczki",
                $"zaliczka {Kwoty.NaTekst(kwotaZaliczki)} jest wyższa niż wartość zamówienia " +
                $"{Kwoty.NaTekst(wartoscZamowienia)}"));
        }

        IReadOnlyList<CzescZaliczki> czesci = Zaliczka.Rozbij(pozycjeZamowienia, kwotaZaliczki);

        Faktura model = ZbudujModel(firma, kontrahent, dataWystawienia, dataWystawienia,
            dataWystawienia, formaPlatnosci, null,
            [.. czesci.Select(c => (
                Nazwa: $"Zaliczka na poczet zamówienia - stawka {c.Stawka.Opis}",
                Jednostka: "usł.",
                Ilosc: 1m,
                CenaNetto: c.Netto,
                KodStawki: c.Stawka.Kod,
                Gtu: (string?)null))]);

        model.Rodzaj = RodzajFaktury.Zaliczkowa;
        model.Zamowienie = pozycjeZamowienia;

        // Zaliczka z definicji jest już zapłacona - to ona jest powodem
        // wystawienia dokumentu.
        model.Platnosc.Zaplacono = true;
        model.Platnosc.DataZaplaty = dataWystawienia;

        model.Numer = "FZ/ROBOCZA";
        WynikWalidacji walidacja = Walidator.SprawdzFakture(model);
        if (walidacja.SaBledy)
        {
            return new WynikWystawienia(null, walidacja);
        }

        model.Numer = await numeracja.NastepnyNumerAsync(dataWystawienia, anulowanie);

        FakturaSprzedazy encja = NaEncje(model, kontrahent);
        encja.Zaplacono = true;
        encja.DataZaplaty = dataWystawienia;

        // Zaliczka to pieniądze, które już wpłynęły - musi więc mieć wpłatę,
        // a nie sam znacznik „zapłacona". Inaczej ekran należności liczyłby
        // z wpłat, znacznik brałby się znikąd i oba pokazywałyby co innego.
        baza.Platnosci.Add(new Platnosc
        {
            FakturaId = encja.Id,
            Kwota = encja.RazemBrutto,
            Data = dataWystawienia,
            Uwagi = "Otrzymana zaliczka"
        });

        int nr = 1;
        foreach (PozycjaZamowienia pozycja in pozycjeZamowienia)
        {
            encja.PozycjeZamowienia.Add(new PozycjaZamowieniaFaktury
            {
                NrWiersza = nr++,
                Nazwa = pozycja.Nazwa,
                Jednostka = pozycja.Jednostka,
                Ilosc = pozycja.Ilosc,
                CenaNetto = pozycja.CenaNetto,
                KodStawki = pozycja.Stawka.Kod,
                Gtu = pozycja.Gtu
            });
        }

        baza.FakturySprzedazy.Add(encja);
        await baza.SaveChangesAsync(anulowanie);

        Dziennik.WystawionoFakture(dziennik, encja.Numer, encja.RazemBrutto);

        return new WynikWystawienia(encja, walidacja);
    }

    /// <summary>
    /// Wystawia fakturę końcową rozliczającą wskazane zaliczki.
    /// </summary>
    /// <remarks>
    /// Faktura końcowa obejmuje całą dostawę, ale wykazuje ją pomniejszoną
    /// o zafakturowane wcześniej zaliczki (art. 106f ust. 3 ustawy) - inaczej
    /// ta sama kwota trafiłaby do podstawy opodatkowania dwa razy.
    /// </remarks>
    public async Task<WynikWystawienia> WystawKoncowaAsync(
        Guid kontrahentId,
        DateOnly dataWystawienia,
        DateOnly? terminPlatnosci,
        FormaPlatnosci? formaPlatnosci,
        IReadOnlyList<Guid> zaliczki,
        IReadOnlyList<(string Nazwa, string Jednostka, decimal Ilosc,
                       decimal CenaNetto, string KodStawki, string? Gtu)> pozycje,
        CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(zaliczki);

        if (zaliczki.Count == 0)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Zaliczki", "wskaż przynajmniej jedną fakturę zaliczkową"));
        }

        List<FakturaSprzedazy> zaliczkowe = await baza.FakturySprzedazy
            .Where(f => zaliczki.Contains(f.Id) && f.Rodzaj == RodzajFaktury.Zaliczkowa)
            .ToListAsync(anulowanie);

        if (zaliczkowe.Count != zaliczki.Count)
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Zaliczki", "nie znaleziono wszystkich wskazanych faktur zaliczkowych"));
        }

        if (zaliczkowe.Any(f => f.KontrahentId != kontrahentId))
        {
            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Zaliczki", "faktura końcowa musi dotyczyć tego samego nabywcy co zaliczki"));
        }

        HashSet<Guid> juzRozliczone = (await baza.RozliczoneZaliczki
                .Where(z => zaliczki.Contains(z.ZaliczkowaId))
                .Select(z => z.ZaliczkowaId)
                .ToListAsync(anulowanie))
            .ToHashSet();

        if (juzRozliczone.Count > 0)
        {
            string numery = string.Join(", ", zaliczkowe
                .Where(f => juzRozliczone.Contains(f.Id))
                .Select(f => f.Numer));

            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Zaliczki", $"te zaliczki są już rozliczone fakturą końcową: {numery}"));
        }

        decimal sumaZaliczek = Kwoty.Zaokraglij(zaliczkowe.Sum(f => f.RazemBrutto));

        try
        {
            return await WystawAsync(kontrahentId, dataWystawienia,
                dataWystawienia, terminPlatnosci, formaPlatnosci, null, pozycje,
                koncowa =>
                {
                    koncowa.Rodzaj = RodzajFaktury.Rozliczeniowa;

                    foreach (FakturaSprzedazy zaliczkowa in zaliczkowe)
                    {
                        koncowa.RozliczoneZaliczki.Add(new RozliczonaZaliczka
                        {
                            ZaliczkowaId = zaliczkowa.Id,
                            Numer = zaliczkowa.Numer,
                            DataWystawienia = zaliczkowa.DataWystawienia,
                            NumerKsef = zaliczkowa.NumerKsef,
                            Brutto = zaliczkowa.RazemBrutto
                        });
                    }

                    // Faktura końcowa obejmuje całą dostawę, ale za część
                    // nabywca już zapłacił. Zaliczka wchodzi więc jako wpłata,
                    // żeby „pozostaje do zapłaty" pokazywało prawdziwą różnicę,
                    // a nie całość drugi raz.
                    baza.Platnosci.Add(new Platnosc
                    {
                        FakturaId = koncowa.Id,
                        Kwota = sumaZaliczek,
                        Data = dataWystawienia,
                        Uwagi = "Rozliczone zaliczki: " +
                                string.Join(", ", zaliczkowe.Select(f => f.Numer))
                    });

                    if (sumaZaliczek >= koncowa.RazemBrutto)
                    {
                        koncowa.Zaplacono = true;
                        koncowa.DataZaplaty = dataWystawienia;
                    }
                },
                anulowanie);
        }
        catch (DbUpdateException wyjatek) when (wyjatek.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            // Sprawdzenie powyżej mogło się rozminąć z drugim żądaniem, które
            // rozliczało tę samą zaliczkę. Ostatnie słowo ma baza danych.
            baza.ChangeTracker.Clear();

            return new WynikWystawienia(null, WynikWalidacji.ZBledem(
                "Zaliczki", "te zaliczki są już rozliczone fakturą końcową"));
        }
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

        // Przy korekcie korekty stanem sprzed jest to, co pokazywała
        // poprzednia korekta po zmianie - a nie treść faktury pierwotnej.
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

        // Korekty mają własną serię numerów - patrz UslugaNumeracji.
        model.Numer = await numeracja.NastepnyNumerAsync(
            dataWystawienia, UslugaNumeracji.SeriaKorekt,
            UslugaNumeracji.DomyslnyWzorKorekt, anulowanie);

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

        byte[] xml = Fa3Generator.ZbudujXml(NaModel(faktura, firma));

        // Klient powstaje dla środowiska tej konkretnej firmy - jedna może
        // jeszcze testować integrację, druga wystawiać faktury produkcyjne.
        IKlientKsef klientKsef = fabrykaKlientow.Utworz(firma.Srodowisko);

        try
        {
            await UwierzytelnienieKsef.ZalogujAsync(klientKsef, firma, ochronaTokena, anulowanie);
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
            .Include(f => f.PozycjeZamowienia)
            .Include(f => f.RozliczoneZaliczki)
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
                                             CancellationToken anulowanie = default) =>
        await ZbudujPdfAsync(fakturaId, duplikat: false, anulowanie);

    /// <summary>
    /// Buduje wizualizację, opcjonalnie jako duplikat.
    /// </summary>
    /// <remarks>
    /// Duplikat to ten sam dokument wydany ponownie - gdy odbiorca zgubił
    /// egzemplarz (art. 106l ustawy). Treść jest identyczna; zmienia się samo
    /// oznaczenie i data wystawienia egzemplarza.
    /// </remarks>
    public async Task<byte[]> ZbudujPdfAsync(Guid fakturaId,
                                             bool duplikat,
                                             CancellationToken anulowanie = default)
    {
        FakturaSprzedazy faktura = await baza.FakturySprzedazy
            .Include(f => f.Pozycje)
            .Include(f => f.PozycjeZamowienia)
            .Include(f => f.RozliczoneZaliczki)
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

        if (duplikat)
        {
            opcje = opcje.JakoDuplikat(DateOnly.FromDateTime(DateTime.Today));
        }

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
        Zamowienie = [.. encja.PozycjeZamowienia
            .OrderBy(p => p.NrWiersza)
            .Select(p => new PozycjaZamowienia
            {
                Nazwa = p.Nazwa,
                Jednostka = p.Jednostka,
                Ilosc = p.Ilosc,
                CenaNetto = p.CenaNetto,
                Stawka = StawkaVat.ZKodu(p.KodStawki),
                Gtu = p.Gtu
            })],
        Zaliczkowe = [.. encja.RozliczoneZaliczki
            .OrderBy(z => z.DataWystawienia)
            .Select(z => new DaneZaliczki(z.Numer, z.DataWystawienia, z.NumerKsef, z.Brutto))],
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

}
