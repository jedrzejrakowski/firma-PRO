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

        string nip = new((nipSprzedawcy ?? string.Empty).Where(char.IsDigit).ToArray());
        // W kodzie QR data zapisywana jest jako dzień-miesiąc-rok, inaczej
        // niż w samym dokumencie XML.
        string data = dataWystawienia.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        string skrot = Kryptografia.SkrotBase64Url(xmlFaktury);

        return $"{AdresyKsef.KodQr(srodowisko)}/invoice/{nip}/{data}/{skrot}";
    }
}
