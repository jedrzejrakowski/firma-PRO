using System.IO.Compression;
using System.Text;
using FirmaPro.Domena;
using FirmaPro.Web.Uslugi;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>
/// Zestawienia dla biura rachunkowego.
/// </summary>
/// <remarks>
/// Testy pilnują przede wszystkim formatu pliku, bo to on decyduje, czy
/// księgowa zobaczy tabelę, czy jedną kolumnę krzaków. Kwoty są tu drugorzędne
/// - liczy je rejestr VAT, obłożony własnymi testami.
/// </remarks>
public sealed class TestyZestawien
{
    private static RejestrVat Rejestr()
    {
        var okres = OkresRozliczeniowy.Miesiac(2026, 8);

        WpisSprzedazy sprzedaz = new(
            "FV/2026/08/1",
            new DateOnly(2026, 8, 12),
            new DateOnly(2026, 8, 12),
            "Auto-Serwis Nowak sp. j.",
            "1180000001",
            "1180000001-20260812-0AAAAA-BBBBBB-CC",
            [new KwotyWStawce(StawkaVat.Vat23, 1000m, 230m)]);

        WpisZakupu zakup = new(
            "ZAK/2026/9",
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 5),
            new DateOnly(2026, 8, 3),
            "Dostawca Prądu S.A.",
            "7010001453",
            RodzajZakupu.TowaryIUslugi,
            Odliczany: true,
            500m,
            115m);

        return RejestrVat.Zbuduj(okres, [sprzedaz], [zakup]);
    }

    private static string Tekst(byte[] plik) =>
        Encoding.UTF8.GetString(plik, Encoding.UTF8.GetPreamble().Length,
            plik.Length - Encoding.UTF8.GetPreamble().Length);

    /// <summary>
    /// Bez znacznika kolejności bajtów Excel czyta plik w kodowaniu systemowym
    /// i polskie znaki zamieniają się w krzaki.
    /// </summary>
    [Fact]
    public void PlikZaczynaSieOdZnacznikaKodowania()
    {
        byte[] plik = UslugaZestawien.SprzedazCsv(Rejestr());

        Assert.True(plik.Length > 3);
        Assert.Equal(Encoding.UTF8.GetPreamble(), plik[..3]);
    }

    /// <summary>
    /// Kolumny rozdziela średnik, a nie przecinek.
    /// </summary>
    /// <remarks>
    /// Przecinek jest w polskim Excelu separatorem dziesiętnym - plik nim
    /// rozdzielany wczytuje się w całości do jednej kolumny.
    /// </remarks>
    [Fact]
    public void KolumnyRozdzielaSrednik()
    {
        string tekst = Tekst(UslugaZestawien.SprzedazCsv(Rejestr()));
        string naglowek = tekst.Split('\n')[0];

        Assert.Contains("Numer;Data wystawienia", naglowek, StringComparison.Ordinal);
    }

    /// <summary>Kwoty mają przecinek dziesiętny i żadnego separatora tysięcy.</summary>
    [Fact]
    public void KwotyMajaPrzecinekDziesietny()
    {
        string tekst = Tekst(UslugaZestawien.SprzedazCsv(Rejestr()));

        Assert.Contains("1000,00", tekst, StringComparison.Ordinal);
        Assert.Contains("230,00", tekst, StringComparison.Ordinal);

        // Spacja rozdzielająca tysiące jest czytelna dla człowieka, ale arkusz
        // uznałby taką wartość za tekst i nie policzyłby sumy.
        Assert.DoesNotContain("1 000,00", tekst, StringComparison.Ordinal);
    }

    [Fact]
    public void PolskieZnakiSaZachowane()
    {
        string tekst = Tekst(UslugaZestawien.ZakupyCsv(Rejestr()));

        Assert.Contains("Dostawca Prądu S.A.", tekst, StringComparison.Ordinal);
        Assert.Contains("Data wpływu", tekst, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nazwa firmy ze średnikiem nie może rozjechać wiersza.
    /// </summary>
    /// <remarks>
    /// „Kowalski; Nowak s.c." to zupełnie zwyczajna nazwa spółki cywilnej.
    /// Bez cudzysłowów jeden taki kontrahent przesuwa wszystkie kolumny.
    /// </remarks>
    [Fact]
    public void NazwaZeSrednikiemJestUjetaWCudzyslowy()
    {
        var okres = OkresRozliczeniowy.Miesiac(2026, 8);

        WpisSprzedazy wpis = new(
            "FV/1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1),
            "Kowalski; Nowak s.c.", "1180000001", null,
            [new KwotyWStawce(StawkaVat.Vat23, 100m, 23m)]);

        string tekst = Tekst(UslugaZestawien.SprzedazCsv(
            RejestrVat.Zbuduj(okres, [wpis], [])));

        Assert.Contains("\"Kowalski; Nowak s.c.\"", tekst, StringComparison.Ordinal);
    }

    /// <summary>Ostatni wiersz to suma - po niej sprawdza się kompletność pliku.</summary>
    [Fact]
    public void NaKoncuJestWierszSumy()
    {
        string tekst = Tekst(UslugaZestawien.SprzedazCsv(Rejestr()));

        string[] wiersze = tekst.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("RAZEM;", wiersze[^1], StringComparison.Ordinal);
        Assert.Contains("1000,00", wiersze[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void ZestawienieZakupowRozroznaOdliczenie()
    {
        var okres = OkresRozliczeniowy.Miesiac(2026, 8);

        WpisZakupu bezOdliczenia = new(
            "ZAK/2", new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 4),
            new DateOnly(2026, 8, 4), "Sprzedawca", null,
            RodzajZakupu.TowaryIUslugi, Odliczany: false, 200m, 46m);

        string tekst = Tekst(UslugaZestawien.ZakupyCsv(
            RejestrVat.Zbuduj(okres, [], [bezOdliczenia])));

        // Podatek jest w kolumnie VAT, ale nie w kolumnie do odliczenia.
        Assert.Contains(";nie;200,00;46,00;0,00;246,00", tekst, StringComparison.Ordinal);
    }

    /// <summary>Paczka zawiera komplet - to ona idzie do księgowej.</summary>
    [Fact]
    public void PaczkaZawieraTrzyPliki()
    {
        byte[] paczka = UslugaZestawien.Paczka(Rejestr(), "Moja Firma sp. z o.o.", "5252248481");

        using var strumien = new MemoryStream(paczka);
        using var archiwum = new ZipArchive(strumien, ZipArchiveMode.Read);

        Assert.Equal(3, archiwum.Entries.Count);
        Assert.NotNull(archiwum.GetEntry(UslugaZestawien.PlikSprzedazy));
        Assert.NotNull(archiwum.GetEntry(UslugaZestawien.PlikZakupow));
        Assert.NotNull(archiwum.GetEntry(UslugaZestawien.PlikPodsumowania));
    }

    [Fact]
    public void PodsumowanieMowiIleWyszloDoZaplaty()
    {
        byte[] plik = UslugaZestawien.Podsumowanie(
            Rejestr(), "Moja Firma sp. z o.o.", "5252248481");

        string tekst = Tekst(plik);

        Assert.Contains("Moja Firma sp. z o.o.", tekst, StringComparison.Ordinal);
        Assert.Contains("sierpień 2026", tekst, StringComparison.Ordinal);

        // 230 podatku należnego minus 115 naliczonego.
        Assert.Contains("do zapłaty:            115,00", tekst, StringComparison.Ordinal);
    }

    /// <summary>Nazwa pliku niesie okres - inaczej łatwo pomylić miesiące.</summary>
    [Fact]
    public void NazwaPlikuZawieraOkres()
    {
        string nazwa = UslugaZestawien.NazwaPliku(
            "zestawienie", OkresRozliczeniowy.Miesiac(2026, 8), "zip");

        Assert.Equal("zestawienie-2026-08.zip", nazwa);
    }

    /// <summary>Pusty okres też daje plik - z nagłówkami i zerami.</summary>
    [Fact]
    public void PustyOkresDajePlikZNaglowkami()
    {
        RejestrVat pusty = RejestrVat.Zbuduj(OkresRozliczeniowy.Miesiac(2026, 8), [], []);

        string tekst = Tekst(UslugaZestawien.SprzedazCsv(pusty));

        Assert.Contains("Numer;", tekst, StringComparison.Ordinal);
        Assert.Contains("RAZEM;;;;;0,00;0,00;0,00;", tekst, StringComparison.Ordinal);
    }
}
