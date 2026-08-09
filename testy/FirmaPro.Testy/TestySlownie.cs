using FirmaPro.Domena;

namespace FirmaPro.Testy;

/// <summary>
/// Zapis kwoty słownie.
/// </summary>
/// <remarks>
/// Polska odmiana liczebników ma pułapki, które łatwo przeoczyć: „dwa złote",
/// ale „dwanaście złotych", mimo że obie liczby kończą się na dwójkę. Testy
/// pilnują właśnie tych przypadków, bo kwota słownie na fakturze bywa
/// czytana przez człowieka przy sprawdzaniu przelewu.
/// </remarks>
public sealed class TestySlownie
{
    [Theory]
    [InlineData(0, "zero")]
    [InlineData(1, "jeden")]
    [InlineData(5, "pięć")]
    [InlineData(11, "jedenaście")]
    [InlineData(21, "dwadzieścia jeden")]
    [InlineData(100, "sto")]
    [InlineData(101, "sto jeden")]
    [InlineData(200, "dwieście")]
    [InlineData(999, "dziewięćset dziewięćdziesiąt dziewięć")]
    [InlineData(1000, "tysiąc")]
    [InlineData(1001, "tysiąc jeden")]
    [InlineData(2000, "dwa tysiące")]
    [InlineData(5000, "pięć tysięcy")]
    [InlineData(12000, "dwanaście tysięcy")]
    [InlineData(22000, "dwadzieścia dwa tysiące")]
    [InlineData(1000000, "milion")]
    [InlineData(2000000, "dwa miliony")]
    [InlineData(5000000, "pięć milionów")]
    [InlineData(1234567, "milion dwieście trzydzieści cztery tysiące pięćset sześćdziesiąt siedem")]
    public void LiczbyZapisywaneSaPoprawnie(long liczba, string oczekiwany) =>
        Assert.Equal(oczekiwany, Slownie.Liczba(liczba));

    /// <summary>
    /// Liczby 12-14 wymagają dopełniacza, choć kończą się na 2, 3 i 4.
    /// </summary>
    /// <remarks>
    /// To najczęstszy błąd w takich modułach: reguła „końcówka 2-4 to forma
    /// mnoga" daje „dwanaście złote" zamiast „dwanaście złotych".
    /// </remarks>
    [Theory]
    [InlineData(1, "jeden złoty 00/100")]
    [InlineData(2, "dwa złote 00/100")]
    [InlineData(4, "cztery złote 00/100")]
    [InlineData(5, "pięć złotych 00/100")]
    [InlineData(12, "dwanaście złotych 00/100")]
    [InlineData(13, "trzynaście złotych 00/100")]
    [InlineData(14, "czternaście złotych 00/100")]
    [InlineData(22, "dwadzieścia dwa złote 00/100")]
    [InlineData(112, "sto dwanaście złotych 00/100")]
    [InlineData(122, "sto dwadzieścia dwa złote 00/100")]
    public void OdmianaZlotychIdzieZaPolskimiRegulami(int kwota, string oczekiwany) =>
        Assert.Equal(oczekiwany, Slownie.Kwota(kwota));

    [Theory]
    [InlineData(0.07, "zero złotych 07/100")]
    [InlineData(2925.07, "dwa tysiące dziewięćset dwadzieścia pięć złotych 07/100")]
    [InlineData(1234.50, "tysiąc dwieście trzydzieści cztery złote 50/100")]
    public void GroszePodawaneSaJakoUlamek(double kwota, string oczekiwany) =>
        Assert.Equal(oczekiwany, Slownie.Kwota((decimal)kwota));

    /// <summary>
    /// Grosze biorą się z zaokrąglenia kwoty, a nie z obcięcia jej końcówki.
    /// </summary>
    [Fact]
    public void KwotaJestZaokraglanaPrzedZapisem() =>
        Assert.Equal("dziesięć złotych 13/100", Slownie.Kwota(10.125m));

    [Fact]
    public void WalutaSpozaSlownikaPodawanaJestKodem() =>
        Assert.Equal("pięć NOK 00/100", Slownie.Kwota(5m, "NOK"));

    [Theory]
    [InlineData("EUR", "dwa euro 00/100")]
    [InlineData("USD", "dwa dolary 00/100")]
    public void ObceWalutySaOdmieniane(string waluta, string oczekiwany) =>
        Assert.Equal(oczekiwany, Slownie.Kwota(2m, waluta));

    [Fact]
    public void KwotaUjemnaMaPrzedrostekMinus() =>
        Assert.StartsWith("minus ", Slownie.Kwota(-5m), StringComparison.Ordinal);
}
