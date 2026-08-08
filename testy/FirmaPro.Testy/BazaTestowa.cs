using FirmaPro.Dane;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Świeża baza PostgreSQL na czas jednego zestawu testów.
/// </summary>
/// <remarks>
/// Testy izolacji między firmami celowo działają na prawdziwym PostgreSQL,
/// a nie na bazie w pamięci. Filtry zapytań, więzy unikalności i typ
/// <c>numeric</c> zachowują się inaczej w każdym silniku - sprawdzanie ich
/// na atrapie dawałoby złudne poczucie bezpieczeństwa akurat tam, gdzie
/// pomyłka byłaby najdroższa.
/// </remarks>
public sealed class BazaTestowa : IAsyncLifetime
{
    private const string ZmiennaPolaczenia = "FIRMAPRO_TEST_DB";

    /// <summary>
    /// Domyślne połączenie wskazuje lokalny serwer. W CI podmienia je
    /// zmienna środowiskowa.
    /// </summary>
    private const string DomyslnyServer =
        "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    private readonly string _nazwaBazy = "firmapro_testy_" + Guid.NewGuid().ToString("N")[..12];

    private static string PolaczenieDoSerwera =>
        Environment.GetEnvironmentVariable(ZmiennaPolaczenia) ?? DomyslnyServer;

    /// <summary>Ciąg połączenia do bazy utworzonej na potrzeby testów.</summary>
    public string Polaczenie { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var budowniczy = new NpgsqlConnectionStringBuilder(PolaczenieDoSerwera)
        {
            Database = "postgres"
        };

        try
        {
            await using (var polaczenie = new NpgsqlConnection(budowniczy.ConnectionString))
            {
                await polaczenie.OpenAsync();
                await using var polecenie = polaczenie.CreateCommand();
                polecenie.CommandText = $"CREATE DATABASE \"{_nazwaBazy}\"";
                await polecenie.ExecuteNonQueryAsync();
            }
        }
        catch (NpgsqlException blad)
        {
            throw new InvalidOperationException(
                "Testy warstwy danych wymagają działającego serwera PostgreSQL. " +
                "Uruchom go poleceniem: " +
                "docker run --rm -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:16 " +
                $"albo wskaż inny serwer zmienną {ZmiennaPolaczenia}.", blad);
        }

        budowniczy.Database = _nazwaBazy;
        Polaczenie = budowniczy.ConnectionString;

        await using FirmaProDbContext kontekst = UtworzKontekst(null);
        await kontekst.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        var budowniczy = new NpgsqlConnectionStringBuilder(PolaczenieDoSerwera)
        {
            Database = "postgres"
        };

        await using var polaczenie = new NpgsqlConnection(budowniczy.ConnectionString);
        await polaczenie.OpenAsync();
        await using var polecenie = polaczenie.CreateCommand();
        // WITH (FORCE) rozłącza ewentualne pozostawione sesje.
        polecenie.CommandText = $"DROP DATABASE IF EXISTS \"{_nazwaBazy}\" WITH (FORCE)";
        await polecenie.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Tworzy kontekst pracujący w podanej firmie. <c>null</c> oznacza brak
    /// wybranej firmy - wtedy dane firmowe mają być niewidoczne.
    /// </summary>
    public FirmaProDbContext UtworzKontekst(Guid? firmaId)
    {
        DbContextOptions<FirmaProDbContext> opcje =
            new DbContextOptionsBuilder<FirmaProDbContext>()
                .UseNpgsql(Polaczenie)
                .Options;

        return new FirmaProDbContext(opcje, new StalyKontekstFirmy(firmaId));
    }
}

/// <summary>
/// Zbiór testów współdzielących jedną bazę - jej utworzenie i migracja
/// trwają na tyle długo, że nie warto powtarzać ich dla każdej klasy.
/// </summary>
[CollectionDefinition(Nazwa)]
public sealed class KolekcjaBazy : ICollectionFixture<BazaTestowa>
{
    public const string Nazwa = "baza";
}
