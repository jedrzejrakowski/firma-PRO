using FirmaPro.Domena;
using FirmaPro.Ksef;

namespace FirmaPro.Wydruk;

/// <summary>
/// Dodatki do wizualizacji, których nie ma w samej fakturze.
/// </summary>
/// <remarks>
/// Numer KSeF i kod weryfikacyjny powstają dopiero po przyjęciu dokumentu
/// przez system, więc nie należą do modelu faktury - dokłada je warstwa
/// wywołująca wydruk.
/// </remarks>
public sealed record OpcjeWydruku
{
    /// <summary>Numer nadany przez KSeF po przyjęciu faktury.</summary>
    public string? NumerKsef { get; init; }

    /// <summary>
    /// Link weryfikacyjny KOD I, zamieniany na kod QR.
    /// </summary>
    /// <remarks>
    /// Buduje go <see cref="KodyQr.LinkWeryfikacyjny"/> ze skrótu pliku XML,
    /// który faktycznie trafił do KSeF. Gdy faktura nie została jeszcze
    /// wysłana, link ma być pusty - kod prowadzący donikąd byłby gorszy
    /// niż jego brak.
    /// </remarks>
    public string? LinkWeryfikacyjny { get; init; }

    /// <summary>
    /// Oznaczenie egzemplarza, np. „Oryginał" albo „Duplikat".
    /// </summary>
    public string? Egzemplarz { get; init; }

    /// <summary>
    /// Czy dokument jest dopiero projektem, nieobecnym w KSeF.
    /// </summary>
    /// <remarks>
    /// Wydruk takiego dokumentu dostaje wyraźne ostrzeżenie. Faktura zaczyna
    /// istnieć w obrocie prawnym dopiero po przyjęciu przez KSeF, więc kartka
    /// wyglądająca jak gotowa faktura, a niebędąca nią, wprowadzałaby
    /// odbiorcę w błąd.
    /// </remarks>
    public bool Projekt { get; init; }

    /// <summary>
    /// Buduje opcje dla faktury przyjętej przez KSeF.
    /// </summary>
    /// <param name="numerKsef">Numer nadany przez KSeF.</param>
    /// <param name="nipSprzedawcy">NIP wystawcy - część linku weryfikacyjnego.</param>
    /// <param name="dataWystawienia">Data z pola P_1.</param>
    /// <param name="xmlWyslany">Bajty pliku przesłanego do KSeF.</param>
    /// <param name="srodowisko">Środowisko, w którym wystawiono fakturę.</param>
    public static OpcjeWydruku DlaPrzyjetej(string numerKsef,
                                            string nipSprzedawcy,
                                            DateOnly dataWystawienia,
                                            byte[] xmlWyslany,
                                            SrodowiskoKsef srodowisko) =>
        new()
        {
            NumerKsef = numerKsef,
            LinkWeryfikacyjny = KodyQr.LinkWeryfikacyjny(
                nipSprzedawcy, dataWystawienia, xmlWyslany, srodowisko),
            Egzemplarz = "Oryginał"
        };

    /// <summary>
    /// Buduje opcje dla faktury przyjętej przez KSeF, na podstawie zapisanego
    /// skrótu przesłanego pliku.
    /// </summary>
    /// <remarks>
    /// Tej wersji używa program przy wystawianiu wizualizacji po czasie:
    /// plik XML wygenerowany ponownie miałby inny znacznik czasu wytworzenia,
    /// a więc i inny skrót, przez co kod QR prowadziłby donikąd.
    /// </remarks>
    public static OpcjeWydruku DlaPrzyjetejZeSkrotu(string numerKsef,
                                                    string nipSprzedawcy,
                                                    DateOnly dataWystawienia,
                                                    string skrotBase64,
                                                    SrodowiskoKsef srodowisko) =>
        new()
        {
            NumerKsef = numerKsef,
            LinkWeryfikacyjny = KodyQr.LinkWeryfikacyjnyZeSkrotu(
                nipSprzedawcy, dataWystawienia, skrotBase64, srodowisko),
            Egzemplarz = "Oryginał"
        };

    /// <summary>Buduje opcje dla dokumentu, którego nie ma jeszcze w KSeF.</summary>
    public static OpcjeWydruku DlaProjektu() => new() { Projekt = true };

    /// <summary>
    /// Zmienia wydruk w duplikat wystawiony we wskazanym dniu.
    /// </summary>
    /// <remarks>
    /// Duplikat to ten sam dokument, wydany ponownie - różni go wyłącznie
    /// oznaczenie i data wystawienia egzemplarza (art. 106l ustawy). Treść
    /// faktury pozostaje bez zmian, więc kod QR i numer KSeF zostają te same.
    /// </remarks>
    public OpcjeWydruku JakoDuplikat(DateOnly dataWystawienia) => this with
    {
        Egzemplarz = $"Duplikat z dnia {dataWystawienia:yyyy-MM-dd}"
    };
}
