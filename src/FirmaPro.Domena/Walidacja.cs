using System.Text.RegularExpressions;

namespace FirmaPro.Domena;

/// <summary>Waga zastrzeżenia zgłoszonego przy sprawdzaniu faktury.</summary>
public enum PoziomProblemu
{
    /// <summary>Faktura zostanie odrzucona albo jest niezgodna z przepisami.</summary>
    Blad,
    /// <summary>Coś wygląda podejrzanie, ale wysyłka jest możliwa.</summary>
    Ostrzezenie
}

/// <summary>Pojedyncze zastrzeżenie do faktury.</summary>
public sealed record Problem(PoziomProblemu Poziom, string Pole, string Komunikat)
{
    public override string ToString()
    {
        string poziom = Poziom == PoziomProblemu.Blad ? "błąd" : "ostrzeżenie";
        return $"[{poziom}] {Pole}: {Komunikat}";
    }
}

/// <summary>Wynik sprawdzenia faktury.</summary>
public sealed class WynikWalidacji
{
    private readonly List<Problem> _problemy = [];

    public IReadOnlyList<Problem> Problemy => _problemy;

    /// <summary>Czy jest choć jeden błąd blokujący wysyłkę.</summary>
    public bool SaBledy => _problemy.Any(p => p.Poziom == PoziomProblemu.Blad);

    /// <summary>Czy faktura przeszła bez żadnych zastrzeżeń.</summary>
    public bool BezZastrzezen => _problemy.Count == 0;

    internal void Blad(string pole, string komunikat) =>
        _problemy.Add(new Problem(PoziomProblemu.Blad, pole, komunikat));

    internal void Ostrzez(string pole, string komunikat) =>
        _problemy.Add(new Problem(PoziomProblemu.Ostrzezenie, pole, komunikat));

    /// <summary>Składa zastrzeżenia w czytelny tekst dla użytkownika.</summary>
    public string Opis() =>
        _problemy.Count == 0
            ? "Nie znaleziono zastrzeżeń."
            : string.Join(Environment.NewLine, _problemy.Select(p => $"  • {p}"));
}

/// <summary>
/// Sprawdzanie poprawności faktury przed wysłaniem do KSeF.
/// </summary>
/// <remarks>
/// Wychwytuje błędy, których nie da się wyrazić w schemacie XSD: sumy
/// kontrolne NIP i numeru rachunku, sensowność dat, limity długości pól.
/// Dzięki temu pomyłkę widać od razu w programie, a nie dopiero w odrzuconej
/// sesji KSeF.
/// </remarks>
public static class Walidator
{
    // Ograniczenia długości wynikające z typów schematu FA(3):
    // TZnakowy (256), TZnakowy512 (512), TTekstowy (3500).
    private const int MaxZnakowy = 256;
    private const int MaxZnakowy512 = 512;
    private const int MaxTekstowy = 3500;

    /// <summary>Największa kwota dopuszczona przez typ TKwotowy.</summary>
    private const decimal MaxKwota = 9999999999999999.99m;

    // Wagi cyfr NIP używane przy liczeniu sumy kontrolnej.
    private static readonly int[] WagiNip = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    private static readonly Regex TylkoCyfry = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex KodKrajuWzor = new(@"^[A-Z]{2}$", RegexOptions.Compiled);
    private static readonly Regex WalutaWzor = new(@"^[A-Z]{3}$", RegexOptions.Compiled);
    private static readonly Regex GtuWzor = new(@"^GTU_(0[1-9]|1[0-3])$", RegexOptions.Compiled);

    /// <summary>
    /// Sprawdza sumę kontrolną numeru NIP.
    /// </summary>
    /// <remarks>
    /// NIP ma 10 cyfr; ostatnia jest cyfrą kontrolną liczoną modulo 11 z wag
    /// (6,5,7,2,3,4,5,6,7). Reszta równa 10 oznacza numer niepoprawny.
    /// </remarks>
    public static bool NipPoprawny(string? nip)
    {
        string cyfry = OczyscNumer(nip);
        if (cyfry.Length != 10 || !TylkoCyfry.IsMatch(cyfry))
        {
            return false;
        }

        int suma = 0;
        for (int i = 0; i < WagiNip.Length; i++)
        {
            suma += (cyfry[i] - '0') * WagiNip[i];
        }

        int kontrolna = suma % 11;
        return kontrolna != 10 && kontrolna == cyfry[9] - '0';
    }

    /// <summary>
    /// Sprawdza sumę kontrolną polskiego numeru rachunku (NRB, 26 cyfr).
    /// </summary>
    /// <remarks>
    /// Rachunek jest poprawny, gdy liczba powstała z przeniesienia kodu kraju
    /// ("PL" -> 2521) na koniec i dopisania "00" daje resztę 1 z dzielenia
    /// przez 97 (norma ISO 13616 dla numeru IBAN). Liczba ma 32 cyfry, więc
    /// nie mieści się w żadnym typie całkowitym - resztę liczymy cyfra po
    /// cyfrze.
    /// </remarks>
    public static bool RachunekPoprawny(string? numer)
    {
        string cyfry = OczyscNumer(numer);
        if (cyfry.Length != 26 || !TylkoCyfry.IsMatch(cyfry))
        {
            return false;
        }

        int reszta = 0;
        foreach (char znak in cyfry + "252100")
        {
            reszta = (reszta * 10 + (znak - '0')) % 97;
        }

        return reszta == 1;
    }

    private static string OczyscNumer(string? numer) =>
        new((numer ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>Sprawdza fakturę i zwraca listę zastrzeżeń.</summary>
    public static WynikWalidacji SprawdzFakture(Faktura faktura)
    {
        ArgumentNullException.ThrowIfNull(faktura);
        var wynik = new WynikWalidacji();

        SprawdzNaglowek(faktura, wynik);
        SprawdzPodmiot(faktura.Sprzedawca, "Sprzedawca", nipWymagany: true, wynik);
        SprawdzPodmiot(faktura.Nabywca, "Nabywca", nipWymagany: false, wynik);
        SprawdzPozycje(faktura, wynik);
        SprawdzPlatnosc(faktura, wynik);

        if (!string.IsNullOrWhiteSpace(faktura.Sprzedawca.Nip)
            && OczyscNumer(faktura.Sprzedawca.Nip) == OczyscNumer(faktura.Nabywca.Nip)
            && !string.IsNullOrWhiteSpace(faktura.Nabywca.Nip))
        {
            wynik.Ostrzez("Nabywca / NIP", "nabywca ma ten sam NIP co sprzedawca");
        }

        if (faktura.MaSprzedazZwolniona && string.IsNullOrWhiteSpace(faktura.PodstawaZwolnienia))
        {
            wynik.Blad("Podstawa zwolnienia",
                "przy sprzedaży zwolnionej trzeba wskazać przepis, na podstawie " +
                "którego stosuje się zwolnienie");
        }

        return wynik;
    }

    private static void SprawdzNaglowek(Faktura faktura, WynikWalidacji wynik)
    {
        if (string.IsNullOrWhiteSpace(faktura.Numer))
        {
            wynik.Blad("Numer faktury", "pole jest puste");
        }

        SprawdzDlugosc(faktura.Numer, MaxZnakowy, "Numer faktury", wynik);
        SprawdzDlugosc(faktura.MiejsceWystawienia, MaxZnakowy, "Miejsce wystawienia", wynik);
        SprawdzDlugosc(faktura.Stopka, MaxTekstowy, "Stopka", wynik);
        SprawdzDlugosc(faktura.PodstawaZwolnienia, MaxZnakowy, "Podstawa zwolnienia", wynik);

        // Schemat FA(3) dopuszcza daty od 2006-01-01 do 2050-01-01.
        if (faktura.DataWystawienia == default)
        {
            wynik.Blad("Data wystawienia", "brak daty");
        }
        else
        {
            if (faktura.DataWystawienia < new DateOnly(2006, 1, 1)
                || faktura.DataWystawienia > new DateOnly(2050, 1, 1))
            {
                wynik.Blad("Data wystawienia",
                    "data poza zakresem dopuszczonym przez schemat FA(3)");
            }

            if (faktura.DataWystawienia > DateOnly.FromDateTime(DateTime.Today))
            {
                wynik.Ostrzez("Data wystawienia", "data jest z przyszłości");
            }
        }

        if (faktura.DataSprzedazy is { } sprzedaz && sprzedaz > faktura.DataWystawienia)
        {
            wynik.Ostrzez("Data sprzedaży", "jest późniejsza niż data wystawienia");
        }

        if (!WalutaWzor.IsMatch(faktura.Waluta ?? string.Empty))
        {
            wynik.Blad("Waluta",
                $"'{faktura.Waluta}' nie jest trzyliterowym kodem waluty");
        }
    }

    private static void SprawdzPodmiot(Podmiot podmiot, string etykieta,
                                       bool nipWymagany, WynikWalidacji wynik)
    {
        if (string.IsNullOrWhiteSpace(podmiot.Nazwa))
        {
            wynik.Blad($"{etykieta} / Nazwa", "pole jest puste");
        }

        if (!string.IsNullOrWhiteSpace(podmiot.Nip))
        {
            if (!NipPoprawny(podmiot.Nip))
            {
                wynik.Blad($"{etykieta} / NIP",
                    $"numer '{podmiot.Nip}' ma błędną sumę kontrolną");
            }
        }
        else if (nipWymagany)
        {
            wynik.Blad($"{etykieta} / NIP", "sprzedawca musi mieć numer NIP");
        }
        else if (string.IsNullOrWhiteSpace(podmiot.NrVatUe))
        {
            // Brak NIP u nabywcy jest dopuszczalny (osoba prywatna), ale warto
            // to potwierdzić - to częsta pomyłka przy przepisywaniu danych.
            wynik.Ostrzez($"{etykieta} / NIP",
                "brak numeru - faktura zostanie wystawiona jako dla nabywcy " +
                "bez identyfikatora podatkowego");
        }

        if (nipWymagany && string.IsNullOrWhiteSpace(podmiot.Adres.Linia1))
        {
            wynik.Blad($"{etykieta} / Adres", "brak pierwszej linii adresu");
        }

        if (!KodKrajuWzor.IsMatch(podmiot.Adres.KodKraju ?? string.Empty))
        {
            wynik.Blad($"{etykieta} / Kod kraju",
                $"'{podmiot.Adres.KodKraju}' nie jest dwuliterowym kodem kraju");
        }

        SprawdzDlugosc(podmiot.Nazwa, MaxZnakowy512, $"{etykieta} / Nazwa", wynik);
        SprawdzDlugosc(podmiot.Adres.Linia1, MaxZnakowy512, $"{etykieta} / Adres", wynik);
        SprawdzDlugosc(podmiot.Adres.Linia2, MaxZnakowy512, $"{etykieta} / Adres", wynik);
    }

    private static void SprawdzPozycje(Faktura faktura, WynikWalidacji wynik)
    {
        if (faktura.Pozycje.Count == 0)
        {
            wynik.Blad("Pozycje", "faktura nie ma ani jednej pozycji");
            return;
        }

        for (int i = 0; i < faktura.Pozycje.Count; i++)
        {
            PozycjaFaktury pozycja = faktura.Pozycje[i];
            string etykieta = $"Pozycja {i + 1}";

            if (string.IsNullOrWhiteSpace(pozycja.Nazwa))
            {
                wynik.Blad($"{etykieta} / Nazwa", "brak nazwy towaru lub usługi");
            }

            SprawdzDlugosc(pozycja.Nazwa, MaxZnakowy512, $"{etykieta} / Nazwa", wynik);

            if (pozycja.Ilosc <= 0m)
            {
                wynik.Blad($"{etykieta} / Ilość", "ilość musi być większa od zera");
            }

            if (pozycja.CenaNetto < 0m)
            {
                wynik.Ostrzez($"{etykieta} / Cena",
                    "cena jest ujemna - dopuszczalne tylko na korekcie");
            }

            if (pozycja.WartoscNetto > MaxKwota)
            {
                wynik.Blad($"{etykieta} / Wartość",
                    "kwota przekracza zakres dopuszczony przez schemat FA(3)");
            }

            if (pozycja.Gtu is not null && !GtuWzor.IsMatch(pozycja.Gtu))
            {
                wynik.Blad($"{etykieta} / GTU",
                    $"'{pozycja.Gtu}' nie jest kodem z zakresu GTU_01..GTU_13");
            }
        }

        if (faktura.Podsumowanie().RazemBrutto <= 0m)
        {
            wynik.Ostrzez("Suma faktury", "kwota należności ogółem nie jest dodatnia");
        }
    }

    private static void SprawdzPlatnosc(Faktura faktura, WynikWalidacji wynik)
    {
        WarunkiPlatnosci platnosc = faktura.Platnosc;

        if (!string.IsNullOrWhiteSpace(platnosc.Rachunek)
            && !RachunekPoprawny(platnosc.Rachunek))
        {
            wynik.Blad("Płatność / Rachunek",
                $"numer '{platnosc.Rachunek}' ma błędną sumę kontrolną " +
                "(oczekiwano 26 cyfr numeru NRB)");
        }

        if (platnosc.Termin is { } termin && termin < faktura.DataWystawienia)
        {
            wynik.Ostrzez("Płatność / Termin",
                "termin płatności jest wcześniejszy niż data wystawienia");
        }
    }

    private static void SprawdzDlugosc(string? wartosc, int maksimum, string pole,
                                       WynikWalidacji wynik)
    {
        if (!string.IsNullOrEmpty(wartosc) && wartosc.Length > maksimum)
        {
            wynik.Blad(pole,
                $"tekst ma {wartosc.Length} znaków, a schemat dopuszcza " +
                $"najwyżej {maksimum}");
        }
    }
}
