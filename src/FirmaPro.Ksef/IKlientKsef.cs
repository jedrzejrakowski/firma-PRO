using FirmaPro.Domena;

namespace FirmaPro.Ksef;

/// <summary>Błąd zgłoszony przez KSeF albo problem z połączeniem.</summary>
public sealed class BladKsefException : Exception
{
    public BladKsefException(string komunikat) : base(komunikat)
    {
    }

    public BladKsefException(string komunikat, Exception przyczyna)
        : base(komunikat, przyczyna)
    {
    }

    public BladKsefException(string komunikat, int? kodHttp,
                             IReadOnlyList<string>? szczegoly)
        : base(komunikat)
    {
        KodHttp = kodHttp;
        Szczegoly = szczegoly ?? [];
    }

    /// <summary>Kod odpowiedzi HTTP, jeśli błąd pochodzi z API.</summary>
    public int? KodHttp { get; }

    /// <summary>Szczegóły zwrócone przez system - zwykle wskazują konkretne pole.</summary>
    public IReadOnlyList<string> Szczegoly { get; } = [];

    /// <summary>Pełny opis wraz ze szczegółami - do pokazania użytkownikowi.</summary>
    public string PelnyOpis() =>
        Szczegoly.Count == 0
            ? Message
            : Message + Environment.NewLine +
              string.Join(Environment.NewLine, Szczegoly.Select(s => "  - " + s));
}

/// <summary>Wynik weryfikacji faktury przez KSeF.</summary>
public sealed record WynikWeryfikacji(
    string NumerReferencyjny,
    int Kod,
    string Opis,
    string? NumerKsef,
    DateTimeOffset? DataPrzyjecia,
    IReadOnlyList<string> Szczegoly)
{
    /// <summary>Faktura przyjęta - nadany numer KSeF.</summary>
    public bool Przyjeta => Kod == 200 && !string.IsNullOrWhiteSpace(NumerKsef);

    /// <summary>Faktura odrzucona - dalsze czekanie nie ma sensu.</summary>
    public bool Odrzucona => Kod >= 300;

    /// <summary>Weryfikacja wciąż trwa.</summary>
    public bool WToku => !Przyjeta && !Odrzucona;
}

/// <summary>
/// Stan środowiska KSeF sprawdzony bez użycia tokena.
/// </summary>
/// <remarks>
/// Pobranie certyfikatów to jedyne wywołanie, które nie wymaga
/// uwierzytelnienia - nadaje się więc na sprawdzenie, czy program w ogóle
/// dociera do serwera, zanim zacznie podejrzewać token.
/// </remarks>
public sealed record StanSrodowiska(
    string Adres,
    int IleCertyfikatow,
    bool MaKluczDoTokena,
    bool MaKluczDoSesji,
    DateTimeOffset? NajblizszeWygasniecie);

/// <summary>Podsumowanie faktury zakupowej pobranej z systemu.</summary>
public sealed record FakturaZakupowa(
    string NumerKsef,
    string Numer,
    DateOnly? DataWystawienia,
    string SprzedawcaNazwa,
    string SprzedawcaNip,
    decimal Netto,
    decimal Vat,
    decimal Brutto,
    string Waluta,
    DateTimeOffset? DataPrzyjecia);

/// <summary>
/// Komunikacja z Krajowym Systemem e-Faktur.
/// </summary>
/// <remarks>
/// <para>
/// Reszta systemu widzi wyłącznie ten interfejs, nigdy konkretnej
/// implementacji. Dzięki temu można podmienić sposób rozmowy z KSeF - na
/// przykład na bibliotekę wydawaną przez Ministerstwo Finansów - bez
/// dotykania warstwy aplikacji.
/// </para>
/// <para>
/// Obsługiwaną metodą uwierzytelniania jest <b>token KSeF</b>. Token generuje
/// się raz w aplikacji webowej KSeF i od tego momentu system działa bez
/// udziału podpisu kwalifikowanego. Druga dopuszczalna metoda (podpis XAdES)
/// wymaga certyfikatu kwalifikowanego i nie jest tu zaimplementowana.
/// </para>
/// </remarks>
public interface IKlientKsef
{
    /// <summary>Środowisko, z którym rozmawia ten egzemplarz klienta.</summary>
    SrodowiskoKsef Srodowisko { get; }

    /// <summary>
    /// Sprawdza, czy środowisko odpowiada i udostępnia ważne klucze.
    /// </summary>
    /// <remarks>
    /// Wywołanie nie wymaga tokena, więc rozdziela dwie zupełnie różne
    /// przyczyny niepowodzenia: „program nie dociera do KSeF" i „KSeF nie
    /// uznaje tokena". Bez tego rozróżnienia pierwsze uruchomienie u klienta
    /// sprowadza się do zgadywania.
    /// </remarks>
    Task<StanSrodowiska> SprawdzSrodowiskoAsync(CancellationToken anulowanie = default);

    /// <summary>
    /// Uwierzytelnia się tokenem KSeF i zapamiętuje token dostępowy.
    /// </summary>
    /// <param name="nip">NIP firmy, w imieniu której działa system.</param>
    /// <param name="tokenKsef">Token wygenerowany w aplikacji webowej KSeF.</param>
    Task UwierzytelnijAsync(string nip, string tokenKsef,
                            CancellationToken anulowanie = default);

    /// <summary>Otwiera sesję interaktywną i zwraca jej numer referencyjny.</summary>
    Task<string> OtworzSesjeAsync(CancellationToken anulowanie = default);

    /// <summary>Wysyła jedną fakturę i zwraca jej numer referencyjny w sesji.</summary>
    Task<string> WyslijFaktureAsync(byte[] xmlFaktury,
                                    CancellationToken anulowanie = default);

    /// <summary>Sprawdza jednorazowo wynik weryfikacji faktury.</summary>
    Task<WynikWeryfikacji> SprawdzStatusAsync(string numerReferencyjnyFaktury,
                                              CancellationToken anulowanie = default);

    /// <summary>
    /// Odpytuje o status, aż faktura zostanie przyjęta albo odrzucona.
    /// </summary>
    /// <remarks>
    /// Weryfikacja jest asynchroniczna: zaraz po wysłaniu system zwraca stan
    /// pośredni, a numer KSeF pojawia się dopiero po pozytywnym sprawdzeniu.
    /// </remarks>
    Task<WynikWeryfikacji> PoczekajNaWynikAsync(string numerReferencyjnyFaktury,
                                                CancellationToken anulowanie = default);

    /// <summary>Zamyka sesję, co uruchamia generowanie zbiorczego UPO.</summary>
    Task ZamknijSesjeAsync(CancellationToken anulowanie = default);

    /// <summary>Pobiera urzędowe poświadczenie odbioru faktury.</summary>
    Task<string> PobierzUpoAsync(string numerKsef, CancellationToken anulowanie = default);

    /// <summary>Pobiera listę faktur wystawionych na nasz NIP w danym okresie.</summary>
    Task<IReadOnlyList<FakturaZakupowa>> PobierzFakturyZakupoweAsync(
        DateOnly dataOd, DateOnly dataDo, CancellationToken anulowanie = default);

    /// <summary>Pobiera oryginalny plik XML faktury o podanym numerze KSeF.</summary>
    Task<byte[]> PobierzXmlFakturyAsync(string numerKsef,
                                        CancellationToken anulowanie = default);
}
