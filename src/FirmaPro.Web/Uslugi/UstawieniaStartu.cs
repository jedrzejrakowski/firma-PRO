namespace FirmaPro.Web.Uslugi;

/// <summary>Dane pierwszego konta właściciela, podane w ustawieniach.</summary>
public sealed record PierwszeKonto(string Email, string Haslo, string Firma, string Nip);

/// <summary>
/// Decyzje podejmowane przy starcie programu.
/// </summary>
/// <remarks>
/// Wydzielone z <c>Program.cs</c>, bo od nich zależy bezpieczeństwo
/// wdrożenia, a kodu w pliku startowym nie da się sprawdzić testem.
/// </remarks>
public static class UstawieniaStartu
{
    /// <summary>Najkrótsze dopuszczalne hasło pierwszego konta.</summary>
    public const int MinimalnaDlugoscHasla = 12;

    /// <summary>
    /// Czy zakładać konto i firmę demonstracyjną.
    /// </summary>
    /// <remarks>
    /// Konto demonstracyjne ma hasło wypisane w kodzie źródłowym. W trybie
    /// deweloperskim to wygoda, na serwerze - otwarte drzwi do ksiąg firmy.
    /// Domyślnie powstaje więc wyłącznie przy pracy nad programem; żeby
    /// założyć je gdzie indziej, trzeba tego zażądać wprost.
    /// </remarks>
    public static bool CzyZakladacDaneDemonstracyjne(IConfiguration ustawienia,
                                                     bool czyDeweloperskie)
    {
        ArgumentNullException.ThrowIfNull(ustawienia);

        string? wskazane = ustawienia["Aplikacja:DaneDemonstracyjne"];

        return string.IsNullOrWhiteSpace(wskazane)
            ? czyDeweloperskie
            : bool.TryParse(wskazane, out bool wybor) && wybor;
    }

    /// <summary>
    /// Katalog, w którym leżą klucze ochrony danych.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bez wskazania katalogu klucze powstają od nowa przy każdym starcie.
    /// W kontenerze oznacza to, że po podmianie obrazu <b>zaszyfrowany token
    /// KSeF przestaje się odczytywać</b> - a program zgłosi po prostu brak
    /// tokena, więc nikt nie skojarzy przyczyny ze skutkiem. Poza trybem
    /// deweloperskim brak katalogu jest zatem błędem, a nie ustawieniem
    /// domyślnym.
    /// </para>
    /// </remarks>
    public static string? KatalogKluczy(IConfiguration ustawienia, bool czyDeweloperskie)
    {
        ArgumentNullException.ThrowIfNull(ustawienia);

        string? katalog = ustawienia["Aplikacja:KatalogKluczy"];

        if (!string.IsNullOrWhiteSpace(katalog))
        {
            return katalog;
        }

        if (czyDeweloperskie)
        {
            return null;
        }

        throw new InvalidOperationException(
            "Nie wskazano katalogu kluczy ochrony (Aplikacja:KatalogKluczy). " +
            "Bez trwałych kluczy każdy restart programu unieważnia zapisany " +
            "token KSeF i wylogowuje wszystkich użytkowników. Katalog musi " +
            "leżeć na woluminie, który przeżyje wymianę kontenera.");
    }

    /// <summary>
    /// Dane pierwszego konta właściciela albo <c>null</c>, gdy ich nie podano.
    /// </summary>
    /// <remarks>
    /// Bez danych demonstracyjnych nowa instalacja nie miałaby żadnego konta
    /// i nie dałoby się do niej wejść. Zamiast zakładać konto z hasłem
    /// wpisanym w kodzie, program bierze je z ustawień wdrożenia.
    /// </remarks>
    public static PierwszeKonto? PierwszeKontoZUstawien(IConfiguration ustawienia)
    {
        ArgumentNullException.ThrowIfNull(ustawienia);

        string? email = ustawienia["Aplikacja:PierwszeKonto:Email"];
        string? haslo = ustawienia["Aplikacja:PierwszeKonto:Haslo"];

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(haslo))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Pierwsze konto wymaga poprawnego adresu e-mail " +
                "(Aplikacja:PierwszeKonto:Email).");
        }

        // Hasło do programu księgowego wystawionego w sieci. Krótkie hasło
        // odrzucamy przy starcie, a nie dopiero przy włamaniu.
        if (string.IsNullOrWhiteSpace(haslo) || haslo.Length < MinimalnaDlugoscHasla)
        {
            throw new InvalidOperationException(
                $"Hasło pierwszego konta musi mieć co najmniej {MinimalnaDlugoscHasla} " +
                "znaków (Aplikacja:PierwszeKonto:Haslo).");
        }

        string firma = ustawienia["Aplikacja:PierwszeKonto:Firma"] ?? string.Empty;
        string nip = new((ustawienia["Aplikacja:PierwszeKonto:Nip"] ?? string.Empty)
            .Where(char.IsDigit).ToArray());

        if (string.IsNullOrWhiteSpace(firma))
        {
            throw new InvalidOperationException(
                "Pierwsze konto wymaga nazwy firmy (Aplikacja:PierwszeKonto:Firma).");
        }

        return new PierwszeKonto(email.Trim(), haslo, firma.Trim(), nip);
    }
}
