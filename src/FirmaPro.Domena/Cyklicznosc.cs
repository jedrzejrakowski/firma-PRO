namespace FirmaPro.Domena;

/// <summary>Jak często wraca faktura wystawiana cyklicznie.</summary>
public enum RytmFaktury
{
    /// <summary>Co miesiąc.</summary>
    Miesiecznie,

    /// <summary>Co trzy miesiące.</summary>
    Kwartalnie,

    /// <summary>Co pół roku.</summary>
    Polrocznie,

    /// <summary>Raz w roku.</summary>
    Rocznie
}

/// <summary>
/// Wyliczanie dat faktur wystawianych cyklicznie.
/// </summary>
/// <remarks>
/// Rachunek dat wygląda na prosty do chwili, gdy ktoś ustawi wystawianie na
/// 31. dzień miesiąca. Luty go nie ma, kwiecień też nie - a faktura ma się
/// wtedy pojawić, nie zniknąć. Dlatego dzień wystawienia jest przycinany do
/// długości miesiąca, a nie przenoszony na początek następnego.
/// </remarks>
public static class Cyklicznosc
{
    /// <summary>Umowny numer dnia oznaczający ostatni dzień miesiąca.</summary>
    /// <remarks>
    /// Abonamenty rozlicza się zwykle albo pierwszego, albo ostatniego dnia
    /// okresu. „Ostatni” musi być osobnym pojęciem, bo raz oznacza 28, raz 31.
    /// </remarks>
    public const int OstatniDzien = 0;

    /// <summary>O ile miesięcy przesuwa się data przy danym rytmie.</summary>
    public static int IleMiesiecy(RytmFaktury rytm) => rytm switch
    {
        RytmFaktury.Miesiecznie => 1,
        RytmFaktury.Kwartalnie => 3,
        RytmFaktury.Polrocznie => 6,
        RytmFaktury.Rocznie => 12,
        _ => 1
    };

    public static string Opis(RytmFaktury rytm) => rytm switch
    {
        RytmFaktury.Miesiecznie => "co miesiąc",
        RytmFaktury.Kwartalnie => "co kwartał",
        RytmFaktury.Polrocznie => "co pół roku",
        RytmFaktury.Rocznie => "co rok",
        _ => rytm.ToString()
    };

    /// <summary>
    /// Pierwsza data wystawienia, licząc od dnia rozpoczęcia.
    /// </summary>
    /// <remarks>
    /// Gdy wskazany dzień miesiąca już minął, pierwsza faktura wypada
    /// w kolejnym okresie - wzorzec założony 20. na dzień 5. nie wystawia
    /// wstecz faktury za bieżący miesiąc.
    /// </remarks>
    public static DateOnly PierwszaData(DateOnly od, int dzienMiesiaca, RytmFaktury rytm)
    {
        DateOnly wTymMiesiacu = WMiesiacu(od.Year, od.Month, dzienMiesiaca);

        return wTymMiesiacu >= od
            ? wTymMiesiacu
            : Nastepna(wTymMiesiacu, dzienMiesiaca, rytm);
    }

    /// <summary>Kolejna data wystawienia po wskazanej.</summary>
    public static DateOnly Nastepna(DateOnly poprzednia, int dzienMiesiaca, RytmFaktury rytm)
    {
        DateOnly przesunieta = poprzednia.AddMonths(IleMiesiecy(rytm));

        return WMiesiacu(przesunieta.Year, przesunieta.Month, dzienMiesiaca);
    }

    /// <summary>
    /// Ile faktur z tego wzorca powinno już być wystawionych.
    /// </summary>
    /// <remarks>
    /// Program nie wystawia niczego sam, więc po dłuższej przerwie w pracy
    /// zaległości potrafi być kilka. Liczba mówi użytkownikowi, ile razy
    /// jeszcze będzie musiał kliknąć - milczenie na ten temat wyglądałoby
    /// na zgubione faktury.
    /// </remarks>
    public static int IleZaleglych(DateOnly nastepna, DateOnly dzisiaj,
                                   int dzienMiesiaca, RytmFaktury rytm, DateOnly? doKiedy)
    {
        int ile = 0;
        DateOnly biezaca = nastepna;

        // Ograniczenie licznika jest zabezpieczeniem przed wzorcem z datą
        // sprzed lat - lista zaległości i tak nie ma sensu powyżej kilkunastu.
        while (biezaca <= dzisiaj && (doKiedy is null || biezaca <= doKiedy) && ile < 60)
        {
            ile++;
            biezaca = Nastepna(biezaca, dzienMiesiaca, rytm);
        }

        return ile;
    }

    /// <summary>
    /// Data w danym miesiącu, przycięta do jego długości.
    /// </summary>
    private static DateOnly WMiesiacu(int rok, int miesiac, int dzienMiesiaca)
    {
        int dlugosc = DateTime.DaysInMonth(rok, miesiac);

        int dzien = dzienMiesiaca == OstatniDzien
            ? dlugosc
            : Math.Min(dzienMiesiaca, dlugosc);

        return new DateOnly(rok, miesiac, dzien);
    }
}
