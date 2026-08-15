using FirmaPro.Domena;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Komunikaty dziennika zdarzeń.
/// </summary>
/// <remarks>
/// Definicje generowane są w czasie kompilacji (atrybut LoggerMessage), więc
/// wpis nie kosztuje nic, gdy dany poziom logowania jest wyłączony - w tym
/// nie sklejają się wtedy teksty komunikatów. Trzymanie ich w jednym miejscu
/// ułatwia też przegląd tego, co system w ogóle zapisuje.
/// </remarks>
internal static partial class Dziennik
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Wystawiono fakturę {numer} na kwotę {kwota}")]
    internal static partial void WystawionoFakture(ILogger dziennik, string numer, decimal kwota);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Nieudana wysyłka faktury {numer} do KSeF")]
    internal static partial void NieudanaWysylka(ILogger dziennik, Exception przyczyna, string numer);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Warning,
        Message = "Nie udało się pobrać poświadczenia odbioru faktury {numer}")]
    internal static partial void NieudanePobranieUpo(ILogger dziennik, Exception przyczyna,
                                                     string numer);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Założono dane demonstracyjne. Logowanie: {email}")]
    internal static partial void ZalozonoDaneDemonstracyjne(ILogger dziennik, string email);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Założono pierwsze konto właściciela: {email}")]
    internal static partial void ZalozonoPierwszeKonto(ILogger dziennik, string email);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Wysłano wiadomość do {adres}: {temat}")]
    internal static partial void WyslanoWiadomosc(ILogger dziennik, string adres, string temat);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Warning,
        Message = "Poczta nie jest skonfigurowana - wiadomość do {adres} " +
                  "(„{temat}\") nie została wysłana. Treść: {tresc}")]
    internal static partial void PocztaNieskonfigurowana(
        ILogger dziennik, string adres, string temat, string tresc);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "Sprawdzenie połączenia z KSeF nie przeszło kroku: {krok}")]
    internal static partial void NieudanaDiagnostyka(
        ILogger dziennik, Exception przyczyna, string krok);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Information,
        Message = "Sprawdzono połączenie z KSeF ({srodowisko}): " +
                  "udane={udalo}, błędów={ileBledow}")]
    internal static partial void ZakonczonaDiagnostyka(
        ILogger dziennik, SrodowiskoKsef srodowisko, bool udalo, int ileBledow);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Dane demonstracyjne włączone poza trybem deweloperskim. " +
                  "Konto {email} ma hasło jawnie wpisane w kodzie programu - " +
                  "usuń je, zanim wpuścisz kogokolwiek do tej instalacji.")]
    internal static partial void DaneDemonstracyjnePozaDeweloperskim(ILogger dziennik, string email);
}
