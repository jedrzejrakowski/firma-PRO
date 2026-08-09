using FirmaPro.Domena;

namespace FirmaPro.Testy;

/// <summary>Okresy rozliczeniowe i reguły ustalania okresu dokumentu.</summary>
public sealed class TestyOkresow
{
    [Theory]
    [InlineData(2026, 1, 1, 31)]
    [InlineData(2026, 2, 1, 28)]
    [InlineData(2028, 2, 1, 29)]
    [InlineData(2026, 12, 1, 31)]
    public void MiesiacObejmujeWlasciweDni(int rok, int miesiac, int pierwszy, int ostatni)
    {
        OkresRozliczeniowy okres = OkresRozliczeniowy.Miesiac(rok, miesiac);

        Assert.Equal(new DateOnly(rok, miesiac, pierwszy), okres.PierwszyDzien);
        Assert.Equal(new DateOnly(rok, miesiac, ostatni), okres.OstatniDzien);
    }

    [Theory]
    [InlineData(1, 1, 1, 3, 31)]
    [InlineData(2, 4, 1, 6, 30)]
    [InlineData(3, 7, 1, 9, 30)]
    [InlineData(4, 10, 1, 12, 31)]
    public void KwartalObejmujeTrzyMiesiace(int kwartal, int miesiacOd, int dzienOd,
                                            int miesiacDo, int dzienDo)
    {
        OkresRozliczeniowy okres = OkresRozliczeniowy.Kwartal(2026, kwartal);

        Assert.Equal(new DateOnly(2026, miesiacOd, dzienOd), okres.PierwszyDzien);
        Assert.Equal(new DateOnly(2026, miesiacDo, dzienDo), okres.OstatniDzien);
    }

    [Fact]
    public void NastepnyOkresPrzechodziPrzezKoniecRoku()
    {
        Assert.Equal(OkresRozliczeniowy.Miesiac(2027, 1),
                     OkresRozliczeniowy.Miesiac(2026, 12).Nastepny);

        Assert.Equal(OkresRozliczeniowy.Kwartal(2027, 1),
                     OkresRozliczeniowy.Kwartal(2026, 4).Nastepny);

        Assert.Equal(OkresRozliczeniowy.Miesiac(2025, 12),
                     OkresRozliczeniowy.Miesiac(2026, 1).Poprzedni);
    }

    [Fact]
    public void OdlegloscLiczySieWObuKierunkach()
    {
        OkresRozliczeniowy sierpien = OkresRozliczeniowy.Miesiac(2026, 8);

        Assert.Equal(3, sierpien.Odleglosc(OkresRozliczeniowy.Miesiac(2026, 11)));
        Assert.Equal(-2, sierpien.Odleglosc(OkresRozliczeniowy.Miesiac(2026, 6)));
        Assert.Equal(5, sierpien.Odleglosc(OkresRozliczeniowy.Miesiac(2027, 1)));
    }

    /// <summary>Miesiąca i kwartału nie da się porównywać.</summary>
    [Fact]
    public void PorownanieRoznychRytmowJestOdrzucane() =>
        Assert.Throws<ArgumentException>(() =>
            OkresRozliczeniowy.Miesiac(2026, 8)
                .Odleglosc(OkresRozliczeniowy.Kwartal(2026, 3)));

    [Theory]
    [InlineData("2026-08", 2026, 8, TypOkresu.Miesieczny)]
    [InlineData("2026-01", 2026, 1, TypOkresu.Miesieczny)]
    [InlineData("2026-K3", 2026, 3, TypOkresu.Kwartalny)]
    public void KodDaSieOdczytacZPowrotem(string kod, int rok, int numer, TypOkresu typ)
    {
        Assert.True(OkresRozliczeniowy.TryZKodu(kod, out OkresRozliczeniowy? okres));

        Assert.Equal(rok, okres!.Rok);
        Assert.Equal(numer, okres.Numer);
        Assert.Equal(typ, okres.Typ);
        Assert.Equal(kod, okres.Kod);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("2026-13")]
    [InlineData("2026-K5")]
    [InlineData("1999-01")]
    [InlineData("abcd-ef")]
    public void BledneKodyOkresowSaOdrzucane(string? kod) =>
        Assert.False(OkresRozliczeniowy.TryZKodu(kod, out _));

    /// <summary>
    /// O okresie decyduje data sprzedaży, nie data wystawienia faktury.
    /// </summary>
    /// <remarks>
    /// Najczęstsza pomyłka w rejestrze: usługa wykonana pod koniec miesiąca,
    /// faktura wystawiona na początku następnego. Dokument należy do miesiąca
    /// wykonania usługi.
    /// </remarks>
    [Fact]
    public void SprzedazTrafiaDoOkresuWykonaniaUslugi()
    {
        OkresRozliczeniowy okres = TerminyVat.OkresSprzedazy(
            dataWystawienia: new DateOnly(2026, 9, 3),
            dataSprzedazy: new DateOnly(2026, 8, 28),
            TypOkresu.Miesieczny);

        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 8), okres);
    }

    [Fact]
    public void BezDatySprzedazyLiczySieDataWystawienia()
    {
        OkresRozliczeniowy okres = TerminyVat.OkresSprzedazy(
            new DateOnly(2026, 9, 3), dataSprzedazy: null, TypOkresu.Miesieczny);

        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 9), okres);
    }

    /// <summary>
    /// Data wskazana wprost bije regułę ogólną.
    /// </summary>
    /// <remarks>
    /// Przy mediach i najmie obowiązek podatkowy powstaje z chwilą wystawienia
    /// faktury, a nie wykonania usługi. Program nie rozpoznaje takich
    /// przypadków sam - pozwala je wskazać.
    /// </remarks>
    [Fact]
    public void DataWskazanaWprostMaPierwszenstwo()
    {
        OkresRozliczeniowy okres = TerminyVat.OkresSprzedazy(
            new DateOnly(2026, 9, 3), new DateOnly(2026, 8, 28), TypOkresu.Miesieczny,
            dataUjeciaWprost: new DateOnly(2026, 9, 3));

        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 9), okres);
    }

    /// <summary>
    /// Zakupu nie da się odliczyć wcześniej niż wpłynęła faktura.
    /// </summary>
    /// <remarks>
    /// Towar dostarczony w sierpniu, faktura otrzymana we wrześniu - podatek
    /// odlicza się dopiero we wrześniu (art. 86 ust. 10b pkt 1).
    /// </remarks>
    [Fact]
    public void OdliczenieNieWczesniejNizWplywFaktury()
    {
        OkresRozliczeniowy okres = TerminyVat.NajwczesniejszyOkresOdliczenia(
            dataObowiazkuUSprzedawcy: new DateOnly(2026, 8, 20),
            dataOtrzymaniaFaktury: new DateOnly(2026, 9, 2),
            TypOkresu.Miesieczny);

        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 9), okres);
    }

    /// <summary>
    /// Faktura otrzymana przed dostawą nie daje prawa do wcześniejszego
    /// odliczenia - liczy się moment powstania obowiązku u sprzedawcy.
    /// </summary>
    [Fact]
    public void OdliczenieNieWczesniejNizObowiazekUSprzedawcy()
    {
        OkresRozliczeniowy okres = TerminyVat.NajwczesniejszyOkresOdliczenia(
            dataObowiazkuUSprzedawcy: new DateOnly(2026, 9, 10),
            dataOtrzymaniaFaktury: new DateOnly(2026, 8, 25),
            TypOkresu.Miesieczny);

        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 9), okres);
    }

    [Fact]
    public void PrzyRozliczeniuMiesiecznymSaTrzyOkresyNaOdliczenie()
    {
        OkresRozliczeniowy najwczesniejszy = OkresRozliczeniowy.Miesiac(2026, 8);

        Assert.Equal(OkresRozliczeniowy.Miesiac(2026, 11),
                     TerminyVat.NajpozniejszyOkresOdliczenia(najwczesniejszy));
    }

    [Fact]
    public void PrzyRozliczeniuKwartalnymSaDwaOkresyNaOdliczenie()
    {
        OkresRozliczeniowy najwczesniejszy = OkresRozliczeniowy.Kwartal(2026, 3);

        Assert.Equal(OkresRozliczeniowy.Kwartal(2027, 1),
                     TerminyVat.NajpozniejszyOkresOdliczenia(najwczesniejszy));
    }

    [Fact]
    public void OdliczenieZaWczesneJestOdrzucane()
    {
        WynikWalidacji wynik = TerminyVat.SprawdzOkresOdliczenia(
            dataObowiazkuUSprzedawcy: new DateOnly(2026, 8, 20),
            dataOtrzymaniaFaktury: new DateOnly(2026, 9, 2),
            dataUjecia: new DateOnly(2026, 8, 31),
            TypOkresu.Miesieczny);

        Assert.True(wynik.SaBledy);
        Assert.Contains("wrzesień 2026", wynik.Opis(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(1, false)]
    public void OdliczenieMiesciSieWTerminie(int miesiac, bool dozwolone)
    {
        int rok = miesiac == 1 ? 2027 : 2026;

        WynikWalidacji wynik = TerminyVat.SprawdzOkresOdliczenia(
            dataObowiazkuUSprzedawcy: new DateOnly(2026, 9, 1),
            dataOtrzymaniaFaktury: new DateOnly(2026, 9, 1),
            dataUjecia: new DateOnly(rok, miesiac, 15),
            TypOkresu.Miesieczny);

        Assert.Equal(dozwolone, !wynik.SaBledy);
    }
}
