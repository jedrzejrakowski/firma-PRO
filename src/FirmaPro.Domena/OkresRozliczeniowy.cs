using System.Globalization;

namespace FirmaPro.Domena;

/// <summary>Rytm, w jakim firma rozlicza się z podatku.</summary>
public enum TypOkresu
{
    /// <summary>Rozliczenie miesięczne - domyślne dla większości firm.</summary>
    Miesieczny = 0,

    /// <summary>Rozliczenie kwartalne - dostępne dla małych podatników.</summary>
    Kwartalny = 1
}

/// <summary>
/// Okres, za który rozlicza się podatek.
/// </summary>
/// <remarks>
/// <para>
/// Rejestr VAT, deklaracja i przelew do urzędu odnoszą się zawsze do okresu,
/// a nie do przedziału dat wybranego z kalendarza. Osobny typ pilnuje, żeby
/// nie dało się zbudować rejestru „od 5 marca do 12 kwietnia" - taki zakres
/// nie ma sensu podatkowego, a wynikające z niego kwoty nie zgadzałyby się
/// z niczym, co trafia do urzędu.
/// </para>
/// <para>
/// Numer oznacza miesiąc (1-12) albo kwartał (1-4), zależnie od typu.
/// </para>
/// </remarks>
public sealed record OkresRozliczeniowy
{
    private static readonly string[] NazwyMiesiecy =
    [
        "styczeń", "luty", "marzec", "kwiecień", "maj", "czerwiec",
        "lipiec", "sierpień", "wrzesień", "październik", "listopad", "grudzień"
    ];

    private static readonly string[] NazwyKwartalow = ["I", "II", "III", "IV"];

    public OkresRozliczeniowy(int rok, int numer, TypOkresu typ)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rok, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rok, 2200);
        ArgumentOutOfRangeException.ThrowIfLessThan(numer, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            numer, typ == TypOkresu.Miesieczny ? 12 : 4);

        Rok = rok;
        Numer = numer;
        Typ = typ;
    }

    public int Rok { get; }

    /// <summary>Miesiąc (1-12) albo kwartał (1-4).</summary>
    public int Numer { get; }

    public TypOkresu Typ { get; }

    public static OkresRozliczeniowy Miesiac(int rok, int miesiac) =>
        new(rok, miesiac, TypOkresu.Miesieczny);

    public static OkresRozliczeniowy Kwartal(int rok, int kwartal) =>
        new(rok, kwartal, TypOkresu.Kwartalny);

    /// <summary>Okres, w którym mieści się podana data.</summary>
    public static OkresRozliczeniowy Dla(DateOnly data, TypOkresu typ) =>
        typ == TypOkresu.Miesieczny
            ? Miesiac(data.Year, data.Month)
            : Kwartal(data.Year, ((data.Month - 1) / 3) + 1);

    public DateOnly PierwszyDzien => Typ == TypOkresu.Miesieczny
        ? new DateOnly(Rok, Numer, 1)
        : new DateOnly(Rok, ((Numer - 1) * 3) + 1, 1);

    public DateOnly OstatniDzien => Nastepny.PierwszyDzien.AddDays(-1);

    public bool Zawiera(DateOnly data) =>
        data >= PierwszyDzien && data <= OstatniDzien;

    public OkresRozliczeniowy Nastepny
    {
        get
        {
            int maks = Typ == TypOkresu.Miesieczny ? 12 : 4;
            return Numer < maks
                ? new OkresRozliczeniowy(Rok, Numer + 1, Typ)
                : new OkresRozliczeniowy(Rok + 1, 1, Typ);
        }
    }

    public OkresRozliczeniowy Poprzedni
    {
        get
        {
            int maks = Typ == TypOkresu.Miesieczny ? 12 : 4;
            return Numer > 1
                ? new OkresRozliczeniowy(Rok, Numer - 1, Typ)
                : new OkresRozliczeniowy(Rok - 1, maks, Typ);
        }
    }

    /// <summary>Przesuwa okres o podaną liczbę okresów (może być ujemna).</summary>
    public OkresRozliczeniowy Przesun(int oIle)
    {
        OkresRozliczeniowy okres = this;

        for (int i = 0; i < Math.Abs(oIle); i++)
        {
            okres = oIle > 0 ? okres.Nastepny : okres.Poprzedni;
        }

        return okres;
    }

    /// <summary>
    /// Ile okresów dzieli ten okres od podanego.
    /// </summary>
    /// <remarks>
    /// Wynik dodatni oznacza, że podany okres jest późniejszy. Porównywać
    /// można wyłącznie okresy tego samego rytmu - miesiąc i kwartał to różne
    /// jednostki i ich odejmowanie nie miałoby sensu.
    /// </remarks>
    public int Odleglosc(OkresRozliczeniowy inny)
    {
        ArgumentNullException.ThrowIfNull(inny);

        if (inny.Typ != Typ)
        {
            throw new ArgumentException(
                "Nie da się porównać okresu miesięcznego z kwartalnym.", nameof(inny));
        }

        int naRok = Typ == TypOkresu.Miesieczny ? 12 : 4;
        return ((inny.Rok - Rok) * naRok) + (inny.Numer - Numer);
    }

    /// <summary>Nazwa do pokazania użytkownikowi, np. „sierpień 2026".</summary>
    public string Nazwa => Typ == TypOkresu.Miesieczny
        ? $"{NazwyMiesiecy[Numer - 1]} {Rok.ToString(CultureInfo.InvariantCulture)}"
        : $"{NazwyKwartalow[Numer - 1]} kwartał {Rok.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Zapis nadający się do adresu strony i do sortowania, np. „2026-08".
    /// </summary>
    public string Kod => Typ == TypOkresu.Miesieczny
        ? string.Create(CultureInfo.InvariantCulture, $"{Rok:0000}-{Numer:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{Rok:0000}-K{Numer}");

    /// <summary>Odczytuje okres z zapisu zwróconego przez <see cref="Kod"/>.</summary>
    public static bool TryZKodu(string? kod, out OkresRozliczeniowy? okres)
    {
        okres = null;

        if (kod is null || kod.Length < 7 || kod[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(kod.AsSpan(0, 4), CultureInfo.InvariantCulture, out int rok))
        {
            return false;
        }

        ReadOnlySpan<char> reszta = kod.AsSpan(5);
        bool kwartalny = reszta[0] is 'K' or 'k';

        if (!int.TryParse(kwartalny ? reszta[1..] : reszta,
                          CultureInfo.InvariantCulture, out int numer))
        {
            return false;
        }

        TypOkresu typ = kwartalny ? TypOkresu.Kwartalny : TypOkresu.Miesieczny;
        if (numer < 1 || numer > (kwartalny ? 4 : 12) || rok is < 2000 or > 2200)
        {
            return false;
        }

        okres = new OkresRozliczeniowy(rok, numer, typ);
        return true;
    }

    public override string ToString() => Nazwa;
}
