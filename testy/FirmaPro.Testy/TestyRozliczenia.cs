using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Rozliczanie należności.
/// </summary>
/// <remarks>
/// Reguła jest jedna dla całego programu - przy fakturze, na liście
/// należności i w treści przypomnienia. Gdyby każde z tych miejsc liczyło
/// po swojemu, prędzej czy później pokazałyby kontrahentowi różne kwoty.
/// </remarks>
public class TestyRozliczenia
{
    private static readonly DateOnly Dzisiaj = new(2026, 8, 20);

    private static Rozliczenie Dla(decimal brutto, decimal zaplacono, DateOnly? termin = null) =>
        new(brutto, zaplacono, termin, Dzisiaj);

    [Fact]
    public void BrakWplatToFakturaNiezaplacona()
    {
        Rozliczenie rozliczenie = Dla(1230m, 0m);

        Assert.Equal(StanZaplaty.Nieoplacona, rozliczenie.Stan);
        Assert.Equal(1230m, rozliczenie.Pozostalo);
    }

    [Fact]
    public void CzesciowaWplataZostawiaReszte()
    {
        Rozliczenie rozliczenie = Dla(1230m, 500m);

        Assert.Equal(StanZaplaty.CzesciowoOplacona, rozliczenie.Stan);
        Assert.Equal(730m, rozliczenie.Pozostalo);
    }

    [Fact]
    public void RownaWplataZamykaNaleznosc()
    {
        Rozliczenie rozliczenie = Dla(1230m, 1230m);

        Assert.Equal(StanZaplaty.Oplacona, rozliczenie.Stan);
        Assert.Equal(0m, rozliczenie.Pozostalo);
        Assert.False(rozliczenie.PoTerminie);
    }

    /// <summary>
    /// Nadpłata nie jest błędem i nie może zniknąć.
    /// </summary>
    /// <remarks>
    /// Kontrahent bywa, że zaokrągli przelew w górę albo zapłaci dwa razy.
    /// Program ma to pokazać, bo to pieniądze do zwrotu albo do rozliczenia
    /// z następną fakturą.
    /// </remarks>
    [Fact]
    public void NadplataJestWidoczna()
    {
        Rozliczenie rozliczenie = Dla(1230m, 1300m);

        Assert.Equal(StanZaplaty.Nadplacona, rozliczenie.Stan);
        Assert.Equal(-70m, rozliczenie.Pozostalo);
        Assert.False(rozliczenie.PoTerminie);
    }

    /// <summary>
    /// Zapłata w ostatnim dniu terminu jest zapłatą w terminie.
    /// </summary>
    /// <remarks>
    /// Opóźnienie zaczyna się nazajutrz po terminie - inaczej program
    /// wysyłałby wezwania ludziom, którzy zapłacili zgodnie z umową.
    /// </remarks>
    [Theory]
    [InlineData(2026, 8, 21, false, 0)]   // termin jutro
    [InlineData(2026, 8, 20, false, 0)]   // termin dzisiaj
    [InlineData(2026, 8, 19, true, 1)]    // termin był wczoraj
    [InlineData(2026, 7, 21, true, 30)]
    public void TerminLiczySieDoKoncaDnia(int rok, int miesiac, int dzien,
                                          bool poTerminie, int dni)
    {
        Rozliczenie rozliczenie = Dla(1230m, 0m, new DateOnly(rok, miesiac, dzien));

        Assert.Equal(poTerminie, rozliczenie.PoTerminie);
        Assert.Equal(dni, rozliczenie.DniPoTerminie);
    }

    [Fact]
    public void ZaplaconaFakturaNigdyNieJestPoTerminie()
    {
        Rozliczenie rozliczenie = Dla(1230m, 1230m, new DateOnly(2026, 1, 1));

        Assert.False(rozliczenie.PoTerminie);
        Assert.Equal(0, rozliczenie.DniPoTerminie);
    }

    [Fact]
    public void FakturaBezTerminuNieJestPrzeterminowana()
    {
        Rozliczenie rozliczenie = Dla(1230m, 0m, termin: null);

        Assert.False(rozliczenie.PoTerminie);
        Assert.Equal(StanZaplaty.Nieoplacona, rozliczenie.Stan);
    }

    /// <summary>Reszta liczona jest w groszach, a nie w ułamkach grosza.</summary>
    [Fact]
    public void ResztaZaokraglanaJestDoGrosza()
    {
        Rozliczenie rozliczenie = Dla(100.005m, 0m);

        Assert.Equal(100.01m, rozliczenie.Pozostalo);
    }
}
