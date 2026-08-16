using System.Globalization;
using System.Net;
using System.Text.Json;
using FirmaPro.Domena;

namespace FirmaPro.Web.Uslugi;

/// <summary>Nie udało się ustalić kursu waluty.</summary>
public sealed class BladKursuException(string komunikat, Exception? przyczyna = null)
    : Exception(komunikat, przyczyna);

/// <summary>Źródło kursów walut.</summary>
/// <remarks>
/// Wydzielone interfejsem, bo wystawienie faktury nie może zależeć od tego,
/// czy akurat działa sieć - a test nie może wołać Narodowego Banku Polskiego.
/// </remarks>
public interface IKursyWalut
{
    /// <summary>
    /// Kurs średni waluty z ostatniej tabeli nie później niż wskazany dzień.
    /// </summary>
    /// <param name="waluta">Trzyliterowy kod waluty, np. „EUR”.</param>
    /// <param name="naDzien">
    /// Dzień, z którego ma pochodzić kurs. Gdy tego dnia nie było tabeli
    /// (weekend, święto), obowiązuje ostatnia wcześniejsza.
    /// </param>
    Task<KursWaluty> KursAsync(string waluta, DateOnly naDzien,
                               CancellationToken anulowanie = default);
}

/// <summary>
/// Kursy średnie z tabeli A Narodowego Banku Polskiego.
/// </summary>
/// <remarks>
/// <para>
/// NBP publikuje tabelę wyłącznie w dni robocze. Zapytanie o sobotę, niedzielę
/// albo święto kończy się odpowiedzią „404”, i to jest normalny przebieg,
/// a nie awaria - trzeba wtedy cofnąć się do ostatniej opublikowanej tabeli.
/// Dokładnie tego wymaga ustawa: kurs z ostatniego dnia roboczego.
/// </para>
/// <para>
/// Kalendarza świąt nie liczymy sami. Wielkanoc rusza się co roku, a dni wolne
/// bywają zmieniane ustawą - jedynym pewnym źródłem tego, czy dzień był
/// roboczy, jest to, czy NBP wydał wtedy tabelę.
/// </para>
/// </remarks>
public sealed class KlientNbp(IHttpClientFactory fabryka, ILogger<KlientNbp> dziennik)
    : IKursyWalut
{
    /// <summary>Nazwa klienta HTTP rejestrowanego w kontenerze usług.</summary>
    public const string NazwaKlientaHttp = "nbp";

    /// <summary>Adres udostępnianej publicznie usługi kursów.</summary>
    public const string AdresBazowy = "https://api.nbp.pl/api/";

    /// <summary>
    /// Ile dni wstecz szukamy tabeli.
    /// </summary>
    /// <remarks>
    /// Najdłuższa przerwa w publikacji to przełom roku ze świętami wypadającymi
    /// w dni robocze - kilka dni. Dziesięć daje zapas, a jednocześnie nie
    /// pozwala programowi w nieskończoność odpytywać NBP o kurs waluty, której
    /// ten nie notuje.
    /// </remarks>
    private const int IleDniWstecz = 10;

    public async Task<KursWaluty> KursAsync(string waluta, DateOnly naDzien,
                                            CancellationToken anulowanie = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waluta);

        string kod = waluta.Trim().ToUpperInvariant();

        if (kod == "PLN")
        {
            return KursWaluty.Zlotowy(naDzien);
        }

        using HttpClient http = fabryka.CreateClient(NazwaKlientaHttp);

        for (int wstecz = 0; wstecz < IleDniWstecz; wstecz++)
        {
            DateOnly dzien = naDzien.AddDays(-wstecz);

            KursWaluty? kurs = await SprobujAsync(http, kod, dzien, anulowanie);

            if (kurs is not null)
            {
                return kurs;
            }
        }

        throw new BladKursuException(
            $"Nie znaleziono kursu waluty {kod} w tabelach NBP z {IleDniWstecz} dni " +
            $"poprzedzających {naDzien:yyyy-MM-dd}. Sprawdź kod waluty.");
    }

    private async Task<KursWaluty?> SprobujAsync(HttpClient http, string kod, DateOnly dzien,
                                                 CancellationToken anulowanie)
    {
        string adres = string.Create(CultureInfo.InvariantCulture,
            $"exchangerates/rates/a/{kod}/{dzien:yyyy-MM-dd}/?format=json");

        HttpResponseMessage odpowiedz;

        try
        {
            odpowiedz = await http.GetAsync(new Uri(adres, UriKind.Relative), anulowanie);
        }
        catch (HttpRequestException blad)
        {
            throw new BladKursuException(
                "Nie udało się połączyć z serwisem kursów NBP. " +
                "Sprawdź połączenie z internetem.", blad);
        }

        using (odpowiedz)
        {
            // Brak tabeli na dany dzień to zwykły weekend albo święto.
            if (odpowiedz.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!odpowiedz.IsSuccessStatusCode)
            {
                Dziennik.NieudanyKurs(dziennik, kod, (int)odpowiedz.StatusCode);

                throw new BladKursuException(
                    $"Serwis kursów NBP odpowiedział błędem {(int)odpowiedz.StatusCode}.");
            }

            string tresc = await odpowiedz.Content.ReadAsStringAsync(anulowanie);

            return Odczytaj(tresc, kod);
        }
    }

    /// <summary>
    /// Wyjmuje kurs z odpowiedzi.
    /// </summary>
    /// <remarks>
    /// Odpowiedź niesie jedną pozycję w tablicy „rates”: kurs średni („mid”),
    /// numer tabeli i jej datę. Bierzemy datę z odpowiedzi, a nie tę, o którą
    /// pytaliśmy - to ona jest dniem, z którego kurs faktycznie pochodzi.
    /// </remarks>
    private static KursWaluty Odczytaj(string json, string kod)
    {
        try
        {
            using JsonDocument dokument = JsonDocument.Parse(json);
            JsonElement pozycja = dokument.RootElement.GetProperty("rates")[0];

            decimal wartosc = pozycja.GetProperty("mid").GetDecimal();
            string? tabela = pozycja.TryGetProperty("no", out JsonElement numer)
                ? numer.GetString()
                : null;

            DateOnly data = pozycja.TryGetProperty("effectiveDate", out JsonElement dzien)
                             && DateOnly.TryParse(dzien.GetString(),
                                    CultureInfo.InvariantCulture, out DateOnly odczytana)
                ? odczytana
                : DateOnly.FromDateTime(DateTime.UtcNow);

            if (wartosc <= 0)
            {
                throw new BladKursuException($"Serwis NBP podał kurs {kod} równy zeru.");
            }

            return new KursWaluty(kod, wartosc, data, tabela);
        }
        catch (Exception blad) when (blad is JsonException or KeyNotFoundException
                                              or IndexOutOfRangeException
                                              or InvalidOperationException)
        {
            throw new BladKursuException(
                "Odpowiedź serwisu kursów NBP ma nieznany kształt.", blad);
        }
    }
}
