using System.Globalization;
using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Nadaje kolejne numery faktur w obrębie firmy.
/// </summary>
/// <remarks>
/// Numery muszą tworzyć ciąg bez luk i powtórzeń. Licznik trzymany jest
/// w bazie i zwiększany w transakcji, a niepowtarzalność wymusza dodatkowo
/// indeks unikalny na parze (firma, numer) - to on jest ostateczną gwarancją,
/// gdyby dwie osoby wystawiały fakturę w tej samej chwili.
/// </remarks>
public sealed class UslugaNumeracji(FirmaProDbContext baza)
{
    /// <summary>Domyślny wzór numeru dla nowo zakładanej firmy.</summary>
    public const string DomyslnyWzor = "FV/{ROK}/{MC}/{NR}";

    /// <summary>
    /// Rezerwuje kolejny numer na wskazany dzień i zwraca go.
    /// </summary>
    /// <remarks>
    /// Numeracja zeruje się co miesiąc, więc licznik prowadzony jest osobno
    /// dla każdej pary rok-miesiąc.
    /// </remarks>
    public async Task<string> NastepnyNumerAsync(DateOnly dataWystawienia,
                                                 CancellationToken anulowanie = default)
    {
        SeriaNumeracji seria = await baza.SerieNumeracji
            .FirstOrDefaultAsync(s => s.Rok == dataWystawienia.Year
                                      && s.Miesiac == dataWystawienia.Month,
                                 anulowanie)
            ?? await ZalozSerieAsync(dataWystawienia, anulowanie);

        seria.OstatniNumer++;
        await baza.SaveChangesAsync(anulowanie);

        return ZbudujNumer(seria.Wzor, dataWystawienia, seria.OstatniNumer);
    }

    private async Task<SeriaNumeracji> ZalozSerieAsync(DateOnly data,
                                                       CancellationToken anulowanie)
    {
        // Wzór przepisujemy z serii z poprzedniego okresu, żeby zmiana formatu
        // numeru nie znikała przy przejściu na nowy miesiąc.
        SeriaNumeracji? poprzednia = await baza.SerieNumeracji
            .OrderByDescending(s => s.Rok).ThenByDescending(s => s.Miesiac)
            .FirstOrDefaultAsync(anulowanie);

        var seria = new SeriaNumeracji
        {
            Nazwa = "Sprzedaż",
            Wzor = poprzednia?.Wzor ?? DomyslnyWzor,
            Rok = data.Year,
            Miesiac = data.Month,
            OstatniNumer = 0
        };

        baza.SerieNumeracji.Add(seria);
        return seria;
    }

    /// <summary>Podstawia wartości do wzoru numeru.</summary>
    public static string ZbudujNumer(string wzor, DateOnly data, int numer) =>
        wzor.Replace("{ROK}", data.Year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{MC}", data.Month.ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{NR}", numer.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
