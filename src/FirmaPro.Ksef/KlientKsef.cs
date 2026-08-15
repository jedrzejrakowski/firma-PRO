using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FirmaPro.Domena;

namespace FirmaPro.Ksef;

/// <summary>
/// Klient API Krajowego Systemu e-Faktur (KSeF 2.0, wersja API v2).
/// </summary>
/// <remarks>
/// <para>Przebieg wysyłki faktury:</para>
/// <code>
/// UwierzytelnijAsync   -> token dostępowy (JWT)
/// OtworzSesjeAsync     -> numer sesji + klucz szyfrujący
/// WyslijFaktureAsync   -> numer referencyjny dokumentu
/// PoczekajNaWynikAsync -> numer KSeF po pozytywnej weryfikacji
/// ZamknijSesjeAsync    -> zbiorcze UPO
/// </code>
/// <para>
/// Adresy i nazwy pól pochodzą ze specyfikacji OpenAPI opublikowanej przez
/// Ministerstwo Finansów.
/// </para>
/// </remarks>
public sealed class KlientKsef : IKlientKsef
{
    /// <summary>Kod wzoru przekazywany przy otwieraniu sesji.</summary>
    private static readonly KodFormularza WzorFa3 =
        new(Fa3Generator.KodSystemowy, Fa3Generator.WersjaSchemy, Fa3Generator.KodFormularza);

    /// <summary>Przeznaczenia kluczy publicznych publikowanych przez KSeF.</summary>
    private const string UzycieToken = "KsefTokenEncryption";
    private const string UzycieKluczSymetryczny = "SymmetricKeyEncryption";

    /// <summary>Kod oznaczający pomyślne zakończenie operacji.</summary>
    private const int KodPrzyjeta = 200;

    private static readonly JsonSerializerOptions UstawieniaJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly TimeProvider _czas;
    private readonly TimeSpan _odstepOdpytywania;
    private readonly int _maksProbOdpytywania;

    private IReadOnlyList<CertyfikatPubliczny>? _certyfikaty;
    private string? _tokenDostepu;
    private Kryptografia.KluczSesji? _kluczSesji;
    private string? _numerSesji;

    /// <summary>
    /// Tworzy klienta. <paramref name="http"/> jest wstrzykiwany, dzięki czemu
    /// testy mogą podstawić własną obsługę żądań bez ruszania sieci.
    /// </summary>
    public KlientKsef(HttpClient http, SrodowiskoKsef srodowisko,
                      TimeProvider? czas = null,
                      TimeSpan? odstepOdpytywania = null,
                      int maksProbOdpytywania = 30)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        Srodowisko = srodowisko;
        _czas = czas ?? TimeProvider.System;
        _odstepOdpytywania = odstepOdpytywania ?? TimeSpan.FromSeconds(2);
        _maksProbOdpytywania = maksProbOdpytywania;

        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(AdresyKsef.Api(srodowisko) + "/");
        }
    }

    public SrodowiskoKsef Srodowisko { get; }

    /// <summary>Numer bieżącej sesji wysyłkowej, jeśli jest otwarta.</summary>
    public string? NumerSesji => _numerSesji;

    // -------------------------------------------------------- sprawdzenie łączności

    public async Task<StanSrodowiska> SprawdzSrodowiskoAsync(
        CancellationToken anulowanie = default)
    {
        // Świadomie pomijamy zapamiętane certyfikaty: sprawdzenie ma pokazać
        // stan teraz, a nie odtworzyć to, co pobrano przy poprzedniej wysyłce.
        _certyfikaty = await GetAsync<List<CertyfikatPubliczny>>(
            "security/public-key-certificates", null, anulowanie);

        DateTimeOffset teraz = _czas.GetUtcNow();

        List<CertyfikatPubliczny> wazne = [.. _certyfikaty
            .Where(c => c.WaznyOd <= teraz && c.WaznyDo >= teraz)];

        bool Ma(string przeznaczenie) =>
            wazne.Any(c => c.Przeznaczenie.Contains(przeznaczenie, StringComparer.Ordinal));

        return new StanSrodowiska(
            _http.BaseAddress?.ToString() ?? AdresyKsef.Api(Srodowisko),
            wazne.Count,
            Ma(UzycieToken),
            Ma(UzycieKluczSymetryczny),
            wazne.Count == 0 ? null : wazne.Min(c => c.WaznyDo));
    }

    // ------------------------------------------------------ uwierzytelnianie

    public async Task UwierzytelnijAsync(string nip, string tokenKsef,
                                         CancellationToken anulowanie = default)
    {
        if (string.IsNullOrWhiteSpace(tokenKsef))
        {
            throw new BladKsefException(
                "Brak tokena KSeF. Wygeneruj go w aplikacji webowej KSeF " +
                "i zapisz w ustawieniach firmy.");
        }

        OdpowiedzWyzwania wyzwanie =
            await PostAsync<object, OdpowiedzWyzwania>("auth/challenge", null, null, anulowanie);

        // Szyfrowany jest ciąg "token|znacznik_czasu". Znacznik pełni rolę
        // wartości jednorazowej, dzięki czemu przechwycony szyfrogram nie da
        // się użyć ponownie w kolejnej sesji.
        (RSA klucz, string identyfikatorKlucza) =
            await KluczDoAsync(UzycieToken, anulowanie);

        string zaszyfrowany;
        using (klucz)
        {
            byte[] doZaszyfrowania = Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture,
                    $"{tokenKsef}|{wyzwanie.ZnacznikMs}"));
            zaszyfrowany = Convert.ToBase64String(
                Kryptografia.SzyfrujRsa(doZaszyfrowania, klucz));
        }

        var zadanie = new ZadanieTokenem(
            wyzwanie.Wyzwanie,
            new IdentyfikatorKontekstu("Nip", OczyscNip(nip)),
            zaszyfrowany,
            identyfikatorKlucza);

        OdpowiedzUwierzytelnienia rozpoczete =
            await PostAsync<ZadanieTokenem, OdpowiedzUwierzytelnienia>(
                "auth/ksef-token", zadanie, null, anulowanie);

        await OdbierzTokenDostepuAsync(rozpoczete, anulowanie);
    }

    /// <summary>
    /// Uwierzytelnia się podpisem certyfikatu.
    /// </summary>
    /// <remarks>
    /// Droga docelowa: od 2027 roku tokeny przestają działać i pozostaje
    /// wyłącznie certyfikat. Przebieg jest ten sam co przy tokenie - wyzwanie,
    /// potwierdzenie, wymiana na token dostępowy - różni się środek: zamiast
    /// zaszyfrować token kluczem publicznym systemu, podpisujemy wyzwanie
    /// kluczem prywatnym certyfikatu.
    /// </remarks>
    public async Task UwierzytelnijCertyfikatemAsync(
        string nip, X509Certificate2 certyfikat, CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(certyfikat);

        OdpowiedzWyzwania wyzwanie =
            await PostAsync<object, OdpowiedzWyzwania>("auth/challenge", null, null, anulowanie);

        string dokument = ZadanieUwierzytelnienia.Zbuduj(wyzwanie.Wyzwanie, OczyscNip(nip));
        string podpisany = PodpisXades.Zloz(dokument, certyfikat, _czas);

        OdpowiedzUwierzytelnienia rozpoczete =
            await WyslijPodpisanyDokumentAsync(podpisany, anulowanie);

        await OdbierzTokenDostepuAsync(rozpoczete, anulowanie);
    }

    /// <summary>
    /// Czeka na potwierdzenie i wymienia je na token dostępowy.
    /// </summary>
    /// <remarks>
    /// Wspólne zakończenie obu dróg uwierzytelnienia - od tego miejsca
    /// system nie rozróżnia już, czy przedstawiliśmy się tokenem,
    /// czy podpisem.
    /// </remarks>
    private async Task OdbierzTokenDostepuAsync(OdpowiedzUwierzytelnienia rozpoczete,
                                                CancellationToken anulowanie)
    {
        await PoczekajNaUwierzytelnienieAsync(
            rozpoczete.NumerReferencyjny, rozpoczete.TokenOperacji.Token, anulowanie);

        OdpowiedzTokenow tokeny =
            await PostAsync<object, OdpowiedzTokenow>(
                "auth/token/redeem", null, rozpoczete.TokenOperacji.Token, anulowanie);

        _tokenDostepu = tokeny.TokenDostepu.Token;
    }

    /// <summary>
    /// Wysyła podpisany dokument uwierzytelniający.
    /// </summary>
    /// <remarks>
    /// W odróżnieniu od reszty wywołań treścią jest tu XML, a nie JSON -
    /// przesyłany dosłownie, bo każda zmiana bajtów unieważniłaby podpis.
    /// Sprawdzanie ścieżki certyfikatu jest wyłączone: w środowisku testowym
    /// używa się certyfikatów samopodpisanych, a certyfikat wydany przez KSeF
    /// system i tak rozpoznaje po swojemu.
    /// </remarks>
    private async Task<OdpowiedzUwierzytelnienia> WyslijPodpisanyDokumentAsync(
        string podpisanyXml, CancellationToken anulowanie)
    {
        using var zadanie = new HttpRequestMessage(
            HttpMethod.Post, "auth/xades-signature?verifyCertificateChain=false")
        {
            Content = new StringContent(podpisanyXml, Encoding.UTF8, "application/xml")
        };

        return await WyslijAsync<OdpowiedzUwierzytelnienia>(zadanie, null, anulowanie);
    }

    private async Task PoczekajNaUwierzytelnienieAsync(
        string numerReferencyjny, string tokenOperacji, CancellationToken anulowanie)
    {
        for (int proba = 0; proba < _maksProbOdpytywania; proba++)
        {
            OdpowiedzStanuUwierzytelnienia stan =
                await GetAsync<OdpowiedzStanuUwierzytelnienia>(
                    $"auth/{numerReferencyjny}", tokenOperacji, anulowanie);

            if (stan.Status.Kod == KodPrzyjeta)
            {
                return;
            }

            if (stan.Status.Kod >= 300)
            {
                throw new BladKsefException(
                    "Uwierzytelnienie odrzucone: " + (stan.Status.Opis ?? "brak opisu"),
                    null, stan.Status.Szczegoly);
            }

            await Task.Delay(_odstepOdpytywania, _czas, anulowanie);
        }

        throw new BladKsefException(
            "KSeF nie potwierdził uwierzytelnienia w wyznaczonym czasie.");
    }

    // -------------------------------------------------------- klucze publiczne

    /// <summary>
    /// Zwraca klucz publiczny o danym przeznaczeniu wraz z identyfikatorem.
    /// </summary>
    /// <remarks>
    /// Klucze pobierane są z API, a nie zaszyte w kodzie - Ministerstwo
    /// okresowo je wymienia i zapisana na stałe wartość przestałaby działać.
    /// Przy rotacji dostępny bywa więcej niż jeden certyfikat, więc wybieramy
    /// ten o najpóźniejszej dacie ważności.
    /// </remarks>
    private async Task<(RSA Klucz, string Identyfikator)> KluczDoAsync(
        string przeznaczenie, CancellationToken anulowanie)
    {
        _certyfikaty ??= await GetAsync<List<CertyfikatPubliczny>>(
            "security/public-key-certificates", null, anulowanie);

        DateTimeOffset teraz = _czas.GetUtcNow();

        CertyfikatPubliczny? wybrany = _certyfikaty
            .Where(c => c.Przeznaczenie.Contains(przeznaczenie, StringComparer.Ordinal))
            .Where(c => c.WaznyOd <= teraz && c.WaznyDo >= teraz)
            .MaxBy(c => c.WaznyDo);

        if (wybrany is null)
        {
            throw new BladKsefException(
                $"KSeF nie udostępnia ważnego certyfikatu o przeznaczeniu " +
                $"'{przeznaczenie}'.");
        }

        return (Kryptografia.KluczPublicznyZCertyfikatu(
                    Convert.FromBase64String(wybrany.Certyfikat)),
                wybrany.IdentyfikatorKlucza);
    }

    // ------------------------------------------------------------ sesja online

    public async Task<string> OtworzSesjeAsync(CancellationToken anulowanie = default)
    {
        Kryptografia.KluczSesji klucz = Kryptografia.GenerujKluczSesji();
        (RSA kluczPubliczny, string identyfikator) =
            await KluczDoAsync(UzycieKluczSymetryczny, anulowanie);

        string zaszyfrowanyKlucz;
        using (kluczPubliczny)
        {
            zaszyfrowanyKlucz = Convert.ToBase64String(
                Kryptografia.SzyfrujRsa(klucz.Klucz, kluczPubliczny));
        }

        var zadanie = new ZadanieOtwarciaSesji(
            WzorFa3,
            new InformacjeSzyfrowania(
                zaszyfrowanyKlucz,
                Convert.ToBase64String(klucz.WektorInicjujacy),
                identyfikator));

        OdpowiedzSesji sesja = await PostAsync<ZadanieOtwarciaSesji, OdpowiedzSesji>(
            "sessions/online", zadanie, TokenDostepu(), anulowanie);

        _kluczSesji = klucz;
        _numerSesji = sesja.NumerReferencyjny;
        return sesja.NumerReferencyjny;
    }

    public async Task<string> WyslijFaktureAsync(byte[] xmlFaktury,
                                                 CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(xmlFaktury);

        if (_numerSesji is null || _kluczSesji is null)
        {
            throw new BladKsefException(
                "Sesja nie została otwarta - wywołaj OtworzSesjeAsync().");
        }

        byte[] zaszyfrowana = Kryptografia.SzyfrujAes(xmlFaktury, _kluczSesji);

        var zadanie = new ZadanieWyslaniaFaktury(
            Kryptografia.SkrotBase64(xmlFaktury),
            xmlFaktury.Length,
            Kryptografia.SkrotBase64(zaszyfrowana),
            zaszyfrowana.Length,
            Convert.ToBase64String(zaszyfrowana));

        OdpowiedzWyslania odpowiedz =
            await PostAsync<ZadanieWyslaniaFaktury, OdpowiedzWyslania>(
                $"sessions/online/{_numerSesji}/invoices", zadanie,
                TokenDostepu(), anulowanie);

        return odpowiedz.NumerReferencyjny;
    }

    public async Task<WynikWeryfikacji> SprawdzStatusAsync(
        string numerReferencyjnyFaktury, CancellationToken anulowanie = default)
    {
        if (_numerSesji is null)
        {
            throw new BladKsefException("Sesja nie została otwarta.");
        }

        OdpowiedzStanuFaktury stan = await GetAsync<OdpowiedzStanuFaktury>(
            $"sessions/{_numerSesji}/invoices/{numerReferencyjnyFaktury}",
            TokenDostepu(), anulowanie);

        return new WynikWeryfikacji(
            stan.NumerReferencyjny,
            stan.Status.Kod,
            stan.Status.Opis ?? string.Empty,
            stan.NumerKsef,
            stan.DataPrzyjecia,
            stan.Status.Szczegoly ?? []);
    }

    public async Task<WynikWeryfikacji> PoczekajNaWynikAsync(
        string numerReferencyjnyFaktury, CancellationToken anulowanie = default)
    {
        WynikWeryfikacji? ostatni = null;

        for (int proba = 0; proba < _maksProbOdpytywania; proba++)
        {
            ostatni = await SprawdzStatusAsync(numerReferencyjnyFaktury, anulowanie);
            if (!ostatni.WToku)
            {
                return ostatni;
            }

            await Task.Delay(_odstepOdpytywania, _czas, anulowanie);
        }

        return ostatni ?? throw new BladKsefException(
            "Nie udało się sprawdzić statusu faktury.");
    }

    public async Task ZamknijSesjeAsync(CancellationToken anulowanie = default)
    {
        if (_numerSesji is null)
        {
            return;
        }

        await PostAsync<object, object?>(
            $"sessions/online/{_numerSesji}/close", null, TokenDostepu(), anulowanie);

        _numerSesji = null;
        _kluczSesji = null;
    }

    public async Task<string> PobierzUpoAsync(string numerKsef,
                                              CancellationToken anulowanie = default)
    {
        if (_numerSesji is null)
        {
            throw new BladKsefException("Sesja nie została otwarta.");
        }

        return await GetTekstAsync(
            $"sessions/{_numerSesji}/invoices/ksef/{numerKsef}/upo",
            TokenDostepu(), anulowanie);
    }

    // ------------------------------------------------------- faktury zakupowe

    public async Task<IReadOnlyList<FakturaZakupowa>> PobierzFakturyZakupoweAsync(
        DateOnly dataOd, DateOnly dataDo, CancellationToken anulowanie = default)
    {
        var wyniki = new List<FakturaZakupowa>();

        var zapytanie = new ZapytanieOFaktury(
            // Subject2 oznacza, że interesują nas faktury, na których jesteśmy
            // nabywcą - czyli zakupowe.
            "Subject2",
            new ZakresDat(
                "Invoicing",
                new DateTimeOffset(dataOd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                new DateTimeOffset(dataDo.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)));

        for (int strona = 0; strona < 100; strona++)
        {
            OdpowiedzListyFaktur odpowiedz =
                await PostAsync<ZapytanieOFaktury, OdpowiedzListyFaktur>(
                    $"invoices/query/metadata?pageOffset={strona}&pageSize=100",
                    zapytanie, TokenDostepu(), anulowanie);

            foreach (MetadaneFaktury pozycja in odpowiedz.Faktury ?? [])
            {
                wyniki.Add(new FakturaZakupowa(
                    pozycja.NumerKsef,
                    pozycja.NumerFaktury ?? string.Empty,
                    pozycja.DataWystawienia,
                    pozycja.Sprzedawca?.Nazwa ?? string.Empty,
                    pozycja.Sprzedawca?.Nip ?? string.Empty,
                    pozycja.Netto,
                    pozycja.Vat,
                    pozycja.Brutto,
                    pozycja.Waluta ?? "PLN",
                    pozycja.DataPrzyjecia));
            }

            if (!odpowiedz.CzyWiecej)
            {
                break;
            }
        }

        return wyniki;
    }

    public async Task<byte[]> PobierzXmlFakturyAsync(string numerKsef,
                                                     CancellationToken anulowanie = default)
    {
        string tresc = await GetTekstAsync($"invoices/ksef/{numerKsef}",
            TokenDostepu(), anulowanie);
        return Encoding.UTF8.GetBytes(tresc);
    }

    // ------------------------------------------------------------- pomocnicze

    private string TokenDostepu() =>
        _tokenDostepu ?? throw new BladKsefException(
            "Brak tokena dostępowego - najpierw wywołaj UwierzytelnijAsync().");

    private static string OczyscNip(string nip) =>
        new((nip ?? string.Empty).Where(char.IsDigit).ToArray());

    private async Task<TOdpowiedz> PostAsync<TZadanie, TOdpowiedz>(
        string sciezka, TZadanie? tresc, string? token, CancellationToken anulowanie)
    {
        using var zadanie = new HttpRequestMessage(HttpMethod.Post, sciezka);
        if (tresc is not null)
        {
            zadanie.Content = JsonContent.Create(tresc, options: UstawieniaJson);
        }

        return await WyslijAsync<TOdpowiedz>(zadanie, token, anulowanie);
    }

    private async Task<TOdpowiedz> GetAsync<TOdpowiedz>(
        string sciezka, string? token, CancellationToken anulowanie)
    {
        using var zadanie = new HttpRequestMessage(HttpMethod.Get, sciezka);
        return await WyslijAsync<TOdpowiedz>(zadanie, token, anulowanie);
    }

    private async Task<string> GetTekstAsync(string sciezka, string? token,
                                             CancellationToken anulowanie)
    {
        using var zadanie = new HttpRequestMessage(HttpMethod.Get, sciezka);
        using HttpResponseMessage odpowiedz = await WykonajAsync(zadanie, token, anulowanie);
        return await odpowiedz.Content.ReadAsStringAsync(anulowanie);
    }

    private async Task<TOdpowiedz> WyslijAsync<TOdpowiedz>(
        HttpRequestMessage zadanie, string? token, CancellationToken anulowanie)
    {
        using HttpResponseMessage odpowiedz = await WykonajAsync(zadanie, token, anulowanie);

        // Część wywołań (np. zamknięcie sesji) nie zwraca treści.
        if (odpowiedz.Content.Headers.ContentLength is null or 0)
        {
            return default!;
        }

        TOdpowiedz? wynik = await odpowiedz.Content
            .ReadFromJsonAsync<TOdpowiedz>(UstawieniaJson, anulowanie);

        return wynik ?? throw new BladKsefException(
            "KSeF zwrócił pustą odpowiedź tam, gdzie oczekiwano danych.");
    }

    private async Task<HttpResponseMessage> WykonajAsync(
        HttpRequestMessage zadanie, string? token, CancellationToken anulowanie)
    {
        if (token is not null)
        {
            zadanie.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage odpowiedz;
        try
        {
            odpowiedz = await _http.SendAsync(zadanie, anulowanie);
        }
        catch (HttpRequestException blad)
        {
            throw new BladKsefException(
                $"Nie udało się połączyć z KSeF ({zadanie.RequestUri}): {blad.Message}",
                blad);
        }

        if (!odpowiedz.IsSuccessStatusCode)
        {
            string tresc = await odpowiedz.Content.ReadAsStringAsync(anulowanie);
            odpowiedz.Dispose();
            throw ZbudujBlad((int)odpowiedz.StatusCode, tresc);
        }

        return odpowiedz;
    }

    /// <summary>Wyciąga z odpowiedzi błędu opis zrozumiały dla użytkownika.</summary>
    private static BladKsefException ZbudujBlad(int kodHttp, string tresc)
    {
        var szczegoly = new List<string>();

        if (!string.IsNullOrWhiteSpace(tresc))
        {
            try
            {
                using JsonDocument dokument = JsonDocument.Parse(tresc);
                JsonElement korzen = dokument.RootElement;

                if (korzen.TryGetProperty("status", out JsonElement status))
                {
                    if (status.TryGetProperty("description", out JsonElement opis))
                    {
                        szczegoly.Add(opis.GetString() ?? string.Empty);
                    }

                    if (status.TryGetProperty("details", out JsonElement lista)
                        && lista.ValueKind == JsonValueKind.Array)
                    {
                        szczegoly.AddRange(lista.EnumerateArray()
                            .Select(e => e.GetString() ?? string.Empty));
                    }
                }

                foreach (string klucz in new[] { "title", "detail", "message" })
                {
                    if (korzen.TryGetProperty(klucz, out JsonElement wartosc)
                        && wartosc.ValueKind == JsonValueKind.String)
                    {
                        szczegoly.Add(wartosc.GetString() ?? string.Empty);
                    }
                }
            }
            catch (JsonException)
            {
                // Odpowiedź nie jest dokumentem JSON - pokazujemy ją tak, jak przyszła.
                szczegoly.Add(tresc.Length > 500 ? tresc[..500] : tresc);
            }
        }

        return new BladKsefException(
            $"KSeF odrzucił żądanie (HTTP {kodHttp})",
            kodHttp,
            szczegoly.Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
    }
}
