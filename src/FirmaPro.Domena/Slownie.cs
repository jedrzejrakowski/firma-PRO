using System.Globalization;

namespace FirmaPro.Domena;

/// <summary>
/// Zapis kwoty słownie po polsku.
/// </summary>
/// <remarks>
/// Na fakturach zwyczajowo podaje się kwotę należności także słowami - to
/// utrudnia podrobienie dokumentu i ułatwia kontrolę. Odmiana rzeczowników
/// idzie za polskimi regułami liczby mnogiej: 1 złoty, 2 złote, 5 złotych,
/// przy czym 12-14 zachowuje się jak 5, a nie jak 2-4.
/// </remarks>
public static class Slownie
{
    private static readonly string[] Jednosci =
    [
        "", "jeden", "dwa", "trzy", "cztery", "pięć", "sześć", "siedem",
        "osiem", "dziewięć"
    ];

    private static readonly string[] Nastki =
    [
        "dziesięć", "jedenaście", "dwanaście", "trzynaście", "czternaście",
        "piętnaście", "szesnaście", "siedemnaście", "osiemnaście",
        "dziewiętnaście"
    ];

    private static readonly string[] Dziesiatki =
    [
        "", "", "dwadzieścia", "trzydzieści", "czterdzieści", "pięćdziesiąt",
        "sześćdziesiąt", "siedemdziesiąt", "osiemdziesiąt", "dziewięćdziesiąt"
    ];

    private static readonly string[] Setki =
    [
        "", "sto", "dwieście", "trzysta", "czterysta", "pięćset", "sześćset",
        "siedemset", "osiemset", "dziewięćset"
    ];

    /// <summary>
    /// Nazwa rzeczownika w trzech formach wymaganych przez polską odmianę.
    /// </summary>
    /// <param name="Pojedyncza">Forma dla liczby 1.</param>
    /// <param name="Mnoga">Forma dla liczb zakończonych na 2-4.</param>
    /// <param name="Dopelniacz">Forma dla pozostałych liczb.</param>
    private sealed record Formy(string Pojedyncza, string Mnoga, string Dopelniacz);

    // Rzędy wielkości. Pusty wpis na pozycji zerowej to same jedności -
    // nie dopisuje się do nich żadnej nazwy.
    private static readonly Formy[] Rzedy =
    [
        new("", "", ""),
        new("tysiąc", "tysiące", "tysięcy"),
        new("milion", "miliony", "milionów"),
        new("miliard", "miliardy", "miliardów"),
        new("bilion", "biliony", "bilionów")
    ];

    private static readonly Dictionary<string, Formy> Waluty =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PLN"] = new("złoty", "złote", "złotych"),
            ["EUR"] = new("euro", "euro", "euro"),
            ["USD"] = new("dolar", "dolary", "dolarów"),
            ["GBP"] = new("funt", "funty", "funtów"),
            ["CHF"] = new("frank", "franki", "franków"),
            ["CZK"] = new("korona", "korony", "koron")
        };

    /// <summary>Największa liczba, którą da się zapisać słowami.</summary>
    private const long Granica = 1_000_000_000_000_000L;

    /// <summary>
    /// Zapisuje kwotę słownie, np. „cztery tysiące jeden złotych 00/100".
    /// </summary>
    /// <remarks>
    /// Grosze podawane są cyfrowo, w postaci ułamka - tak przyjęło się na
    /// polskich fakturach i tak jest czytelniej niż „siedem groszy".
    /// </remarks>
    public static string Kwota(decimal kwota, string waluta = "PLN")
    {
        decimal zaokraglona = Kwoty.Zaokraglij(kwota);
        bool ujemna = zaokraglona < 0;
        zaokraglona = Math.Abs(zaokraglona);

        long calosc = (long)decimal.Truncate(zaokraglona);
        int grosze = (int)((zaokraglona - calosc) * 100);

        string tekst = Liczba(calosc);

        if (Waluty.TryGetValue(waluta ?? string.Empty, out Formy? formy))
        {
            tekst += " " + Forma(calosc, formy);
        }
        else
        {
            // Waluta spoza słownika - podajemy jej kod zamiast zgadywać odmianę.
            tekst += " " + (waluta ?? string.Empty).ToUpperInvariant();
        }

        tekst += " " + grosze.ToString("00", CultureInfo.InvariantCulture) + "/100";

        return ujemna ? "minus " + tekst : tekst;
    }

    /// <summary>Zapisuje słowami nieujemną liczbę całkowitą.</summary>
    public static string Liczba(long liczba)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(liczba);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(liczba, Granica);

        if (liczba == 0)
        {
            return "zero";
        }

        // Dzielimy liczbę na trzycyfrowe grupy, od najmniej znaczącej.
        var grupy = new List<int>();
        long pozostalo = liczba;
        while (pozostalo > 0)
        {
            grupy.Add((int)(pozostalo % 1000));
            pozostalo /= 1000;
        }

        var slowa = new List<string>();
        for (int indeks = grupy.Count - 1; indeks >= 0; indeks--)
        {
            int grupa = grupy[indeks];
            if (grupa == 0)
            {
                continue;
            }

            // „jeden tysiąc" brzmi sztucznie - mówi się po prostu „tysiąc".
            if (!(indeks > 0 && grupa == 1))
            {
                slowa.AddRange(GrupaSlownie(grupa));
            }

            if (indeks > 0)
            {
                slowa.Add(Forma(grupa, Rzedy[indeks]));
            }
        }

        return string.Join(' ', slowa.Where(s => s.Length > 0));
    }

    /// <summary>Zapisuje słowami liczbę z zakresu 1-999.</summary>
    private static List<string> GrupaSlownie(int liczba)
    {
        var slowa = new List<string>();

        (int setki, int reszta) = Math.DivRem(liczba, 100);
        if (setki > 0)
        {
            slowa.Add(Setki[setki]);
        }

        if (reszta is >= 10 and <= 19)
        {
            slowa.Add(Nastki[reszta - 10]);
            return slowa;
        }

        (int dziesiatki, int jednosci) = Math.DivRem(reszta, 10);
        if (dziesiatki > 0)
        {
            slowa.Add(Dziesiatki[dziesiatki]);
        }

        if (jednosci > 0)
        {
            slowa.Add(Jednosci[jednosci]);
        }

        return slowa;
    }

    /// <summary>
    /// Dobiera formę rzeczownika do liczby.
    /// </summary>
    /// <remarks>
    /// Reguła nie jest tak prosta jak „końcówka 2-4": dwanaście, trzynaście
    /// i czternaście wymagają dopełniacza (dwanaście złotych), mimo że
    /// kończą się na 2, 3 i 4.
    /// </remarks>
    private static string Forma(long liczba, Formy formy)
    {
        if (liczba == 1)
        {
            return formy.Pojedyncza;
        }

        long resztaDziesiatek = liczba % 10;
        long resztaSetek = liczba % 100;

        return resztaDziesiatek is >= 2 and <= 4 && resztaSetek is < 12 or > 14
            ? formy.Mnoga
            : formy.Dopelniacz;
    }
}
