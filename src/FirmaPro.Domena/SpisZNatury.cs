namespace FirmaPro.Domena;

/// <summary>
/// Sposób wyceny pozycji spisu z natury.
/// </summary>
/// <remarks>
/// Rozporządzenie w sprawie prowadzenia księgi wycenia różne rzeczy różnie
/// (§ 26): towary i materiały według cen zakupu albo cen rynkowych, jeśli te
/// są niższe; wyroby własne według kosztu wytworzenia; odpady według wartości
/// oszacowanej. Sposób wyceny zapisujemy przy pozycji, bo przy kontroli trzeba
/// umieć powiedzieć, skąd wzięła się kwota.
/// </remarks>
public enum SposobWyceny
{
    /// <summary>Cena zakupu albo nabycia - towary handlowe i materiały.</summary>
    CenaZakupu = 0,

    /// <summary>Cena rynkowa - gdy jest niższa od ceny zakupu.</summary>
    CenaRynkowa = 1,

    /// <summary>Koszt wytworzenia - półwyroby, wyroby gotowe, braki.</summary>
    KosztWytworzenia = 2,

    /// <summary>Wartość oszacowana - odpady użytkowe.</summary>
    Oszacowanie = 3
}

/// <summary>Jedna pozycja spisu z natury.</summary>
/// <param name="Nazwa">Nazwa towaru, materiału albo wyrobu.</param>
/// <param name="Jednostka">Jednostka miary.</param>
/// <param name="Ilosc">Ilość stwierdzona w spisie.</param>
/// <param name="CenaJednostkowa">Cena przyjęta do wyceny.</param>
/// <param name="Wycena">Sposób ustalenia ceny.</param>
public sealed record PozycjaSpisu(
    string Nazwa,
    string Jednostka,
    decimal Ilosc,
    decimal CenaJednostkowa,
    SposobWyceny Wycena = SposobWyceny.CenaZakupu)
{
    /// <summary>Wartość pozycji - ilość razy cena, zaokrąglona do groszy.</summary>
    public decimal Wartosc => Kwoty.Zaokraglij(Ilosc * CenaJednostkowa);
}

/// <summary>
/// Spis z natury - remanent.
/// </summary>
/// <remarks>
/// <para>
/// Sporządza się go obowiązkowo na koniec roku i na jego początek, a poza tym
/// przy rozpoczęciu działalności, jej likwidacji i zmianie wspólnika (§ 24
/// rozporządzenia). Obejmuje towary handlowe, materiały, półwyroby, produkcję
/// w toku, wyroby gotowe, braki i odpady.
/// </para>
/// <para>
/// Spis nie jest ani przychodem, ani kosztem - jest stanem. Wchodzi do
/// rozliczenia dopiero różnicą między remanentem początkowym a końcowym
/// i tylko raz w roku.
/// </para>
/// </remarks>
public sealed record SpisZNatury(DateOnly Data, IReadOnlyList<PozycjaSpisu> Pozycje)
{
    /// <summary>Łączna wartość spisu.</summary>
    public decimal Wartosc => Kwoty.Zaokraglij(Pozycje.Sum(p => p.Wartosc));

    /// <summary>Spis pusty - zero na stanie, ale sporządzony.</summary>
    /// <remarks>
    /// Firma usługowa też ma obowiązek spisu; jego wartość bywa zerowa, ale to
    /// nie to samo co brak spisu. Zero trzeba wykazać.
    /// </remarks>
    public static SpisZNatury Pusty(DateOnly data) => new(data, []);
}

/// <summary>
/// Roczne rozliczenie dochodu z uwzględnieniem remanentów.
/// </summary>
/// <remarks>
/// <para>
/// Tu leży rzecz, która myli najczęściej: <b>zakup towarów nie jest kosztem
/// w chwili zakupu</b>. Kosztem jest towar sprzedany. Różnicę między jednym
/// a drugim pokazuje właśnie spis z natury - to, co zostało na półce, wypada
/// z kosztów roku.
/// </para>
/// <para>
/// Rachunek prowadzi się dokładnie w kolejności z objaśnień do księgi:
/// do remanentu początkowego dodaje się zakupy i koszty uboczne, odejmuje
/// remanent końcowy - i dopiero to jest wartością sprzedanych towarów.
/// Do niej dochodzą wynagrodzenia i pozostałe wydatki.
/// </para>
/// <para>
/// Skutek bywa zaskakujący: rok z dużym zakupem towaru na magazyn potrafi
/// dać wysoki dochód, bo towar leży, a nie został sprzedany.
/// </para>
/// </remarks>
public sealed record RozliczenieRoczne(
    decimal Przychod,
    decimal RemanentPoczatkowy,
    decimal ZakupTowarow,
    decimal KosztyUboczne,
    decimal RemanentKoncowy,
    decimal Wynagrodzenia,
    decimal PozostaleWydatki)
{
    /// <summary>Remanent początkowy powiększony o zakupy - punkt wyjścia.</summary>
    public decimal RazemZZakupami =>
        Kwoty.Zaokraglij(RemanentPoczatkowy + ZakupTowarow + KosztyUboczne);

    /// <summary>
    /// Wartość sprzedanych towarów i zużytych materiałów.
    /// </summary>
    /// <remarks>
    /// To ona jest kosztem, a nie sama kwota zakupów. Towar, który został
    /// w magazynie, siedzi w remanencie końcowym i z kosztów wypada.
    /// </remarks>
    public decimal WartoscSprzedanychTowarow =>
        Kwoty.Zaokraglij(RazemZZakupami - RemanentKoncowy);

    /// <summary>Koszty uzyskania przychodu razem.</summary>
    public decimal KosztyUzyskania =>
        Kwoty.Zaokraglij(WartoscSprzedanychTowarow + Wynagrodzenia + PozostaleWydatki);

    /// <summary>Dochód roczny - podstawa do zeznania.</summary>
    public decimal Dochod => Kwoty.Zaokraglij(Przychod - KosztyUzyskania);

    /// <summary>
    /// O ile remanenty zmieniły dochód.
    /// </summary>
    /// <remarks>
    /// Dodatnia wartość znaczy, że towaru przybyło i dochód przez to urósł.
    /// Pokazujemy to wprost, bo bez tej liczby różnica między dochodem
    /// widocznym w miesiącach a rocznym wygląda na pomyłkę programu.
    /// </remarks>
    public decimal WplywRemanentow =>
        Kwoty.Zaokraglij(RemanentKoncowy - RemanentPoczatkowy);

    /// <summary>Czy rozliczenie w ogóle uwzględnia spisy.</summary>
    public bool MaRemanenty => RemanentPoczatkowy != 0 || RemanentKoncowy != 0;

    /// <summary>
    /// Buduje rozliczenie roczne z księgi i dwóch spisów.
    /// </summary>
    /// <param name="ksiega">Księga za ostatni okres roku - liczą się jej sumy narastające.</param>
    /// <param name="remanentPoczatkowy">Spis na początek roku; puste, gdy go nie ma.</param>
    /// <param name="remanentKoncowy">Spis na koniec roku; puste, gdy go nie ma.</param>
    public static RozliczenieRoczne Zbuduj(Kpir ksiega,
                                           SpisZNatury? remanentPoczatkowy,
                                           SpisZNatury? remanentKoncowy)
    {
        ArgumentNullException.ThrowIfNull(ksiega);

        return new RozliczenieRoczne(
            ksiega.PrzychodNarastajaco,
            remanentPoczatkowy?.Wartosc ?? 0m,
            ksiega.Narastajaco(KolumnaKpir.ZakupTowarow),
            ksiega.Narastajaco(KolumnaKpir.KosztyUboczneZakupu),
            remanentKoncowy?.Wartosc ?? 0m,
            ksiega.Narastajaco(KolumnaKpir.Wynagrodzenia),
            ksiega.Narastajaco(KolumnaKpir.PozostaleWydatki));
    }
}

/// <summary>Dobiera spisy właściwe dla roku podatkowego.</summary>
/// <remarks>
/// Remanent początkowy bywa datowany na 1 stycznia albo na 31 grudnia roku
/// poprzedniego - to ten sam stan magazynu, tylko inaczej opisany. Program
/// przyjmuje oba zapisy, bo obu używa się w praktyce.
/// </remarks>
public static class Remanenty
{
    /// <summary>Spis otwierający rok.</summary>
    public static SpisZNatury? Poczatkowy(IEnumerable<SpisZNatury> spisy, int rok)
    {
        ArgumentNullException.ThrowIfNull(spisy);

        var granica = new DateOnly(rok, 1, 1);

        return spisy
            .Where(s => s.Data <= granica)
            .OrderByDescending(s => s.Data)
            .FirstOrDefault();
    }

    /// <summary>
    /// Spis zamykający rok.
    /// </summary>
    /// <remarks>
    /// Ostatni spis roku, ale nigdy ten sam, który go otwiera - inaczej przy
    /// jednym spisie datowanym na 1 stycznia różnica remanentów wyszłaby zerem
    /// i towar z magazynu zniknąłby z rachunku.
    /// </remarks>
    public static SpisZNatury? Koncowy(IEnumerable<SpisZNatury> spisy, int rok)
    {
        ArgumentNullException.ThrowIfNull(spisy);

        SpisZNatury? poczatkowy = Poczatkowy(spisy, rok);

        return spisy
            .Where(s => s.Data.Year == rok && s.Data > (poczatkowy?.Data ?? DateOnly.MinValue))
            .OrderByDescending(s => s.Data)
            .FirstOrDefault();
    }
}
