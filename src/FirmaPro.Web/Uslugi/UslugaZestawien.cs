using System.Globalization;
using System.IO.Compression;
using System.Text;
using FirmaPro.Domena;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Zestawienia okresu dla biura rachunkowego.
/// </summary>
/// <remarks>
/// <para>
/// Księgowa nie zagląda do programu klienta - dostaje od niego pliki. Pytanie
/// brzmi więc nie „jak to pokazać na ekranie", tylko „co wysłać w załączniku,
/// żeby dało się z tym pracować bez przepisywania".
/// </para>
/// <para>
/// Stąd CSV, a nie PDF: plik ma się otworzyć w arkuszu i dać przenieść do
/// programu księgowego. PDF nadaje się do wydruku i do archiwum, ale nie do
/// pracy.
/// </para>
/// </remarks>
public sealed class UslugaZestawien(UslugaRejestruVat uslugaRejestru)
{
    /// <summary>
    /// Znak rozdzielający kolumny.
    /// </summary>
    /// <remarks>
    /// Średnik, nie przecinek. Excel w polskiej wersji językowej używa
    /// przecinka jako separatora dziesiętnego, więc plik rozdzielany
    /// przecinkami wczytuje się do jednej kolumny - i księgowa dostaje
    /// bełkot zamiast zestawienia.
    /// </remarks>
    private const char Rozdzielacz = ';';

    /// <summary>Nazwy plików w paczce.</summary>
    public const string PlikSprzedazy = "sprzedaz.csv";
    public const string PlikZakupow = "zakupy.csv";
    public const string PlikPodsumowania = "podsumowanie.txt";

    public async Task<RejestrVat> RejestrAsync(OkresRozliczeniowy okres,
                                               CancellationToken anulowanie = default) =>
        await uslugaRejestru.ZbudujAsync(okres, anulowanie);

    /// <summary>Zestawienie sprzedaży za okres.</summary>
    public static byte[] SprzedazCsv(RejestrVat rejestr)
    {
        ArgumentNullException.ThrowIfNull(rejestr);

        List<string[]> wiersze = [];

        wiersze.Add(["Numer", "Data wystawienia", "Data ujęcia", "Nabywca", "NIP",
                     "Netto", "VAT", "Brutto", "Numer KSeF"]);

        foreach (WpisSprzedazy wpis in rejestr.Sprzedaz.Wpisy)
        {
            wiersze.Add([
                wpis.Numer,
                Data(wpis.DataWystawienia),
                Data(wpis.DataUjecia),
                wpis.NabywcaNazwa,
                wpis.NabywcaNip ?? string.Empty,
                Kwota(wpis.RazemNetto),
                Kwota(wpis.RazemVat),
                Kwota(wpis.RazemBrutto),
                wpis.NumerKsef ?? string.Empty
            ]);
        }

        // Wiersz sumy na końcu - księgowa sprawdza po nim, czy plik doszedł
        // w całości, zanim zacznie cokolwiek przenosić.
        wiersze.Add([
            "RAZEM", string.Empty, string.Empty, string.Empty, string.Empty,
            Kwota(rejestr.Sprzedaz.RazemNetto),
            Kwota(rejestr.Sprzedaz.PodatekNalezny),
            Kwota(rejestr.Sprzedaz.RazemBrutto),
            string.Empty
        ]);

        return Csv(wiersze);
    }

    /// <summary>Zestawienie zakupów za okres.</summary>
    public static byte[] ZakupyCsv(RejestrVat rejestr)
    {
        ArgumentNullException.ThrowIfNull(rejestr);

        List<string[]> wiersze = [];

        wiersze.Add(["Numer", "Data wystawienia", "Data wpływu", "Data ujęcia", "Sprzedawca",
                     "NIP", "Rodzaj", "Odliczany", "Netto", "VAT", "VAT do odliczenia",
                     "Brutto"]);

        foreach (WpisZakupu wpis in rejestr.Zakupy.Wpisy)
        {
            wiersze.Add([
                wpis.Numer,
                Data(wpis.DataWystawienia),
                Data(wpis.DataWplywu),
                Data(wpis.DataUjecia),
                wpis.SprzedawcaNazwa,
                wpis.SprzedawcaNip ?? string.Empty,
                OpisRodzaju(wpis.Rodzaj),
                wpis.Odliczany ? "tak" : "nie",
                Kwota(wpis.Netto),
                Kwota(wpis.Vat),
                Kwota(wpis.VatDoOdliczenia),
                Kwota(wpis.Brutto)
            ]);
        }

        wiersze.Add([
            "RAZEM", string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty,
            Kwota(rejestr.Zakupy.RazemNetto),
            Kwota(rejestr.Zakupy.RazemVat),
            Kwota(rejestr.Zakupy.PodatekNaliczony),
            Kwota(rejestr.Zakupy.RazemNetto + rejestr.Zakupy.RazemVat)
        ]);

        return Csv(wiersze);
    }

    /// <summary>
    /// Podsumowanie okresu w postaci czytelnej dla człowieka.
    /// </summary>
    /// <remarks>
    /// Idzie do paczki jako zwykły tekst, żeby dało się je przeczytać bez
    /// otwierania czegokolwiek - także w podglądzie załącznika w poczcie.
    /// </remarks>
    public static byte[] Podsumowanie(RejestrVat rejestr, string nazwaFirmy, string nip)
    {
        ArgumentNullException.ThrowIfNull(rejestr);

        RozliczenieOkresu rozliczenie = rejestr.Rozliczenie;

        var wiersze = new List<string>
        {
            $"Zestawienie za okres: {rejestr.Okres.Nazwa}",
            $"Firma: {nazwaFirmy}",
            $"NIP: {nip}",
            string.Empty,
            "SPRZEDAŻ",
            $"  dokumentów:            {rejestr.Sprzedaz.Wpisy.Count}",
            $"  netto:                 {Kwoty.NaTekst(rejestr.Sprzedaz.RazemNetto)}",
            $"  podatek należny:       {Kwoty.NaTekst(rejestr.Sprzedaz.PodatekNalezny)}",
            string.Empty,
            "  w rozbiciu na stawki:"
        };

        foreach (KwotyWStawce kwoty in rejestr.Sprzedaz.WedlugStawek)
        {
            wiersze.Add($"    {kwoty.Stawka.Opis,-8} netto {Kwoty.NaTekst(kwoty.Netto),14}" +
                        $"   VAT {Kwoty.NaTekst(kwoty.Vat),14}");
        }

        wiersze.AddRange([
            string.Empty,
            "ZAKUPY",
            $"  dokumentów:            {rejestr.Zakupy.Wpisy.Count}",
            $"  netto:                 {Kwoty.NaTekst(rejestr.Zakupy.RazemNetto)}",
            $"  podatek naliczony:     {Kwoty.NaTekst(rejestr.Zakupy.PodatekNaliczony)}",
            string.Empty,
            "ROZLICZENIE OKRESU",
            $"  podatek należny:       {Kwoty.NaTekst(rozliczenie.PodatekNalezny)}",
            $"  podatek naliczony:     {Kwoty.NaTekst(rozliczenie.PodatekNaliczony)}",
            rozliczenie.DoZaplaty > 0
                ? $"  do zapłaty:            {Kwoty.NaTekst(rozliczenie.DoZaplaty)}"
                : $"  nadwyżka do przeniesienia: {Kwoty.NaTekst(rozliczenie.Nadwyzka)}",
            string.Empty,
            "Kwoty dotyczą wyłącznie tego okresu. Nadwyżka z okresów wcześniejszych",
            "uwzględniana jest dopiero w deklaracji JPK_V7.",
            string.Empty,
            $"Wygenerowano programem Firma PRO."
        ]);

        return ZBom(string.Join(Environment.NewLine, wiersze) + Environment.NewLine);
    }

    /// <summary>
    /// Wszystko za jeden okres w jednym pliku do wysłania.
    /// </summary>
    /// <remarks>
    /// Jeden załącznik zamiast trzech: mniej okazji, żeby wysłać komplet bez
    /// jednego pliku albo pomylić okresy między załącznikami.
    /// </remarks>
    public static byte[] Paczka(RejestrVat rejestr, string nazwaFirmy, string nip)
    {
        ArgumentNullException.ThrowIfNull(rejestr);

        using var strumien = new MemoryStream();

        using (var archiwum = new ZipArchive(strumien, ZipArchiveMode.Create, leaveOpen: true))
        {
            Dodaj(archiwum, PlikSprzedazy, SprzedazCsv(rejestr));
            Dodaj(archiwum, PlikZakupow, ZakupyCsv(rejestr));
            Dodaj(archiwum, PlikPodsumowania, Podsumowanie(rejestr, nazwaFirmy, nip));
        }

        return strumien.ToArray();
    }

    /// <summary>Nazwa pliku z okresem w środku - żeby nie pomylić miesięcy.</summary>
    public static string NazwaPliku(string przedrostek, OkresRozliczeniowy okres,
                                    string rozszerzenie)
    {
        ArgumentNullException.ThrowIfNull(okres);

        return $"{przedrostek}-{okres.Kod}.{rozszerzenie}";
    }

    // ------------------------------------------------------------ pomocnicze

    private static void Dodaj(ZipArchive archiwum, string nazwa, byte[] zawartosc)
    {
        ZipArchiveEntry wpis = archiwum.CreateEntry(nazwa, CompressionLevel.Optimal);

        using Stream strumien = wpis.Open();
        strumien.Write(zawartosc, 0, zawartosc.Length);
    }

    private static byte[] Csv(IReadOnlyList<string[]> wiersze)
    {
        var budowniczy = new StringBuilder();

        foreach (string[] wiersz in wiersze)
        {
            budowniczy.AppendLine(string.Join(Rozdzielacz, wiersz.Select(Pole)));
        }

        return ZBom(budowniczy.ToString());
    }

    /// <summary>
    /// Ujmuje pole w cudzysłowy, gdy trzeba.
    /// </summary>
    /// <remarks>
    /// Nazwa firmy potrafi zawierać średnik („Kowalski; Nowak s.c."), a wtedy
    /// bez cudzysłowów rozjeżdża się cały wiersz. Cudzysłów w środku podwaja
    /// się - tak wymaga format.
    /// </remarks>
    private static string Pole(string wartosc)
    {
        string tekst = wartosc ?? string.Empty;

        bool trzeba = tekst.Contains(Rozdzielacz, StringComparison.Ordinal)
                      || tekst.Contains('"', StringComparison.Ordinal)
                      || tekst.Contains('\n', StringComparison.Ordinal)
                      || tekst.Contains('\r', StringComparison.Ordinal);

        return trzeba
            ? '"' + tekst.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : tekst;
    }

    /// <summary>
    /// Zapisuje tekst w UTF-8 ze znacznikiem kolejności bajtów.
    /// </summary>
    /// <remarks>
    /// Bez znacznika Excel czyta plik w kodowaniu systemowym i polskie znaki
    /// zamieniają się w krzaki. To najczęstsza przyczyna „program wygenerował
    /// mi bzdury" przy plikach CSV.
    /// </remarks>
    private static byte[] ZBom(string tekst) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(tekst)];

    private static string Data(DateOnly data) =>
        data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Kwota w zapisie, który polski Excel rozpozna jako liczbę.
    /// </summary>
    /// <remarks>
    /// Przecinek dziesiętny i brak separatora tysięcy - spacja rozdzielająca
    /// tysiące jest czytelna dla człowieka, ale arkusz uznałby ją za tekst.
    /// </remarks>
    private static string Kwota(decimal kwota) =>
        kwota.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string OpisRodzaju(RodzajZakupu rodzaj) => rodzaj switch
    {
        RodzajZakupu.SrodkiTrwale => "środki trwałe",
        RodzajZakupu.TowaryIUslugi => "towary i usługi",
        _ => rodzaj.ToString()
    };
}
