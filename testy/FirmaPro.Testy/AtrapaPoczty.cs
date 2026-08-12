using FirmaPro.Web.Uslugi;

namespace FirmaPro.Testy;

/// <summary>
/// Poczta, która niczego nie wysyła, tylko zapamiętuje wiadomości.
/// </summary>
/// <remarks>
/// Test sprawdza dzięki temu to, co naprawdę poszłoby do kontrahenta:
/// adres, temat, treść i załączniki - łącznie z tym, czy PDF jest
/// prawdziwym plikiem PDF.
/// </remarks>
public sealed class AtrapaPoczty : INadawcaPoczty
{
    public sealed record Wiadomosc(
        string Adres, string Temat, string Tresc, IReadOnlyList<Zalacznik> Zalaczniki);

    public List<Wiadomosc> Wyslane { get; } = [];

    /// <summary>Pozwala udawać instalację bez skonfigurowanej poczty.</summary>
    public bool Dziala { get; set; } = true;

    /// <summary>
    /// Udaje niedostępny serwer poczty.
    /// </summary>
    /// <remarks>
    /// Serwer poczty bywa wyłączony albo odrzuca hasło - to zwykłe zdarzenie,
    /// nie awaria programu. Test sprawdza, że użytkownik dostaje wtedy
    /// komunikat, a nie stronę błędu.
    /// </remarks>
    public bool Awaria { get; set; }

    public Task WyslijAsync(string adres, string temat, string tresc,
                            IReadOnlyList<Zalacznik>? zalaczniki = null,
                            CancellationToken anulowanie = default)
    {
        if (Awaria)
        {
            throw new BladPocztyException("Serwer poczty nie odpowiada.",
                new InvalidOperationException("atrapa"));
        }

        Wyslane.Add(new Wiadomosc(adres, temat, tresc, zalaczniki ?? []));
        return Task.CompletedTask;
    }
}
