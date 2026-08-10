namespace FirmaPro.Domena;

/// <summary>
/// Para pól ewidencji JPK_V7, do których trafia sprzedaż w danej stawce.
/// </summary>
/// <param name="PoleNetto">Numer pola z podstawą opodatkowania, np. 19.</param>
/// <param name="PoleVat">
/// Numer pola z kwotą podatku albo <c>null</c>, gdy przy tej stawce podatek
/// nie występuje.
/// </param>
/// <param name="PoleDodatkowe">
/// Pole „w tym", wypełniane obok głównego - część kwot wykazuje się
/// równocześnie w dwóch miejscach.
/// </param>
public sealed record PolaSprzedazy(int PoleNetto, int? PoleVat, int? PoleDodatkowe = null);

/// <summary>
/// Przyporządkowanie stawek podatku do pól JPK_V7.
/// </summary>
/// <remarks>
/// <para>
/// Ta tablica decyduje o tym, w której rubryce deklaracji wyląduje sprzedaż.
/// Pomyłka tutaj nie zmienia sumy podatku, ale wykazuje go w złym miejscu -
/// a to wychodzi dopiero przy czynnościach sprawdzających.
/// </para>
/// <para>
/// Odwzorowane są stawki, które program potrafi wystawić na fakturze.
/// Transakcje, których system jeszcze nie obsługuje - wewnątrzwspólnotowe
/// nabycie, import usług, ulga na złe długi - mają w deklaracji własne pola
/// i wymagają osobnego uzupełnienia.
/// </para>
/// </remarks>
public static class PolaJpk
{
    private static readonly Dictionary<string, PolaSprzedazy> Mapowanie =
        new(StringComparer.Ordinal)
        {
            // Sprzedaż krajowa opodatkowana - podstawa i podatek.
            ["23"] = new(19, 20),
            ["22"] = new(19, 20),
            ["8"] = new(17, 18),
            ["7"] = new(17, 18),
            ["5"] = new(15, 16),

            // Stawka zero w trzech odmianach trafia do trzech różnych pól.
            ["0 KR"] = new(13, null),
            ["0 WDT"] = new(21, null),
            ["0 EX"] = new(22, null),

            // Sprzedaż bez podatku.
            ["zw"] = new(10, null),
            ["np I"] = new(11, null),

            // Usługi z art. 100 ust. 1 pkt 4 wykazuje się w polu 11, a więc
            // razem z pozostałą sprzedażą poza terytorium kraju, i dodatkowo
            // wyodrębnia w polu 12.
            ["np II"] = new(11, null, 12),

            // Dostawa, dla której podatnikiem jest nabywca.
            ["oo"] = new(31, null)
        };

    /// <summary>
    /// Znajduje pola, do których trafia sprzedaż w danej stawce.
    /// </summary>
    /// <returns>
    /// <c>null</c>, gdy stawka nie ma odpowiednika w deklaracji - dotyczy to
    /// ryczałtu dla taksówek (3% i 4%), rozliczanego w osobnej deklaracji
    /// VAT-12, a nie w JPK_V7.
    /// </returns>
    public static PolaSprzedazy? DlaStawki(StawkaVat stawka)
    {
        ArgumentNullException.ThrowIfNull(stawka);
        return Mapowanie.GetValueOrDefault(stawka.Kod);
    }

    /// <summary>Czy stawka w ogóle ma miejsce w deklaracji JPK_V7.</summary>
    public static bool ObslugiwanaWDeklaracji(StawkaVat stawka) =>
        DlaStawki(stawka) is not null;

    // --- pola zakupów -------------------------------------------------------

    /// <summary>Wartość netto nabycia środków trwałych.</summary>
    public const int NettoSrodkiTrwale = 40;

    /// <summary>Podatek naliczony od nabycia środków trwałych.</summary>
    public const int VatSrodkiTrwale = 41;

    /// <summary>Wartość netto pozostałych nabyć.</summary>
    public const int NettoPozostale = 42;

    /// <summary>Podatek naliczony od pozostałych nabyć.</summary>
    public const int VatPozostale = 43;

    /// <summary>Pola ewidencji sprzedaży niosące kwotę podatku należnego.</summary>
    /// <remarks>
    /// Z nich powstaje łączna wysokość podatku należnego (P_38). Lista
    /// obejmuje wyłącznie pola, które program potrafi wypełnić - pozostałe
    /// pozycje deklaracji wymagają danych, których system jeszcze nie zbiera.
    /// </remarks>
    public static IReadOnlyList<int> PolaPodatkuNaleznego { get; } = [16, 18, 20];
}
