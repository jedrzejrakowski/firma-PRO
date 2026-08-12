using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FirmaPro.Web.Uslugi;

/// <summary>Dane serwera poczty wychodzącej.</summary>
public sealed record UstawieniaPoczty(
    string Serwer,
    int Port,
    string? Uzytkownik,
    string? Haslo,
    string AdresNadawcy,
    string NazwaNadawcy,
    bool SzyfrowanieOdRazu,
    bool BezSzyfrowania)
{
    /// <summary>
    /// Czyta ustawienia poczty; <c>null</c>, gdy nie skonfigurowano serwera.
    /// </summary>
    /// <remarks>
    /// Brak poczty jest dopuszczalny - program działa bez niej, tylko
    /// zaproszenia i zmianę hasła trzeba wtedy przekazywać odnośnikiem.
    /// </remarks>
    public static UstawieniaPoczty? ZUstawien(IConfiguration ustawienia)
    {
        ArgumentNullException.ThrowIfNull(ustawienia);

        string? serwer = ustawienia["Poczta:Serwer"];
        if (string.IsNullOrWhiteSpace(serwer))
        {
            return null;
        }

        string? nadawca = ustawienia["Poczta:AdresNadawcy"];
        if (string.IsNullOrWhiteSpace(nadawca))
        {
            throw new InvalidOperationException(
                "Skonfigurowano serwer poczty, ale nie podano adresu nadawcy " +
                "(Poczta:AdresNadawcy). Poczta bez nadawcy zostanie odrzucona " +
                "przez serwer odbiorcy.");
        }

        // Port 465 to szyfrowanie od pierwszego bajtu, 587 - przejście na
        // szyfrowanie po nawiązaniu połączenia. To najczęstszy podział.
        int port = int.TryParse(ustawienia["Poczta:Port"], out int wskazany) ? wskazany : 587;

        // Połączenie bez szyfrowania ma sens wyłącznie do przekaźnika
        // stojącego na tej samej maszynie. Domyślnie wymagamy szyfrowania,
        // bo poza taką sytuacją hasło do poczty szłoby otwartym tekstem.
        bool bezSzyfrowania =
            bool.TryParse(ustawienia["Poczta:BezSzyfrowania"], out bool jawnie) && jawnie;

        return new UstawieniaPoczty(
            serwer.Trim(),
            port,
            ustawienia["Poczta:Uzytkownik"],
            ustawienia["Poczta:Haslo"],
            nadawca.Trim(),
            ustawienia["Poczta:NazwaNadawcy"] ?? "Firma PRO",
            port == 465,
            bezSzyfrowania);
    }
}

/// <summary>Plik dołączany do wiadomości.</summary>
public sealed record Zalacznik(string Nazwa, byte[] Dane, string TypTresci);

/// <summary>Wysyłanie wiadomości do użytkowników.</summary>
public interface INadawcaPoczty
{
    /// <summary>
    /// Czy poczta jest skonfigurowana i wiadomość naprawdę gdzieś poleci.
    /// </summary>
    /// <remarks>
    /// Ekrany pytają o to, zanim obiecają użytkownikowi wysyłkę. Obietnica
    /// „wysłaliśmy wiadomość", po której nic nie przychodzi, jest gorsza niż
    /// uczciwe „poczta nie jest skonfigurowana".
    /// </remarks>
    bool Dziala { get; }

    Task WyslijAsync(string adres, string temat, string tresc,
                     IReadOnlyList<Zalacznik>? zalaczniki = null,
                     CancellationToken anulowanie = default);
}

/// <inheritdoc />
public sealed class NadawcaSmtp(UstawieniaPoczty ustawienia, ILogger<NadawcaSmtp> dziennik)
    : INadawcaPoczty
{
    public bool Dziala => true;

    public async Task WyslijAsync(string adres, string temat, string tresc,
                                  IReadOnlyList<Zalacznik>? zalaczniki = null,
                                  CancellationToken anulowanie = default)
    {
        var wiadomosc = new MimeMessage();
        wiadomosc.From.Add(new MailboxAddress(ustawienia.NazwaNadawcy, ustawienia.AdresNadawcy));
        wiadomosc.To.Add(MailboxAddress.Parse(adres));
        wiadomosc.Subject = temat;

        var tresci = new BodyBuilder { TextBody = tresc };

        foreach (Zalacznik zalacznik in zalaczniki ?? [])
        {
            tresci.Attachments.Add(zalacznik.Nazwa, zalacznik.Dane,
                ContentType.Parse(zalacznik.TypTresci));
        }

        wiadomosc.Body = tresci.ToMessageBody();

        using var klient = new SmtpClient();

        await klient.ConnectAsync(
            ustawienia.Serwer,
            ustawienia.Port,
            ustawienia switch
            {
                { BezSzyfrowania: true } => SecureSocketOptions.None,
                { SzyfrowanieOdRazu: true } => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTls
            },
            anulowanie);

        if (!string.IsNullOrWhiteSpace(ustawienia.Uzytkownik))
        {
            await klient.AuthenticateAsync(
                ustawienia.Uzytkownik, ustawienia.Haslo ?? string.Empty, anulowanie);
        }

        await klient.SendAsync(wiadomosc, anulowanie);
        await klient.DisconnectAsync(quit: true, anulowanie);

        // Do dziennika trafia sam fakt wysyłki i temat. Treść bywa nośnikiem
        // odnośnika działającego jak hasło, więc nigdzie jej nie zapisujemy.
        Dziennik.WyslanoWiadomosc(dziennik, adres, temat);
    }
}

/// <summary>
/// Zastępnik używany, gdy nie skonfigurowano poczty.
/// </summary>
/// <remarks>
/// Zamiast wysyłać, zapisuje wiadomość w dzienniku - przy pracy nad
/// programem to wygodne, a na serwerze nie zdarza się, bo ekrany zależne od
/// poczty same się wyłączają, gdy jej nie ma.
/// </remarks>
public sealed class NadawcaDoDziennika(ILogger<NadawcaDoDziennika> dziennik) : INadawcaPoczty
{
    public bool Dziala => false;

    public Task WyslijAsync(string adres, string temat, string tresc,
                            IReadOnlyList<Zalacznik>? zalaczniki = null,
                            CancellationToken anulowanie = default)
    {
        Dziennik.PocztaNieskonfigurowana(dziennik, adres, temat, tresc);
        return Task.CompletedTask;
    }
}
