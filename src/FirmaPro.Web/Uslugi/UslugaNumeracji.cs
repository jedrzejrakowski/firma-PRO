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

    /// <summary>Nazwa serii zwykłej sprzedaży.</summary>
    public const string SeriaSprzedazy = "Sprzedaż";

    /// <summary>
    /// Nazwa serii faktur korygujących.
    /// </summary>
    /// <remarks>
    /// Korekty numerowane są osobno, bo tak prowadzi je większość firm:
    /// przy przeglądaniu ksiąg widać wtedy od razu, że dokument jest korektą,
    /// bez zaglądania w jego treść. Prawo nie wymaga osobnej serii, ale też
    /// jej nie zabrania - numeracja ma być tylko ciągła w obrębie serii.
    /// </remarks>
    public const string SeriaKorekt = "Korekty";

    /// <summary>Domyślny wzór numeru korekty.</summary>
    public const string DomyslnyWzorKorekt = "KOR/{ROK}/{MC}/{NR}";

    /// <summary>
    /// Rezerwuje kolejny numer na wskazany dzień i zwraca go.
    /// </summary>
    /// <remarks>
    /// Numeracja zeruje się co miesiąc, więc licznik prowadzony jest osobno
    /// dla każdej pary rok-miesiąc.
    /// </remarks>
    public async Task<string> NastepnyNumerAsync(DateOnly dataWystawienia,
                                                 CancellationToken anulowanie = default) =>
        await NastepnyNumerAsync(dataWystawienia, SeriaSprzedazy, DomyslnyWzor, anulowanie);

    /// <summary>Rezerwuje kolejny numer we wskazanej serii.</summary>
    public async Task<string> NastepnyNumerAsync(DateOnly dataWystawienia,
                                                 string nazwaSerii,
                                                 string domyslnyWzor,
                                                 CancellationToken anulowanie = default)
    {
        SeriaNumeracji seria = await baza.SerieNumeracji
            .FirstOrDefaultAsync(s => s.Nazwa == nazwaSerii
                                      && s.Rok == dataWystawienia.Year
                                      && s.Miesiac == dataWystawienia.Month,
                                 anulowanie)
            ?? await ZalozSerieAsync(dataWystawienia, nazwaSerii, domyslnyWzor, anulowanie);

        seria.OstatniNumer++;
        await baza.SaveChangesAsync(anulowanie);

        return ZbudujNumer(seria.Wzor, dataWystawienia, seria.OstatniNumer);
    }

    private async Task<SeriaNumeracji> ZalozSerieAsync(DateOnly data,
                                                       string nazwaSerii,
                                                       string domyslnyWzor,
                                                       CancellationToken anulowanie)
    {
        // Wzór przepisujemy z serii z poprzedniego okresu - tej samej serii,
        // żeby zmiana formatu numeru nie znikała przy przejściu na nowy
        // miesiąc, a korekty nie przejęły wzoru sprzedaży.
        SeriaNumeracji? poprzednia = await baza.SerieNumeracji
            .Where(s => s.Nazwa == nazwaSerii)
            .OrderByDescending(s => s.Rok).ThenByDescending(s => s.Miesiac)
            .FirstOrDefaultAsync(anulowanie);

        var seria = new SeriaNumeracji
        {
            Nazwa = nazwaSerii,
            Wzor = poprzednia?.Wzor ?? domyslnyWzor,
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
