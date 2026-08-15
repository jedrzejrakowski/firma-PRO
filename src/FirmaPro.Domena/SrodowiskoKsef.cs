namespace FirmaPro.Domena;

/// <summary>
/// Środowiska Krajowego Systemu e-Faktur udostępniane przez Ministerstwo
/// Finansów.
/// </summary>
/// <remarks>
/// Typ leży w warstwie dziedziny, bo potrzebują go zarówno baza danych
/// (ustawienie firmy), jak i klient KSeF - a te dwie warstwy nie powinny
/// zależeć od siebie nawzajem.
/// </remarks>
public enum SrodowiskoKsef
{
    /// <summary>Testowe - do nauki i prób. Używaj losowych numerów NIP.</summary>
    Test,

    /// <summary>Przedprodukcyjne - konfiguracja zbliżona do produkcji.</summary>
    Demo,

    /// <summary>Produkcyjne - faktury o pełnej mocy prawnej.</summary>
    Produkcja
}

/// <summary>
/// Sposób, w jaki program przedstawia się systemowi KSeF.
/// </summary>
/// <remarks>
/// Tokeny działają do końca 2026 roku; od stycznia 2027 pozostaje wyłącznie
/// certyfikat. Obie drogi istnieją więc obok siebie, a firma wybiera, której
/// używa - dzięki temu przejście jest zmianą ustawienia, a nie awaryjną
/// aktualizacją programu pod termin.
/// </remarks>
public enum MetodaUwierzytelnieniaKsef
{
    /// <summary>Token wygenerowany w aplikacji podatnika.</summary>
    Token,

    /// <summary>Podpis certyfikatem - droga docelowa.</summary>
    Certyfikat
}

/// <summary>Adresy usług KSeF dla poszczególnych środowisk.</summary>
public static class AdresyKsef
{
    /// <summary>Adres API, do którego wysyłane są faktury.</summary>
    public static string Api(SrodowiskoKsef srodowisko) => srodowisko switch
    {
        SrodowiskoKsef.Test => "https://api-test.ksef.mf.gov.pl/v2",
        SrodowiskoKsef.Demo => "https://api-demo.ksef.mf.gov.pl/v2",
        SrodowiskoKsef.Produkcja => "https://api.ksef.mf.gov.pl/v2",
        _ => throw new ArgumentOutOfRangeException(nameof(srodowisko))
    };

    /// <summary>
    /// Adres używany w kodach QR na wizualizacji faktury - inny niż adres API.
    /// </summary>
    public static string KodQr(SrodowiskoKsef srodowisko) => srodowisko switch
    {
        SrodowiskoKsef.Test => "https://qr-test.ksef.mf.gov.pl",
        SrodowiskoKsef.Demo => "https://qr-demo.ksef.mf.gov.pl",
        SrodowiskoKsef.Produkcja => "https://qr.ksef.mf.gov.pl",
        _ => throw new ArgumentOutOfRangeException(nameof(srodowisko))
    };
}
