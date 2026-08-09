namespace FirmaPro.Domena;

/// <summary>
/// Rodzaj nabycia - rozstrzyga, w której pozycji deklaracji trafi zakup.
/// </summary>
/// <remarks>
/// Nabycie środków trwałych wykazuje się osobno od pozostałych zakupów.
/// Podział jest wymagany w JPK_V7, więc rejestr musi go nieść od początku -
/// dokładanie go później oznaczałoby przeglądanie wszystkich dokumentów
/// wstecz i zgadywanie, co czym było.
/// </remarks>
public enum RodzajZakupu
{
    /// <summary>Nabycie towarów i usług pozostałych.</summary>
    TowaryIUslugi = 0,

    /// <summary>Nabycie towarów i usług zaliczanych do środków trwałych.</summary>
    SrodkiTrwale = 1
}

/// <summary>Kwoty w jednej stawce podatku.</summary>
public sealed record KwotyWStawce(StawkaVat Stawka, decimal Netto, decimal Vat)
{
    public decimal Brutto => Netto + Vat;
}

/// <summary>Pozycja rejestru sprzedaży - jedna faktura.</summary>
/// <param name="DataUjecia">Data decydująca o okresie rozliczenia.</param>
public sealed record WpisSprzedazy(
    string Numer,
    DateOnly DataWystawienia,
    DateOnly DataUjecia,
    string NabywcaNazwa,
    string? NabywcaNip,
    string? NumerKsef,
    IReadOnlyList<KwotyWStawce> WedlugStawek)
{
    public decimal RazemNetto => WedlugStawek.Sum(k => k.Netto);
    public decimal RazemVat => WedlugStawek.Sum(k => k.Vat);
    public decimal RazemBrutto => RazemNetto + RazemVat;
}

/// <summary>Pozycja rejestru zakupów - jedna faktura otrzymana.</summary>
/// <param name="DataUjecia">Okres, w którym odliczany jest podatek.</param>
/// <param name="Odliczany">
/// Czy podatek z tej faktury w ogóle podlega odliczeniu. Zakup służący
/// sprzedaży zwolnionej albo celom prywatnym trafia do rejestru, ale
/// podatku z niego się nie odlicza.
/// </param>
public sealed record WpisZakupu(
    string Numer,
    DateOnly DataWystawienia,
    DateOnly DataWplywu,
    DateOnly DataUjecia,
    string SprzedawcaNazwa,
    string? SprzedawcaNip,
    RodzajZakupu Rodzaj,
    bool Odliczany,
    decimal Netto,
    decimal Vat)
{
    public decimal Brutto => Netto + Vat;

    /// <summary>Kwota podatku faktycznie pomniejszająca podatek należny.</summary>
    public decimal VatDoOdliczenia => Odliczany ? Vat : Kwoty.Zero;
}

/// <summary>Rejestr sprzedaży za jeden okres.</summary>
public sealed record RejestrSprzedazy(
    OkresRozliczeniowy Okres,
    IReadOnlyList<WpisSprzedazy> Wpisy)
{
    /// <summary>Sumy w rozbiciu na stawki - w kolejności ze schematu FA(3).</summary>
    public IReadOnlyList<KwotyWStawce> WedlugStawek { get; } =
        Wpisy.SelectMany(w => w.WedlugStawek)
             .GroupBy(k => k.Stawka)
             .Select(g => new KwotyWStawce(g.Key, g.Sum(k => k.Netto), g.Sum(k => k.Vat)))
             .OrderBy(k => k.Stawka.Kolejnosc)
             .ToList();

    public decimal RazemNetto => Wpisy.Sum(w => w.RazemNetto);

    /// <summary>Podatek należny za okres.</summary>
    public decimal PodatekNalezny => Wpisy.Sum(w => w.RazemVat);

    public decimal RazemBrutto => RazemNetto + PodatekNalezny;
}

/// <summary>Rejestr zakupów za jeden okres.</summary>
public sealed record RejestrZakupow(
    OkresRozliczeniowy Okres,
    IReadOnlyList<WpisZakupu> Wpisy)
{
    public decimal RazemNetto => Wpisy.Sum(w => w.Netto);
    public decimal RazemVat => Wpisy.Sum(w => w.Vat);

    /// <summary>Podatek naliczony do odliczenia w tym okresie.</summary>
    public decimal PodatekNaliczony => Wpisy.Sum(w => w.VatDoOdliczenia);

    /// <summary>
    /// Kwoty w podziale wymaganym przez deklarację.
    /// </summary>
    /// <remarks>
    /// Liczą się tu wyłącznie nabycia dające prawo do odliczenia. Zakup
    /// służący sprzedaży zwolnionej zostaje w rejestrze jako dokument, ale
    /// nie wchodzi ani do kwoty netto, ani do podatku wykazywanego
    /// w deklaracji - wykazanie samego netto bez podatku dawałoby zestawienie,
    /// w którym jedna kolumna nie pasuje do drugiej.
    /// </remarks>
    public decimal NettoSrodkiTrwale => NettoWedlugRodzaju(RodzajZakupu.SrodkiTrwale);

    public decimal VatSrodkiTrwale => VatWedlugRodzaju(RodzajZakupu.SrodkiTrwale);

    public decimal NettoPozostale => NettoWedlugRodzaju(RodzajZakupu.TowaryIUslugi);

    public decimal VatPozostale => VatWedlugRodzaju(RodzajZakupu.TowaryIUslugi);

    private decimal NettoWedlugRodzaju(RodzajZakupu rodzaj) =>
        Wpisy.Where(w => w.Odliczany && w.Rodzaj == rodzaj).Sum(w => w.Netto);

    private decimal VatWedlugRodzaju(RodzajZakupu rodzaj) =>
        Wpisy.Where(w => w.Odliczany && w.Rodzaj == rodzaj).Sum(w => w.VatDoOdliczenia);
}

/// <summary>
/// Rozliczenie podatku za okres.
/// </summary>
/// <remarks>
/// Rejestr pokazuje wyłącznie wynik tego jednego okresu. Nadwyżka podatku
/// przeniesiona z poprzednich miesięcy nie należy do rejestru, tylko do
/// deklaracji - i tam zostanie uwzględniona. Mieszanie obu rzeczy dawałoby
/// kwotę, która nie zgadza się ani z rejestrem, ani z przelewem do urzędu.
/// </remarks>
public sealed record RozliczenieOkresu(
    OkresRozliczeniowy Okres,
    decimal PodatekNalezny,
    decimal PodatekNaliczony)
{
    /// <summary>Różnica podatku - dodatnia oznacza kwotę do zapłaty.</summary>
    public decimal Roznica => PodatekNalezny - PodatekNaliczony;

    public decimal DoZaplaty => Roznica > 0 ? Roznica : Kwoty.Zero;

    /// <summary>Nadwyżka podatku naliczonego nad należnym.</summary>
    public decimal Nadwyzka => Roznica < 0 ? -Roznica : Kwoty.Zero;
}

/// <summary>Komplet rejestrów za jeden okres.</summary>
public sealed record RejestrVat(RejestrSprzedazy Sprzedaz, RejestrZakupow Zakupy)
{
    public OkresRozliczeniowy Okres => Sprzedaz.Okres;

    public RozliczenieOkresu Rozliczenie => new(
        Okres, Sprzedaz.PodatekNalezny, Zakupy.PodatekNaliczony);

    /// <summary>
    /// Składa rejestr z dokumentów mieszczących się w okresie.
    /// </summary>
    /// <remarks>
    /// Wybór dokumentów idzie po dacie ujęcia, nie po dacie wystawienia.
    /// Faktura wystawiona 3 września za usługę wykonaną 28 sierpnia należy
    /// do sierpnia - i tak samo trafia do deklaracji.
    /// </remarks>
    public static RejestrVat Zbuduj(OkresRozliczeniowy okres,
                                    IEnumerable<WpisSprzedazy> sprzedaz,
                                    IEnumerable<WpisZakupu> zakupy)
    {
        ArgumentNullException.ThrowIfNull(okres);
        ArgumentNullException.ThrowIfNull(sprzedaz);
        ArgumentNullException.ThrowIfNull(zakupy);

        List<WpisSprzedazy> wpisySprzedazy = sprzedaz
            .Where(w => okres.Zawiera(w.DataUjecia))
            .OrderBy(w => w.DataUjecia)
            .ThenBy(w => w.Numer, StringComparer.Ordinal)
            .ToList();

        List<WpisZakupu> wpisyZakupow = zakupy
            .Where(w => okres.Zawiera(w.DataUjecia))
            .OrderBy(w => w.DataUjecia)
            .ThenBy(w => w.Numer, StringComparer.Ordinal)
            .ToList();

        return new RejestrVat(
            new RejestrSprzedazy(okres, wpisySprzedazy),
            new RejestrZakupow(okres, wpisyZakupow));
    }
}
