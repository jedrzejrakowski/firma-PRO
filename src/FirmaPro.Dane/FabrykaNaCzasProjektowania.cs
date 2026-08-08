using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FirmaPro.Dane;

/// <summary>
/// Tworzy kontekst bazy na potrzeby narzędzi wiersza poleceń
/// (<c>dotnet ef migrations</c>).
/// </summary>
/// <remarks>
/// Narzędzia nie wiedzą, jak zbudować kontekst wymagający informacji
/// o firmie, dlatego dostają egzemplarz bez wybranej firmy - do wygenerowania
/// migracji wystarczy sam kształt modelu. Ciąg połączenia można podać
/// zmienną środowiskową <c>FIRMAPRO_DB</c>; domyślny wskazuje lokalną bazę
/// deweloperską.
/// </remarks>
public sealed class FabrykaNaCzasProjektowania : IDesignTimeDbContextFactory<FirmaProDbContext>
{
    private const string DomyslnePolaczenie =
        "Host=localhost;Port=5432;Database=firmapro;Username=postgres;Password=postgres";

    public FirmaProDbContext CreateDbContext(string[] args)
    {
        string polaczenie =
            Environment.GetEnvironmentVariable("FIRMAPRO_DB") ?? DomyslnePolaczenie;

        DbContextOptions<FirmaProDbContext> opcje =
            new DbContextOptionsBuilder<FirmaProDbContext>()
                .UseNpgsql(polaczenie)
                .Options;

        return new FirmaProDbContext(opcje, new StalyKontekstFirmy(null));
    }
}
