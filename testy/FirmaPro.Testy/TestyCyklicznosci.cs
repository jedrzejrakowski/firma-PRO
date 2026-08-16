using FirmaPro.Domena;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Rachunek dat faktur cyklicznych.
/// </summary>
/// <remarks>
/// Wygląda na oczywisty do chwili, gdy ktoś ustawi wystawianie na koniec
/// miesiąca. Luty ma 28 albo 29 dni, kwiecień 30 - a faktura ma się wtedy
/// pojawić, nie zniknąć ani nie przeskoczyć na następny miesiąc.
/// </remarks>
public sealed class TestyCyklicznosci
{
    [Theory]
    [InlineData(RytmFaktury.Miesiecznie, 1)]
    [InlineData(RytmFaktury.Kwartalnie, 3)]
    [InlineData(RytmFaktury.Polrocznie, 6)]
    [InlineData(RytmFaktury.Rocznie, 12)]
    public void RytmPrzesuwaOWlasciwaLiczbeMiesiecy(RytmFaktury rytm, int miesiecy)
    {
        var poprzednia = new DateOnly(2026, 1, 10);

        DateOnly nastepna = Cyklicznosc.Nastepna(poprzednia, 10, rytm);

        Assert.Equal(poprzednia.AddMonths(miesiecy), nastepna);
    }

    /// <summary>
    /// Dzień dłuższy od miesiąca przycinamy, zamiast przenosić na następny.
    /// </summary>
    [Fact]
    public void DzienDluzszyOdMiesiacaJestPrzycinany()
    {
        // Wzorzec na 28. dzień - w lutym zwykłego roku to ostatni dzień.
        DateOnly luty = Cyklicznosc.Nastepna(new DateOnly(2026, 1, 28), 28,
            RytmFaktury.Miesiecznie);

        Assert.Equal(new DateOnly(2026, 2, 28), luty);
    }

    [Fact]
    public void OstatniDzienMiesiacaDopasowujeSieDoDlugosci()
    {
        DateOnly styczen = Cyklicznosc.PierwszaData(
            new DateOnly(2026, 1, 1), Cyklicznosc.OstatniDzien, RytmFaktury.Miesiecznie);

        Assert.Equal(new DateOnly(2026, 1, 31), styczen);

        DateOnly luty = Cyklicznosc.Nastepna(styczen, Cyklicznosc.OstatniDzien,
            RytmFaktury.Miesiecznie);

        Assert.Equal(new DateOnly(2026, 2, 28), luty);

        // Rok przestępny - luty ma wtedy 29 dni.
        DateOnly lutyPrzestepny = Cyklicznosc.Nastepna(new DateOnly(2028, 1, 31),
            Cyklicznosc.OstatniDzien, RytmFaktury.Miesiecznie);

        Assert.Equal(new DateOnly(2028, 2, 29), lutyPrzestepny);
    }

    /// <summary>
    /// Wzorzec założony po terminie nie wystawia faktury wstecz.
    /// </summary>
    [Fact]
    public void PierwszaFakturaNieWypadaPrzedZalozeniemWzorca()
    {
        // Wzorzec na 5. dzień miesiąca, założony 20 sierpnia.
        DateOnly pierwsza = Cyklicznosc.PierwszaData(
            new DateOnly(2026, 8, 20), 5, RytmFaktury.Miesiecznie);

        Assert.Equal(new DateOnly(2026, 9, 5), pierwsza);
    }

    [Fact]
    public void PierwszaFakturaMozeWypascWDniuZalozenia()
    {
        DateOnly pierwsza = Cyklicznosc.PierwszaData(
            new DateOnly(2026, 8, 5), 5, RytmFaktury.Miesiecznie);

        Assert.Equal(new DateOnly(2026, 8, 5), pierwsza);
    }

    /// <summary>
    /// Po przerwie w pracy zaległych okresów bywa kilka i trzeba je policzyć.
    /// </summary>
    [Fact]
    public void ZalegleOkresySaPoliczone()
    {
        int ile = Cyklicznosc.IleZaleglych(
            nastepna: new DateOnly(2026, 5, 1),
            dzisiaj: new DateOnly(2026, 8, 16),
            dzienMiesiaca: 1,
            rytm: RytmFaktury.Miesiecznie,
            doKiedy: null);

        // Maj, czerwiec, lipiec, sierpień.
        Assert.Equal(4, ile);
    }

    [Fact]
    public void ZaleglosciKonczaSieNaDacieZakonczeniaWzorca()
    {
        int ile = Cyklicznosc.IleZaleglych(
            nastepna: new DateOnly(2026, 5, 1),
            dzisiaj: new DateOnly(2026, 8, 16),
            dzienMiesiaca: 1,
            rytm: RytmFaktury.Miesiecznie,
            doKiedy: new DateOnly(2026, 6, 30));

        // Maj i czerwiec - lipiec jest już poza obowiązywaniem wzorca.
        Assert.Equal(2, ile);
    }

    [Fact]
    public void TerminWPrzyszlosciNieDajeZaleglosci()
    {
        int ile = Cyklicznosc.IleZaleglych(
            nastepna: new DateOnly(2026, 9, 1),
            dzisiaj: new DateOnly(2026, 8, 16),
            dzienMiesiaca: 1,
            rytm: RytmFaktury.Miesiecznie,
            doKiedy: null);

        Assert.Equal(0, ile);
    }
}
