namespace FirmaPro.Domena;

/// <summary>Stan zapłaty faktury.</summary>
public enum StanZaplaty
{
    /// <summary>Nie wpłynęło nic.</summary>
    Nieoplacona,

    /// <summary>Wpłynęła część należności.</summary>
    CzesciowoOplacona,

    /// <summary>Należność uregulowana w całości.</summary>
    Oplacona,

    /// <summary>Wpłynęło więcej, niż wynosi faktura.</summary>
    Nadplacona
}

/// <summary>
/// Rozliczenie jednej faktury: ile zapłacono, ile zostało i czy w terminie.
/// </summary>
/// <remarks>
/// <para>
/// Wyliczenie siedzi w warstwie dziedziny, bo decyduje o pieniądzach, a nie
/// o wyglądzie ekranu. Ta sama reguła obowiązuje przy fakturze, na liście
/// należności i w treści przypomnienia - gdyby każdy z tych ekranów liczył
/// po swojemu, prędzej czy później pokazałyby różne kwoty.
/// </para>
/// <para>
/// Nadpłata nie jest błędem: kontrahent bywa, że zaokrągli przelew w górę
/// albo zapłaci dwa razy. Program ma to pokazać, a nie ukryć.
/// </para>
/// </remarks>
public sealed record Rozliczenie(
    decimal Brutto,
    decimal Zaplacono,
    DateOnly? TerminPlatnosci,
    DateOnly Dzisiaj)
{
    /// <summary>Kwota pozostała do zapłaty; ujemna przy nadpłacie.</summary>
    public decimal Pozostalo => Kwoty.Zaokraglij(Brutto - Zaplacono);

    public StanZaplaty Stan => Pozostalo switch
    {
        < 0 => StanZaplaty.Nadplacona,
        0 => StanZaplaty.Oplacona,
        _ when Zaplacono > 0 => StanZaplaty.CzesciowoOplacona,
        _ => StanZaplaty.Nieoplacona
    };

    /// <summary>Czy termin minął, a należność wciąż nie jest uregulowana.</summary>
    public bool PoTerminie =>
        Pozostalo > 0 && TerminPlatnosci is DateOnly termin && termin < Dzisiaj;

    /// <summary>
    /// Ile dni minęło od terminu; zero, gdy termin jeszcze nie minął.
    /// </summary>
    /// <remarks>
    /// Dzień terminu jeszcze się liczy - zapłata w ostatnim dniu jest zapłatą
    /// w terminie, więc opóźnienie zaczyna się dopiero nazajutrz.
    /// </remarks>
    public int DniPoTerminie =>
        PoTerminie ? Dzisiaj.DayNumber - TerminPlatnosci!.Value.DayNumber : 0;

    /// <summary>Opis stanu dla człowieka.</summary>
    public string Opis => Stan switch
    {
        StanZaplaty.Oplacona => "zapłacona",
        StanZaplaty.Nadplacona => "nadpłacona",
        StanZaplaty.CzesciowoOplacona when PoTerminie => "częściowo, po terminie",
        StanZaplaty.CzesciowoOplacona => "częściowo zapłacona",
        _ when PoTerminie => "po terminie",
        _ => "niezapłacona"
    };
}
