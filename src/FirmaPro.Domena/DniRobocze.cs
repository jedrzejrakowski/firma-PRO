namespace FirmaPro.Domena;

/// <summary>
/// Dni ustawowo wolne od pracy i przesuwanie terminów.
/// </summary>
/// <remarks>
/// Terminy składkowe i podatkowe, które wypadają w sobotę, niedzielę albo
/// dzień ustawowo wolny, przesuwają się na najbliższy dzień roboczy
/// (art. 12 § 5 Ordynacji podatkowej, art. 31 ustawy o systemie ubezpieczeń
/// społecznych). Bez tego program pokazywałby termin, którego nie da się
/// dotrzymać, i straszył zaległością dzień wcześniej, niż powinien.
/// </remarks>
public static class DniRobocze
{
    /// <summary>Czy dzień jest ustawowo wolny od pracy.</summary>
    public static bool Wolny(DateOnly dzien) =>
        dzien.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
        || Swieta(dzien.Year).Contains(dzien);

    /// <summary>
    /// Termin przesunięty na najbliższy dzień roboczy.
    /// </summary>
    /// <remarks>
    /// Przesuwamy zawsze do przodu - przepis mówi o dniu następnym, a nie
    /// o najbliższym w którąkolwiek stronę. Zapłata przed terminem nigdy nie
    /// jest błędem, więc wcześniejszy dzień roboczy byłby podpowiedzią
    /// niepotrzebnie zaostrzającą obowiązek.
    /// </remarks>
    public static DateOnly NajblizszyRoboczy(DateOnly dzien)
    {
        DateOnly wynik = dzien;

        // Najdłuższy ciąg wolnego w polskim kalendarzu to kilka dni, ale
        // pętla ma twardy limit, żeby błąd w tablicy świąt nie zawiesił
        // programu w miejscu, którego nikt nie podejrzewa.
        for (int i = 0; i < 14 && Wolny(wynik); i++)
        {
            wynik = wynik.AddDays(1);
        }

        return wynik;
    }

    /// <summary>Dni ustawowo wolne w danym roku.</summary>
    /// <remarks>
    /// Stałe daty z ustawy o dniach wolnych od pracy plus trzy ruchome,
    /// liczone od Wielkanocy: poniedziałek wielkanocny, Zielone Świątki
    /// i Boże Ciało. Wielki Piątek i Wigilia dniami ustawowo wolnymi nie są,
    /// więc terminu nie przesuwają - Wigilia dopiero od 2025 roku.
    /// </remarks>
    public static IReadOnlySet<DateOnly> Swieta(int rok)
    {
        DateOnly wielkanoc = Wielkanoc(rok);

        var dni = new HashSet<DateOnly>
        {
            new(rok, 1, 1),      // Nowy Rok
            new(rok, 1, 6),      // Trzech Króli
            wielkanoc,
            wielkanoc.AddDays(1),   // poniedziałek wielkanocny
            new(rok, 5, 1),      // Święto Pracy
            new(rok, 5, 3),      // Święto Konstytucji
            wielkanoc.AddDays(49),  // Zielone Świątki
            wielkanoc.AddDays(60),  // Boże Ciało
            new(rok, 8, 15),     // Wniebowzięcie NMP
            new(rok, 11, 1),     // Wszystkich Świętych
            new(rok, 11, 11),    // Święto Niepodległości
            new(rok, 12, 25),
            new(rok, 12, 26)
        };

        // Wigilia jest dniem ustawowo wolnym dopiero od 2025 roku.
        if (rok >= 2025)
        {
            dni.Add(new DateOnly(rok, 12, 24));
        }

        return dni;
    }

    /// <summary>
    /// Niedziela wielkanocna według algorytmu Gaussa-Meeusa.
    /// </summary>
    /// <remarks>
    /// Trzy dni wolne w roku zależą od tej jednej daty, a wyznacza się ją
    /// z kalendarza księżycowego, nie z kalendarza zwykłego - stąd rachunek,
    /// który nie da się skrócić do prostszej reguły.
    /// </remarks>
    public static DateOnly Wielkanoc(int rok)
    {
        int a = rok % 19;
        int b = rok / 100;
        int c = rok % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (22 * l)) / 451;
        int miesiac = (h + l - (7 * m) + 114) / 31;
        int dzien = ((h + l - (7 * m) + 114) % 31) + 1;

        return new DateOnly(rok, miesiac, dzien);
    }
}
