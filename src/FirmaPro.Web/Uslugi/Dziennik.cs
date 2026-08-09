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
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Założono dane demonstracyjne. Logowanie: {email}")]
    internal static partial void ZalozonoDaneDemonstracyjne(ILogger dziennik, string email);
}
