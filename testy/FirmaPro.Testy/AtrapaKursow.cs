using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;

namespace FirmaPro.Testy;

/// <summary>
/// Kursy walut podstawiane w miejsce serwisu NBP.
/// </summary>
/// <remarks>
/// Kursy zmieniają się codziennie, więc test odpytujący prawdziwy serwis
/// dawałby co dzień inny wynik - i przestawałby przechodzić przy każdej
/// przerwie w łączności. Atrapa trzyma stałą tabelę, a osobne testy
/// <see cref="TestyWalut"/> pilnują reguły wyboru dnia.
/// </remarks>
public sealed class AtrapaKursow : IKursyWalut
{
    /// <summary>Kursy, które atrapa zwraca - kod waluty do wartości.</summary>
    public Dictionary<string, decimal> Tabela { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EUR"] = 4.2837m,
        ["USD"] = 3.9152m,
        ["GBP"] = 5.1043m
    };

    /// <summary>Gdy ustawione, każde pytanie o kurs kończy się tym błędem.</summary>
    public BladKursuException? Blad { get; set; }

    /// <summary>O jakie waluty pytano - w kolejności zapytań.</summary>
    public List<string> Zapytania { get; } = [];

    public Task<KursWaluty> KursAsync(string waluta, DateOnly naDzien,
                                      CancellationToken anulowanie = default)
    {
        Zapytania.Add(waluta);

        if (Blad is { } blad)
        {
            return Task.FromException<KursWaluty>(blad);
        }

        if (string.Equals(waluta, "PLN", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(KursWaluty.Zlotowy(naDzien));
        }

        if (!Tabela.TryGetValue(waluta, out decimal wartosc))
        {
            return Task.FromException<KursWaluty>(
                new BladKursuException($"Atrapa nie zna waluty {waluta}."));
        }

        // Tabela z dnia poprzedzającego - tak samo, jak zwróciłby NBP
        // w zwykły dzień roboczy.
        return Task.FromResult(
            new KursWaluty(waluta.ToUpperInvariant(), wartosc, naDzien, "160/A/NBP/2026"));
    }
}
