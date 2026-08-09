namespace FirmaPro.Domena;

/// <summary>
/// Reguły ustalania okresu, w którym dokument trafia do rozliczenia VAT.
/// </summary>
/// <remarks>
/// <para>
/// To najczęstsze źródło pomyłek w rejestrze: data na fakturze rzadko jest
/// tą, która decyduje o okresie. Faktura wystawiona 3 września za usługę
/// wykonaną 28 sierpnia rozlicza się w sierpniu, a nie we wrześniu.
/// </para>
/// <para>
/// Zebrane tu reguły są ogólne i obejmują typową sprzedaż oraz typowy zakup.
/// Ustawa przewiduje sporo wyjątków - media i najem (obowiązek z chwilą
/// wystawienia faktury), zaliczki (z chwilą otrzymania zapłaty), metoda
/// kasowa. Dlatego dokument może mieć <b>wprost wskazaną</b> datę ujęcia,
/// która ma pierwszeństwo przed regułą. Program nie próbuje rozpoznać
/// wyjątku sam - to decyzja księgowego, a zła podpowiedź byłaby gorsza
/// niż jej brak.
/// </para>
/// </remarks>
public static class TerminyVat
{
    /// <summary>
    /// Data, według której sprzedaż trafia do rejestru.
    /// </summary>
    /// <remarks>
    /// Obowiązek podatkowy powstaje z chwilą dokonania dostawy towaru lub
    /// wykonania usługi (art. 19a ust. 1 ustawy o VAT), a nie z chwilą
    /// wystawienia faktury. Fakturę wolno wystawić wcześniej albo później,
    /// więc to data sprzedaży decyduje o okresie.
    /// </remarks>
    /// <param name="dataWystawienia">Data z pola P_1 faktury.</param>
    /// <param name="dataSprzedazy">Data dokonania dostawy lub wykonania usługi.</param>
    /// <param name="dataUjeciaWprost">
    /// Data wskazana ręcznie - dla przypadków, w których obowiązek podatkowy
    /// powstaje inaczej niż z reguły ogólnej.
    /// </param>
    public static DateOnly DataUjeciaSprzedazy(DateOnly dataWystawienia,
                                               DateOnly? dataSprzedazy,
                                               DateOnly? dataUjeciaWprost = null) =>
        dataUjeciaWprost ?? dataSprzedazy ?? dataWystawienia;

    /// <summary>Okres, w którym sprzedaż trafia do rejestru.</summary>
    public static OkresRozliczeniowy OkresSprzedazy(DateOnly dataWystawienia,
                                                    DateOnly? dataSprzedazy,
                                                    TypOkresu typ,
                                                    DateOnly? dataUjeciaWprost = null) =>
        OkresRozliczeniowy.Dla(
            DataUjeciaSprzedazy(dataWystawienia, dataSprzedazy, dataUjeciaWprost), typ);

    /// <summary>
    /// Najwcześniejszy okres, w którym wolno odliczyć podatek z zakupu.
    /// </summary>
    /// <remarks>
    /// Prawo do odliczenia powstaje w rozliczeniu za okres, w którym po
    /// stronie sprzedawcy powstał obowiązek podatkowy (art. 86 ust. 10),
    /// ale nie wcześniej niż w okresie, w którym nabywca otrzymał fakturę
    /// (art. 86 ust. 10b pkt 1). Z dwóch dat bierzemy więc późniejszą.
    /// </remarks>
    /// <param name="dataObowiazkuUSprzedawcy">
    /// Data powstania obowiązku podatkowego u sprzedawcy - zwykle data
    /// dostawy lub wykonania usługi.
    /// </param>
    /// <param name="dataOtrzymaniaFaktury">Data wpływu faktury do nabywcy.</param>
    public static OkresRozliczeniowy NajwczesniejszyOkresOdliczenia(
        DateOnly dataObowiazkuUSprzedawcy,
        DateOnly dataOtrzymaniaFaktury,
        TypOkresu typ)
    {
        DateOnly pozniejsza = dataObowiazkuUSprzedawcy > dataOtrzymaniaFaktury
            ? dataObowiazkuUSprzedawcy
            : dataOtrzymaniaFaktury;

        return OkresRozliczeniowy.Dla(pozniejsza, typ);
    }

    /// <summary>
    /// Ostatni okres, w którym wolno jeszcze odliczyć podatek na bieżąco.
    /// </summary>
    /// <remarks>
    /// Podatnik, który nie odliczył w pierwszym możliwym okresie, może to
    /// zrobić w jednym z trzech następnych okresów przy rozliczeniu
    /// miesięcznym albo w jednym z dwóch następnych przy kwartalnym
    /// (art. 86 ust. 11). Po tym czasie zostaje już tylko korekta wsteczna.
    /// </remarks>
    public static OkresRozliczeniowy NajpozniejszyOkresOdliczenia(
        OkresRozliczeniowy najwczesniejszy)
    {
        ArgumentNullException.ThrowIfNull(najwczesniejszy);

        int ileNastepnych = najwczesniejszy.Typ == TypOkresu.Miesieczny ? 3 : 2;
        return najwczesniejszy.Przesun(ileNastepnych);
    }

    /// <summary>
    /// Sprawdza, czy zakup wolno odliczyć we wskazanym okresie.
    /// </summary>
    /// <remarks>
    /// Odliczenie zbyt wczesne to odliczenie podatku, do którego prawo
    /// jeszcze nie powstało; zbyt późne wymaga korekty deklaracji za okres
    /// wsteczny. Obie pomyłki wychodzą dopiero przy kontroli, więc lepiej
    /// zatrzymać je przy wprowadzaniu dokumentu.
    /// </remarks>
    public static WynikWalidacji SprawdzOkresOdliczenia(
        DateOnly dataObowiazkuUSprzedawcy,
        DateOnly dataOtrzymaniaFaktury,
        DateOnly dataUjecia,
        TypOkresu typ)
    {
        var wynik = new WynikWalidacji();

        OkresRozliczeniowy najwczesniejszy = NajwczesniejszyOkresOdliczenia(
            dataObowiazkuUSprzedawcy, dataOtrzymaniaFaktury, typ);

        OkresRozliczeniowy wybrany = OkresRozliczeniowy.Dla(dataUjecia, typ);
        int odleglosc = najwczesniejszy.Odleglosc(wybrany);

        if (odleglosc < 0)
        {
            wynik.Blad("DataUjecia",
                $"prawo do odliczenia powstaje dopiero w okresie {najwczesniejszy.Nazwa} " +
                "- nie da się odliczyć podatku wcześniej niż po otrzymaniu faktury");

            return wynik;
        }

        OkresRozliczeniowy najpozniejszy = NajpozniejszyOkresOdliczenia(najwczesniejszy);
        if (odleglosc > najwczesniejszy.Odleglosc(najpozniejszy))
        {
            wynik.Blad("DataUjecia",
                $"termin odliczenia na bieżąco upłynął z okresem {najpozniejszy.Nazwa} " +
                "- podatek można odzyskać już tylko korektą deklaracji");
        }

        return wynik;
    }
}
