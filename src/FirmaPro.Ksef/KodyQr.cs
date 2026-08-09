using System.Buffers.Text;
using System.Globalization;
using FirmaPro.Domena;

namespace FirmaPro.Ksef;

/// <summary>
/// Kody QR weryfikujące fakturę (KOD I).
/// </summary>
/// <remarks>
/// <para>
/// Gdy faktura trafia do odbiorcy inaczej niż przez KSeF - jako PDF, wydruk
/// albo załącznik do wiadomości - musi mieć kod QR pozwalający sprawdzić, czy
/// taki dokument rzeczywiście jest w systemie i czy nie został zmieniony.
/// </para>
/// <para>Link KOD I ma postać:</para>
/// <code>
/// {adres środowiska}/invoice/{NIP sprzedawcy}/{DD-MM-RRRR}/{skrót faktury}
/// </code>
/// <para>
/// Skrótem jest SHA-256 pliku XML zapisany w Base64URL. Liczy się skrót
/// dokładnie tych bajtów, które trafiły do KSeF - dlatego wizualizację robi
/// się z tego samego pliku, który został wysłany, a nie z odtworzonego
/// na nowo.
/// </para>
/// <para>
/// Sam obraz kodu powstaje w warstwie wizualizacji; tutaj budowany jest
/// wyłącznie adres, bo to on decyduje o poprawności weryfikacji.
/// </para>
/// </remarks>
public static class KodyQr
{
    /// <summary>Buduje link KOD I do weryfikacji faktury w KSeF.</summary>
    /// <param name="nipSprzedawcy">NIP wystawcy faktury.</param>
    /// <param name="dataWystawienia">Data z pola P_1 faktury.</param>
    /// <param name="xmlFaktury">Bajty pliku wysłanego do KSeF.</param>
    /// <param name="srodowisko">Środowisko, w którym wystawiono fakturę.</param>
    public static string LinkWeryfikacyjny(string nipSprzedawcy,
                                           DateOnly dataWystawienia,
                                           byte[] xmlFaktury,
                                           SrodowiskoKsef srodowisko)
    {
        ArgumentNullException.ThrowIfNull(xmlFaktury);

        return ZlozLink(nipSprzedawcy, dataWystawienia,
                        Kryptografia.SkrotBase64Url(xmlFaktury), srodowisko);
    }

    /// <summary>
    /// Buduje link KOD I na podstawie zapisanego skrótu faktury.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wersji z gotowym skrótem używa się przy wystawianiu wizualizacji po
    /// czasie. Pliku XML nie da się odtworzyć bajt w bajt - zawiera znacznik
    /// czasu wytworzenia, więc wygenerowany ponownie miałby inny skrót, a kod
    /// QR prowadziłby do dokumentu, którego KSeF nie zna.
    /// </para>
    /// <para>
    /// Dlatego przy wysyłce zapisujemy skrót przesłanych bajtów i to on jest
    /// tu źródłem prawdy.
    /// </para>
    /// </remarks>
    /// <param name="nipSprzedawcy">NIP wystawcy faktury.</param>
    /// <param name="dataWystawienia">Data z pola P_1 faktury.</param>
    /// <param name="skrotBase64">Skrót SHA-256 przesłanego pliku, zapisany w Base64.</param>
    /// <param name="srodowisko">Środowisko, w którym wystawiono fakturę.</param>
    public static string LinkWeryfikacyjnyZeSkrotu(string nipSprzedawcy,
                                                   DateOnly dataWystawienia,
                                                   string skrotBase64,
                                                   SrodowiskoKsef srodowisko)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skrotBase64);

        byte[] skrot = Convert.FromBase64String(skrotBase64);
        if (skrot.Length != 32)
        {
            throw new ArgumentException(
                "Skrót SHA-256 ma 32 bajty; podana wartość ma " + skrot.Length + ".",
                nameof(skrotBase64));
        }

        return ZlozLink(nipSprzedawcy, dataWystawienia,
                        Base64Url.EncodeToString(skrot), srodowisko);
    }

    private static string ZlozLink(string nipSprzedawcy,
                                   DateOnly dataWystawienia,
                                   string skrotBase64Url,
                                   SrodowiskoKsef srodowisko)
    {
        string nip = new((nipSprzedawcy ?? string.Empty).Where(char.IsDigit).ToArray());

        // W kodzie QR data zapisywana jest jako dzień-miesiąc-rok, inaczej
        // niż w samym dokumencie XML.
        string data = dataWystawienia.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        return $"{AdresyKsef.KodQr(srodowisko)}/invoice/{nip}/{data}/{skrotBase64Url}";
    }
}
