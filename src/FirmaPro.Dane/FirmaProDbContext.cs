using FirmaPro.Dane.Encje;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FirmaPro.Dane;

/// <summary>
/// Wskazuje firmę, w której kontekście pracuje bieżący użytkownik.
/// </summary>
/// <remarks>
/// W aplikacji webowej dostarcza ją warstwa żądania (na podstawie firmy
/// wybranej po zalogowaniu). W testach i w zadaniach w tle podaje się ją
/// wprost.
/// </remarks>
public interface IKontekstFirmy
{
    /// <summary>
    /// Identyfikator firmy albo <c>null</c>, gdy operacja nie dotyczy żadnej
    /// firmy (np. rejestracja konta czy zadanie administracyjne).
    /// </summary>
    Guid? FirmaId { get; }
}

/// <summary>Najprostsza implementacja - stała firma przez cały czas życia.</summary>
public sealed class StalyKontekstFirmy(Guid? firmaId) : IKontekstFirmy
{
    public Guid? FirmaId { get; } = firmaId;
}

/// <summary>Próba zapisania danych w sposób łamiący izolację firm.</summary>
public sealed class BladIzolacjiFirmException(string komunikat) : InvalidOperationException(komunikat);

/// <summary>Próba zmiany dokumentu, którego nie wolno już modyfikować.</summary>
public sealed class DokumentZamknietyException(string komunikat) : InvalidOperationException(komunikat);

/// <summary>
/// Kontekst bazy danych systemu.
/// </summary>
/// <remarks>
/// <para>
/// Izolacja danych między firmami jest tu wymuszona na trzy sposoby, bo
/// pomyłka w tym miejscu oznaczałaby pokazanie jednej firmie dokumentów
/// drugiej:
/// </para>
/// <list type="number">
/// <item>globalne filtry zapytań - każde zapytanie o encję firmową jest
/// automatycznie zawężone; nie da się tego pominąć zapominając o warunku
/// <c>Where</c>,</item>
/// <item>automatyczne ustawianie <c>FirmaId</c> przy dodawaniu rekordu, żeby
/// nie dało się zapisać danych "bez firmy",</item>
/// <item>kontrola przy zapisie - próba dopisania lub zmiany rekordu należącego
/// do innej firmy kończy się wyjątkiem, a nie cichym zapisem.</item>
/// </list>
/// <para>
/// Gdy kontekst firmy nie jest ustawiony, filtry nie przepuszczają niczego.
/// To celowe: brak informacji o firmie ma oznaczać "nic nie widać",
/// a nie "widać wszystko".
/// </para>
/// </remarks>
public class FirmaProDbContext : DbContext
{
    private readonly IKontekstFirmy _kontekstFirmy;
    private readonly TimeProvider _czas;

    public FirmaProDbContext(DbContextOptions<FirmaProDbContext> opcje,
                             IKontekstFirmy kontekstFirmy,
                             TimeProvider? czas = null)
        : base(opcje)
    {
        _kontekstFirmy = kontekstFirmy;
        _czas = czas ?? TimeProvider.System;
    }

    /// <summary>Firma, w której kontekście działa ten egzemplarz kontekstu.</summary>
    public Guid? AktualnaFirmaId => _kontekstFirmy.FirmaId;

    public DbSet<Firma> Firmy => Set<Firma>();
    public DbSet<Uzytkownik> Uzytkownicy => Set<Uzytkownik>();
    public DbSet<CzlonkostwoWFirmie> Czlonkostwa => Set<CzlonkostwoWFirmie>();
    public DbSet<Kontrahent> Kontrahenci => Set<Kontrahent>();
    public DbSet<FakturaSprzedazy> FakturySprzedazy => Set<FakturaSprzedazy>();
    public DbSet<PozycjaFakturySprzedazy> PozycjeFaktur => Set<PozycjaFakturySprzedazy>();
    public DbSet<FakturaZakupu> FakturyZakupu => Set<FakturaZakupu>();
    public DbSet<KwotaVatZakupu> KwotyVatZakupu => Set<KwotaVatZakupu>();
    public DbSet<ZamkniecieOkresuVat> ZamknieciaOkresow => Set<ZamkniecieOkresuVat>();
    public DbSet<SeriaNumeracji> SerieNumeracji => Set<SeriaNumeracji>();

    // Nazwa parametru musi odpowiadać deklaracji z klasy bazowej, dlatego
    // jako jedyna w tym pliku pozostaje angielska.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        KonfigurujFirme(modelBuilder);
        KonfigurujUzytkownikow(modelBuilder);
        KonfigurujKontrahentow(modelBuilder);
        KonfigurujFaktury(modelBuilder);
        KonfigurujZakupy(modelBuilder);
        KonfigurujZamknieciaOkresow(modelBuilder);
        KonfigurujNumeracje(modelBuilder);

        ZastosujFiltryFirmy(modelBuilder);
    }

    private static void KonfigurujFirme(ModelBuilder budowniczy)
    {
        budowniczy.Entity<Firma>(e =>
        {
            e.ToTable("firmy");
            e.HasKey(f => f.Id);
            e.Property(f => f.Nazwa).HasMaxLength(512).IsRequired();
            e.Property(f => f.Nip).HasMaxLength(10).IsRequired();
            e.Property(f => f.KodKraju).HasMaxLength(2).IsRequired();
            e.Property(f => f.AdresLinia1).HasMaxLength(512).IsRequired();
            e.Property(f => f.AdresLinia2).HasMaxLength(512);
            e.Property(f => f.Email).HasMaxLength(256);
            e.Property(f => f.Telefon).HasMaxLength(64);
            e.Property(f => f.RachunekBankowy).HasMaxLength(34);
            e.Property(f => f.NazwaBanku).HasMaxLength(256);
            e.Property(f => f.MiejsceWystawienia).HasMaxLength(256);
            e.Property(f => f.StopkaFaktury).HasMaxLength(3500);
            // Nazwy środowisk zapisujemy tekstem - w bazie czyta się je bez
            // zaglądania do kodu, a dodanie nowego nie przesuwa numeracji.
            e.Property(f => f.Srodowisko).HasConversion<string>().HasMaxLength(16);
            e.Property(f => f.TypOkresuVat).HasConversion<string>().HasMaxLength(16);
            e.Property(f => f.KodUrzeduSkarbowego).HasMaxLength(8);
            e.HasIndex(f => f.Nip);
        });
    }

    private static void KonfigurujUzytkownikow(ModelBuilder budowniczy)
    {
        budowniczy.Entity<Uzytkownik>(e =>
        {
            e.ToTable("uzytkownicy");
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.HaszHasla).HasMaxLength(512).IsRequired();
            e.Property(u => u.ImieINazwisko).HasMaxLength(256);
            e.HasIndex(u => u.Email).IsUnique();
        });

        budowniczy.Entity<CzlonkostwoWFirmie>(e =>
        {
            e.ToTable("czlonkostwa");
            e.HasKey(c => c.Id);
            e.Property(c => c.Rola).HasConversion<string>().HasMaxLength(32);

            e.HasOne(c => c.Uzytkownik)
                .WithMany(u => u!.Czlonkostwa)
                .HasForeignKey(c => c.UzytkownikId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Firma)
                .WithMany(f => f!.Czlonkowie)
                .HasForeignKey(c => c.FirmaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Jeden użytkownik ma w danej firmie dokładnie jedną rolę.
            e.HasIndex(c => new { c.UzytkownikId, c.FirmaId }).IsUnique();
        });
    }

    private static void KonfigurujKontrahentow(ModelBuilder budowniczy)
    {
        budowniczy.Entity<Kontrahent>(e =>
        {
            e.ToTable("kontrahenci");
            e.HasKey(k => k.Id);
            e.Property(k => k.Nazwa).HasMaxLength(512).IsRequired();
            e.Property(k => k.Nip).HasMaxLength(10);
            e.Property(k => k.KodKraju).HasMaxLength(2).IsRequired();
            e.Property(k => k.AdresLinia1).HasMaxLength(512);
            e.Property(k => k.AdresLinia2).HasMaxLength(512);
            e.Property(k => k.Email).HasMaxLength(256);
            e.Property(k => k.Telefon).HasMaxLength(64);
            e.Property(k => k.KodUe).HasMaxLength(2);
            e.Property(k => k.NrVatUe).HasMaxLength(32);

            // Szukanie kontrahenta po NIP w obrębie firmy to najczęstsze
            // zapytanie w kartotece.
            e.HasIndex(k => new { k.FirmaId, k.Nip });
            e.HasIndex(k => new { k.FirmaId, k.Nazwa });
        });
    }

    private static void KonfigurujFaktury(ModelBuilder budowniczy)
    {
        budowniczy.Entity<FakturaSprzedazy>(e =>
        {
            e.ToTable("faktury_sprzedazy");
            e.HasKey(f => f.Id);

            e.Property(f => f.Numer).HasMaxLength(256).IsRequired();
            e.Property(f => f.Waluta).HasMaxLength(3).IsRequired();
            e.Property(f => f.MiejsceWystawienia).HasMaxLength(256);
            e.Property(f => f.Rodzaj).HasConversion<string>().HasMaxLength(16);
            e.Property(f => f.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(f => f.FormaPlatnosci).HasConversion<string>().HasMaxLength(16);

            e.Property(f => f.NabywcaNazwa).HasMaxLength(512).IsRequired();
            e.Property(f => f.NabywcaNip).HasMaxLength(10);
            e.Property(f => f.NabywcaKodKraju).HasMaxLength(2).IsRequired();
            e.Property(f => f.NabywcaAdresLinia1).HasMaxLength(512);
            e.Property(f => f.NabywcaAdresLinia2).HasMaxLength(512);
            e.Property(f => f.NabywcaKodUe).HasMaxLength(2);
            e.Property(f => f.NabywcaNrVatUe).HasMaxLength(32);

            e.Property(f => f.RachunekBankowy).HasMaxLength(34);
            e.Property(f => f.NazwaBanku).HasMaxLength(256);
            e.Property(f => f.Stopka).HasMaxLength(3500);
            e.Property(f => f.PodstawaZwolnienia).HasMaxLength(256);
            e.Property(f => f.NumerKsef).HasMaxLength(64);
            e.Property(f => f.UwagiKsef).HasMaxLength(2000);
            e.Property(f => f.SkrotXml).HasMaxLength(64);
            e.Property(f => f.KorygowanaNumer).HasMaxLength(256);
            e.Property(f => f.KorygowanaNumerKsef).HasMaxLength(64);
            e.Property(f => f.PrzyczynaKorekty).HasMaxLength(256);
            e.Property(f => f.TypKorekty).HasConversion<string>().HasMaxLength(24);

            // Faktury korygowanej nie wolno usunąć - korekta bez dokumentu
            // pierwotnego jest niekompletna.
            e.HasOne(f => f.FakturaKorygowana)
                .WithMany()
                .HasForeignKey(f => f.FakturaKorygowanaId)
                .OnDelete(DeleteBehavior.Restrict);

            // Kwoty jako numeric(18,2) - typ dziesiętny bazy, bez ryzyka
            // błędów zapisu binarnego.
            e.Property(f => f.RazemNetto).HasPrecision(18, 2);
            e.Property(f => f.RazemVat).HasPrecision(18, 2);
            e.Property(f => f.RazemBrutto).HasPrecision(18, 2);

            e.HasOne(f => f.Kontrahent)
                .WithMany()
                .HasForeignKey(f => f.KontrahentId)
                // Kontrahenta, na którego wystawiono fakturę, nie wolno
                // usunąć - dokument musi zostać kompletny.
                .OnDelete(DeleteBehavior.Restrict);

            // Ostateczna gwarancja niepowtarzalności numeru faktury; działa
            // nawet gdy dwie osoby wystawiają dokument w tej samej chwili.
            e.HasIndex(f => new { f.FirmaId, f.Numer }).IsUnique();
            e.HasIndex(f => new { f.FirmaId, f.DataWystawienia });
            // Rejestr VAT wybiera dokumenty po dacie ujęcia.
            e.HasIndex(f => new { f.FirmaId, f.DataUjeciaVat });
            e.HasIndex(f => f.NumerKsef);
        });

        budowniczy.Entity<PozycjaFakturySprzedazy>(e =>
        {
            e.ToTable("pozycje_faktur_sprzedazy");
            e.HasKey(p => p.Id);

            e.Property(p => p.Nazwa).HasMaxLength(512).IsRequired();
            e.Property(p => p.Jednostka).HasMaxLength(64);
            e.Property(p => p.KodStawki).HasMaxLength(8).IsRequired();
            e.Property(p => p.Gtu).HasMaxLength(8);
            e.Property(p => p.Pkwiu).HasMaxLength(50);
            e.Property(p => p.Cn).HasMaxLength(50);
            e.Property(p => p.Indeks).HasMaxLength(50);

            // Ilość do sześciu miejsc, cena do ośmiu - tyle dopuszcza schemat
            // FA(3) w typach TIlosci i TKwotowy2.
            e.Property(p => p.Ilosc).HasPrecision(18, 6);
            e.Property(p => p.CenaNetto).HasPrecision(18, 8);
            e.Property(p => p.WartoscNetto).HasPrecision(18, 2);
            e.Property(p => p.KwotaVat).HasPrecision(18, 2);

            e.HasOne(p => p.Faktura)
                .WithMany(f => f!.Pozycje)
                .HasForeignKey(p => p.FakturaId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(p => new { p.FakturaId, p.NrWiersza }).IsUnique();
        });
    }

    private static void KonfigurujZakupy(ModelBuilder budowniczy)
    {
        budowniczy.Entity<FakturaZakupu>(e =>
        {
            e.ToTable("faktury_zakupu");
            e.HasKey(f => f.Id);

            e.Property(f => f.Numer).HasMaxLength(256).IsRequired();
            e.Property(f => f.Waluta).HasMaxLength(3).IsRequired();
            e.Property(f => f.SprzedawcaNazwa).HasMaxLength(512).IsRequired();
            e.Property(f => f.SprzedawcaNip).HasMaxLength(16);
            e.Property(f => f.Rodzaj).HasConversion<string>().HasMaxLength(16);
            e.Property(f => f.NumerKsef).HasMaxLength(64);
            e.Property(f => f.Uwagi).HasMaxLength(2000);

            e.Property(f => f.RazemNetto).HasPrecision(18, 2);
            e.Property(f => f.RazemVat).HasPrecision(18, 2);
            e.Property(f => f.RazemBrutto).HasPrecision(18, 2);

            e.HasOne(f => f.Kontrahent)
                .WithMany()
                .HasForeignKey(f => f.KontrahentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Numer faktury zakupu nadaje sprzedawca, więc dwa różne podmioty
            // mogą wystawić dokumenty o tym samym numerze. Niepowtarzalność
            // ma sens dopiero w połączeniu z NIP-em wystawcy - i tylko wtedy,
            // gdy ten NIP w ogóle jest znany.
            e.HasIndex(f => new { f.FirmaId, f.SprzedawcaNip, f.Numer })
                .IsUnique()
                .HasFilter("\"SprzedawcaNip\" IS NOT NULL");

            // Rejestr VAT wybiera dokumenty po dacie ujęcia - to po niej
            // najczęściej przeszukujemy tę tabelę.
            e.HasIndex(f => new { f.FirmaId, f.DataUjecia });
        });

        budowniczy.Entity<KwotaVatZakupu>(e =>
        {
            e.ToTable("kwoty_vat_zakupu");
            e.HasKey(k => k.Id);

            e.Property(k => k.KodStawki).HasMaxLength(8).IsRequired();
            e.Property(k => k.Netto).HasPrecision(18, 2);
            e.Property(k => k.Vat).HasPrecision(18, 2);

            e.HasOne(k => k.Faktura)
                .WithMany(f => f!.Kwoty)
                .HasForeignKey(k => k.FakturaZakupuId)
                .OnDelete(DeleteBehavior.Cascade);

            // Jedna stawka może wystąpić na fakturze tylko raz - inaczej
            // rejestr pokazywałby ten sam podatek w dwóch wierszach.
            e.HasIndex(k => new { k.FakturaZakupuId, k.KodStawki }).IsUnique();
        });
    }

    private static void KonfigurujZamknieciaOkresow(ModelBuilder budowniczy)
    {
        budowniczy.Entity<ZamkniecieOkresuVat>(e =>
        {
            e.ToTable("zamkniecia_okresow_vat");
            e.HasKey(z => z.Id);
            e.Property(z => z.Typ).HasConversion<string>().HasMaxLength(16);

            // Okres można zamknąć tylko raz - inaczej dwie różne kwoty
            // pretendowałyby do roli nadwyżki przechodzącej dalej.
            e.HasIndex(z => new { z.FirmaId, z.Typ, z.Rok, z.Numer }).IsUnique();
        });
    }

    private static void KonfigurujNumeracje(ModelBuilder budowniczy)
    {
        budowniczy.Entity<SeriaNumeracji>(e =>
        {
            e.ToTable("serie_numeracji");
            e.HasKey(s => s.Id);
            e.Property(s => s.Nazwa).HasMaxLength(128).IsRequired();
            e.Property(s => s.Wzor).HasMaxLength(128).IsRequired();
            e.HasIndex(s => new { s.FirmaId, s.Nazwa, s.Rok, s.Miesiac }).IsUnique();
        });
    }

    /// <summary>
    /// Zakłada globalny filtr na każdą encję oznaczoną
    /// <see cref="INalezyDoFirmy"/>.
    /// </summary>
    private void ZastosujFiltryFirmy(ModelBuilder budowniczy)
    {
        budowniczy.Entity<Kontrahent>()
            .HasQueryFilter(k => k.FirmaId == AktualnaFirmaId);
        budowniczy.Entity<FakturaSprzedazy>()
            .HasQueryFilter(f => f.FirmaId == AktualnaFirmaId);
        budowniczy.Entity<PozycjaFakturySprzedazy>()
            .HasQueryFilter(p => p.FirmaId == AktualnaFirmaId);
        budowniczy.Entity<FakturaZakupu>()
            .HasQueryFilter(f => f.FirmaId == AktualnaFirmaId);
        budowniczy.Entity<KwotaVatZakupu>()
            .HasQueryFilter(k => k.FirmaId == AktualnaFirmaId);
        budowniczy.Entity<ZamkniecieOkresuVat>()
            .HasQueryFilter(z => z.FirmaId == AktualnaFirmaId);
        budowniczy.Entity<SeriaNumeracji>()
            .HasQueryFilter(s => s.FirmaId == AktualnaFirmaId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrzygotujZapis();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
                                               CancellationToken cancellationToken = default)
    {
        PrzygotujZapis();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Uzupełnia pola techniczne i pilnuje reguł, których nie da się wyrazić
    /// więzami bazy danych.
    /// </summary>
    private void PrzygotujZapis()
    {
        DateTimeOffset teraz = _czas.GetUtcNow();

        foreach (EntityEntry wpis in ChangeTracker.Entries().ToList())
        {
            if (wpis.Entity is EncjaBazowa encja)
            {
                if (wpis.State == EntityState.Added)
                {
                    encja.UtworzonoUtc = teraz;
                }
                else if (wpis.State == EntityState.Modified)
                {
                    encja.ZmienionoUtc = teraz;
                    // Data utworzenia jest niezmienna - chroni ślad w czasie.
                    wpis.Property(nameof(EncjaBazowa.UtworzonoUtc)).IsModified = false;
                }
            }

            if (wpis.Entity is INalezyDoFirmy doFirmy)
            {
                PilnujFirmy(wpis, doFirmy);
            }

            if (wpis.Entity is FakturaSprzedazy faktura)
            {
                PilnujNiezmiennosciFaktury(wpis, faktura);
            }
        }
    }

    private void PilnujFirmy(EntityEntry wpis, INalezyDoFirmy encja)
    {
        if (wpis.State == EntityState.Added)
        {
            if (encja.FirmaId == Guid.Empty)
            {
                // Uzupełniamy sami, żeby nie dało się zapisać rekordu "bez firmy".
                encja.FirmaId = AktualnaFirmaId ?? throw new BladIzolacjiFirmException(
                    $"Nie można zapisać rekordu {wpis.Entity.GetType().Name}: " +
                    "nie ustawiono firmy, w której kontekście działa operacja.");
            }
            else if (AktualnaFirmaId is { } aktualna && encja.FirmaId != aktualna)
            {
                throw new BladIzolacjiFirmException(
                    $"Próba zapisania rekordu {wpis.Entity.GetType().Name} " +
                    "w innej firmie niż bieżąca.");
            }
        }
        else if (wpis.State is EntityState.Modified or EntityState.Deleted)
        {
            if (AktualnaFirmaId is { } aktualna && encja.FirmaId != aktualna)
            {
                throw new BladIzolacjiFirmException(
                    $"Próba zmiany rekordu {wpis.Entity.GetType().Name} " +
                    "należącego do innej firmy.");
            }

            // Przeniesienie rekordu do innej firmy byłoby obejściem izolacji.
            PropertyEntry pole = wpis.Property(nameof(INalezyDoFirmy.FirmaId));
            if (pole.IsModified)
            {
                throw new BladIzolacjiFirmException(
                    "Nie wolno przenosić rekordu między firmami.");
            }
        }
    }

    private static void PilnujNiezmiennosciFaktury(EntityEntry wpis, FakturaSprzedazy faktura)
    {
        if (wpis.State == EntityState.Deleted && faktura.CzyZamknieta)
        {
            throw new DokumentZamknietyException(
                $"Faktury {faktura.Numer} nie można usunąć - została już " +
                "wysłana do KSeF. Zmiany wprowadza się fakturą korygującą.");
        }

        if (wpis.State != EntityState.Modified)
        {
            return;
        }

        // Po wysłaniu dokumentu wolno zmieniać już tylko to, co dotyczy jego
        // obiegu w KSeF i rozliczenia płatności - nigdy treści faktury.
        string[] poleDozwolone =
        [
            nameof(FakturaSprzedazy.Status),
            nameof(FakturaSprzedazy.NumerKsef),
            nameof(FakturaSprzedazy.DataPrzyjeciaKsef),
            nameof(FakturaSprzedazy.UwagiKsef),
            nameof(FakturaSprzedazy.SkrotXml),
            nameof(FakturaSprzedazy.Zaplacono),
            nameof(FakturaSprzedazy.DataZaplaty),
            // Okres rejestru VAT to kwalifikacja księgowa, a nie treść
            // dokumentu - jego poprawienie nie narusza wysłanej faktury.
            nameof(FakturaSprzedazy.DataUjeciaVat),
            nameof(EncjaBazowa.ZmienionoUtc)
        ];

        StatusKsef statusPrzedZmiana = wpis.Property(nameof(FakturaSprzedazy.Status))
            .OriginalValue is StatusKsef poprzedni ? poprzedni : faktura.Status;

        bool bylaZamknieta = statusPrzedZmiana is StatusKsef.Przyjeta or StatusKsef.Wyslana;
        if (!bylaZamknieta)
        {
            return;
        }

        List<string> zabronione = wpis.Properties
            .Where(p => p.IsModified && !poleDozwolone.Contains(p.Metadata.Name))
            .Select(p => p.Metadata.Name)
            .ToList();

        if (zabronione.Count > 0)
        {
            throw new DokumentZamknietyException(
                $"Faktura {faktura.Numer} została już wysłana do KSeF i nie " +
                "wolno zmieniać jej treści. Próbowano zmienić: " +
                string.Join(", ", zabronione) +
                ". Zmiany wprowadza się fakturą korygującą.");
        }
    }
}
