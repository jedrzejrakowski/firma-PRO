using System.Diagnostics.CodeAnalysis;

namespace FirmaPro.Domena;

/// <summary>
/// Stawka podatku dopuszczona przez schemat FA(3) (typ TStawkaPodatku).
/// </summary>
/// <remarks>
/// Stawka to nie liczba: obok "23" czy "8" schemat dopuszcza wartości
/// "0 WDT", "zw", "oo" czy "np I". Każda z nich trafia do innego pola
/// podsumowania faktury, dlatego to przyporządkowanie trzymamy razem
/// z samą stawką - inaczej łatwo o pomyłkę przy dodawaniu nowej.
///
/// Opisy pól pochodzą z dokumentacji schematu opublikowanej przez
/// Ministerstwo Finansów.
/// </remarks>
public sealed record StawkaVat
{
    private StawkaVat(string kod, decimal? procent, string poleNetto,
                      string? poleVat, string opis)
    {
        Kod = kod;
        Procent = procent;
        PoleNetto = poleNetto;
        PoleVat = poleVat;
        Opis = opis;
    }

    /// <summary>Wartość zapisywana w polu P_12 faktury.</summary>
    public string Kod { get; }

    /// <summary>Procent podatku; <c>null</c> gdy podatek nie występuje.</summary>
    public decimal? Procent { get; }

    /// <summary>Pole podsumowania, do którego trafia wartość netto.</summary>
    public string PoleNetto { get; }

    /// <summary>Pole podsumowania z kwotą podatku; <c>null</c> gdy brak.</summary>
    public string? PoleVat { get; }

    /// <summary>Opis do pokazania użytkownikowi.</summary>
    public string Opis { get; }

    /// <summary>Czy przy tej stawce w ogóle nalicza się podatek.</summary>
    public bool NaliczaPodatek => Procent is > 0m;

    // --- stawki podstawowe i obniżone -------------------------------------

    public static readonly StawkaVat Vat23 = new("23", 23m, "P_13_1", "P_14_1", "23%");
    public static readonly StawkaVat Vat22 = new("22", 22m, "P_13_1", "P_14_1", "22%");
    public static readonly StawkaVat Vat8 = new("8", 8m, "P_13_2", "P_14_2", "8%");
    public static readonly StawkaVat Vat7 = new("7", 7m, "P_13_2", "P_14_2", "7%");
    public static readonly StawkaVat Vat5 = new("5", 5m, "P_13_3", "P_14_3", "5%");

    // Ryczałt dla taksówek osobowych.
    public static readonly StawkaVat Vat4 = new("4", 4m, "P_13_4", "P_14_4", "4% (taksówki)");
    public static readonly StawkaVat Vat3 = new("3", 3m, "P_13_4", "P_14_4", "3% (taksówki)");

    // --- stawka zero w trzech odmianach -----------------------------------

    public static readonly StawkaVat ZeroKrajowa =
        new("0 KR", 0m, "P_13_6_1", null, "0% krajowa");
    public static readonly StawkaVat ZeroWdt =
        new("0 WDT", 0m, "P_13_6_2", null, "0% wewnątrzwspólnotowa dostawa");
    public static readonly StawkaVat ZeroEksport =
        new("0 EX", 0m, "P_13_6_3", null, "0% eksport");

    // --- przypadki bez podatku --------------------------------------------

    public static readonly StawkaVat Zwolniona =
        new("zw", null, "P_13_7", null, "zwolniona z podatku");
    public static readonly StawkaVat OdwrotneObciazenie =
        new("oo", null, "P_13_10", null, "odwrotne obciążenie");
    public static readonly StawkaVat NiepodlegajacaI =
        new("np I", null, "P_13_8", null, "niepodlegająca - poza terytorium kraju");
    public static readonly StawkaVat NiepodlegajacaII =
        new("np II", null, "P_13_9", null, "niepodlegająca - usługi z art. 100 ust. 1 pkt 4");

    /// <summary>Wszystkie dopuszczalne stawki, w kolejności do pokazania na liście.</summary>
    public static IReadOnlyList<StawkaVat> Wszystkie { get; } =
    [
        Vat23, Vat22, Vat8, Vat7, Vat5, Vat4, Vat3,
        ZeroKrajowa, ZeroWdt, ZeroEksport,
        Zwolniona, OdwrotneObciazenie, NiepodlegajacaI, NiepodlegajacaII
    ];

    private static readonly Dictionary<string, StawkaVat> WedlugKodu =
        Wszystkie.ToDictionary(s => s.Kod, StringComparer.Ordinal);

    /// <summary>Znajduje stawkę po kodzie zapisanym w bazie lub w pliku XML.</summary>
    public static bool TryZKodu(string? kod, [NotNullWhen(true)] out StawkaVat? stawka)
    {
        stawka = null;
        return kod is not null && WedlugKodu.TryGetValue(kod, out stawka);
    }

    /// <summary>
    /// Zwraca stawkę o podanym kodzie albo zgłasza błąd z listą dopuszczalnych.
    /// </summary>
    public static StawkaVat ZKodu(string kod)
    {
        if (TryZKodu(kod, out StawkaVat? stawka))
        {
            return stawka;
        }

        string dozwolone = string.Join(", ", Wszystkie.Select(s => s.Kod));
        throw new ArgumentException(
            $"'{kod}' nie jest stawką dopuszczoną przez schemat FA(3). " +
            $"Dozwolone: {dozwolone}.", nameof(kod));
    }

    /// <summary>Oblicza kwotę podatku od podanej wartości netto.</summary>
    public decimal PodatekOd(decimal netto) =>
        Procent is null ? Kwoty.Zero : Kwoty.Zaokraglij(netto * Procent.Value / 100m);

    public override string ToString() => Kod;
}
