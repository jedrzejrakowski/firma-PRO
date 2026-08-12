namespace FirmaPro.Testy;

/// <summary>
/// Zegar sterowany przez test.
/// </summary>
/// <remarks>
/// Reguły z terminami - ważność zaproszenia, okresy rozliczeniowe - trzeba
/// sprawdzać na przesuniętym czasie, a nie czekając. Własna, kilkulinijkowa
/// atrapa wystarcza i oszczędza kolejnej zależności w projekcie testów.
/// </remarks>
public sealed class CzasTestowy(DateTimeOffset poczatek) : TimeProvider
{
    private DateTimeOffset _teraz = poczatek;

    public override DateTimeOffset GetUtcNow() => _teraz;

    /// <summary>Przesuwa zegar do przodu.</summary>
    public void Przesun(TimeSpan o) => _teraz = _teraz.Add(o);
}
