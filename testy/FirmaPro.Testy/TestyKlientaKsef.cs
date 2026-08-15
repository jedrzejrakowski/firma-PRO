using System.Security.Cryptography;
using System.Text;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Xunit;

namespace FirmaPro.Testy;

/// <summary>Operacje kryptograficzne wymagane przez KSeF.</summary>
public class TestyKryptografii
{
    [Fact]
    public void SzyfrowanieIOdszyfrowanieDajeTeSameDane()
    {
        Kryptografia.KluczSesji klucz = Kryptografia.GenerujKluczSesji();
        byte[] dane = Encoding.UTF8.GetBytes("Faktura z polskimi znakami: żółć ąę");

        Assert.Equal(dane, Kryptografia.OdszyfrujAes(
            Kryptografia.SzyfrujAes(dane, klucz), klucz));
    }

    [Fact]
    public void KluczSesjiMaDlugoscWymaganaPrzezKsef()
    {
        Kryptografia.KluczSesji klucz = Kryptografia.GenerujKluczSesji();

        Assert.Equal(32, klucz.Klucz.Length);            // AES-256
        Assert.Equal(16, klucz.WektorInicjujacy.Length); // 128 bitów
    }

    [Fact]
    public void DopelnienieDzialaDlaDanychODlugosciBloku()
    {
        // PKCS#7 dokłada cały blok, gdy dane są wielokrotnością 16 bajtów.
        Kryptografia.KluczSesji klucz = Kryptografia.GenerujKluczSesji();
        byte[] dane = new byte[16];

        byte[] szyfrogram = Kryptografia.SzyfrujAes(dane, klucz);

        Assert.Equal(32, szyfrogram.Length);
        Assert.Equal(dane, Kryptografia.OdszyfrujAes(szyfrogram, klucz));
    }

    [Fact]
    public void BlednyKluczZglaszaBlad()
    {
        Kryptografia.KluczSesji klucz = Kryptografia.GenerujKluczSesji();
        Kryptografia.KluczSesji inny = Kryptografia.GenerujKluczSesji();
        byte[] szyfrogram = Kryptografia.SzyfrujAes(
            Encoding.UTF8.GetBytes("dane testowe"), klucz);

        Assert.Throws<CryptographicException>(
            () => Kryptografia.OdszyfrujAes(szyfrogram, inny));
    }

    [Fact]
    public void SkrotDlaKoduQrNieZawieraZnakowWymagajacychKodowania()
    {
        string skrot = Kryptografia.SkrotBase64Url(Encoding.UTF8.GetBytes("abc"));

        Assert.DoesNotContain("+", skrot, StringComparison.Ordinal);
        Assert.DoesNotContain("/", skrot, StringComparison.Ordinal);
        Assert.DoesNotContain("=", skrot, StringComparison.Ordinal);
    }
}

/// <summary>
/// Pełny przebieg rozmowy z KSeF sprawdzany na atrapie, która naprawdę
/// odszyfrowuje przesyłane dane.
/// </summary>
public class TestyKlientaKsef
{
    private static byte[] PrzykladowyXml() =>
        Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura(), Fabryka.DataWytworzenia);

    [Fact]
    public async Task UwierzytelnianieSzyfrujeTokenZeZnacznikiemCzasu()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");

        // Serwer odszyfrował dokładnie to, czego wymaga dokumentacja.
        Assert.Equal($"TOKEN-KSEF-123|{AtrapaKsef.ZnacznikMs}", atrapa.OdszyfrowanyToken);
    }

    [Fact]
    public async Task BlednyTokenKonczySieCzytelnymBledem()
    {
        using var atrapa = new AtrapaKsef(oczekiwanyToken: "PRAWIDLOWY");
        KlientKsef klient = atrapa.UtworzKlienta();

        BladKsefException blad = await Assert.ThrowsAsync<BladKsefException>(
            () => klient.UwierzytelnijAsync("5252248481", "ZLY-TOKEN"));

        Assert.Equal(401, blad.KodHttp);
        Assert.Contains("token", blad.PelnyOpis(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrakTokenaZglaszaBladPrzedWyjsciemDoSieci()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await Assert.ThrowsAsync<BladKsefException>(
            () => klient.UwierzytelnijAsync("5252248481", ""));

        // Nic nie poleciało do systemu.
        Assert.Empty(atrapa.Wywolania);
    }

    [Fact]
    public async Task PelnaWysylkaFaktury()
    {
        byte[] xml = PrzykladowyXml();
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");
        await klient.OtworzSesjeAsync();
        string numerReferencyjny = await klient.WyslijFaktureAsync(xml);
        WynikWeryfikacji wynik = await klient.PoczekajNaWynikAsync(numerReferencyjny);

        Assert.True(wynik.Przyjeta);
        Assert.Equal(AtrapaKsef.NumerKsef, wynik.NumerKsef);

        // Serwer odtworzył fakturę bajt w bajt - klucz, wektor i skróty
        // muszą się zgadzać, żeby to było możliwe.
        Assert.Equal(xml, atrapa.OdebraneFaktury.Single());
    }

    [Fact]
    public async Task KluczSesjiTrafiaDoSerweraWeWlasciwejPostaci()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");
        await klient.OtworzSesjeAsync();

        Assert.Equal(32, atrapa.KluczSesji!.Length);
        Assert.Equal(16, atrapa.WektorSesji!.Length);
    }

    [Fact]
    public async Task WysylkaBezOtwartejSesjiJestOdrzucana()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();
        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");

        await Assert.ThrowsAsync<BladKsefException>(
            () => klient.WyslijFaktureAsync(PrzykladowyXml()));
    }

    [Fact]
    public async Task OperacjeBezUwierzytelnieniaSaOdrzucane()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await Assert.ThrowsAsync<BladKsefException>(() => klient.OtworzSesjeAsync());
    }

    [Fact]
    public async Task ZamkniecieSesjiUruchamiaGenerowanieUpo()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");
        await klient.OtworzSesjeAsync();
        await klient.ZamknijSesjeAsync();

        Assert.True(atrapa.SesjaZamknieta);
    }

    /// <summary>
    /// Poświadczenie pobiera się po zamknięciu sesji - wtedy dopiero powstaje.
    /// </summary>
    /// <remarks>
    /// Numer sesji jest tu zwykłym parametrem, a nie stanem klienta. Gdyby
    /// pobranie wymagało otwartej sesji, UPO byłoby nie do odzyskania nazajutrz.
    /// </remarks>
    [Fact]
    public async Task PobranieUpoPoZamknieciuSesji()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");
        string numerSesji = await klient.OtworzSesjeAsync();
        await klient.ZamknijSesjeAsync();

        Assert.Contains("UPO",
            await klient.PobierzUpoAsync(numerSesji, AtrapaKsef.NumerKsef),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PobranieFakturZakupowych()
    {
        byte[] xml = PrzykladowyXml();
        using var atrapa = new AtrapaKsef(xmlFakturyZakupowej: xml);
        KlientKsef klient = atrapa.UtworzKlienta();

        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");
        IReadOnlyList<FakturaZakupowa> lista = await klient.PobierzFakturyZakupoweAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        FakturaZakupowa faktura = Assert.Single(lista);
        Assert.Equal("FS/7/2026", faktura.Numer);
        Assert.Equal("7010001453", faktura.SprzedawcaNip);
        Assert.Equal(1230.00m, faktura.Brutto);

        byte[] pobrany = await klient.PobierzXmlFakturyAsync(faktura.NumerKsef);
        Assert.Contains("<Faktura", Encoding.UTF8.GetString(pobrany), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KolejnoscWywolanOdpowiadaDokumentacji()
    {
        using var atrapa = new AtrapaKsef();
        KlientKsef klient = atrapa.UtworzKlienta();

        await klient.UwierzytelnijAsync("5252248481", "TOKEN-KSEF-123");

        // Wyzwanie musi poprzedzać wysłanie zaszyfrowanego tokena, bo to
        // z niego pochodzi znacznik czasu użyty przy szyfrowaniu.
        Assert.Equal("POST auth/challenge", atrapa.Wywolania[0]);
        Assert.Contains("POST auth/ksef-token", atrapa.Wywolania, StringComparer.Ordinal);
        Assert.Contains("POST auth/token/redeem", atrapa.Wywolania, StringComparer.Ordinal);
    }
}

/// <summary>Adresy środowisk i link weryfikacyjny kodu QR.</summary>
public class TestyAdresowIKoduQr
{
    [Theory]
    [InlineData(SrodowiskoKsef.Test, "https://api-test.ksef.mf.gov.pl/v2")]
    [InlineData(SrodowiskoKsef.Demo, "https://api-demo.ksef.mf.gov.pl/v2")]
    [InlineData(SrodowiskoKsef.Produkcja, "https://api.ksef.mf.gov.pl/v2")]
    public void AdresyApi(SrodowiskoKsef srodowisko, string oczekiwany)
    {
        Assert.Equal(oczekiwany, AdresyKsef.Api(srodowisko));
    }

    [Fact]
    public void AdresKoduQrJestInnyNizAdresApi()
    {
        // Kody QR wskazują osobną usługę weryfikacyjną, nie API.
        Assert.NotEqual(AdresyKsef.Api(SrodowiskoKsef.Produkcja),
            AdresyKsef.KodQr(SrodowiskoKsef.Produkcja));
        Assert.StartsWith("https://qr", AdresyKsef.KodQr(SrodowiskoKsef.Produkcja),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LinkWeryfikacyjnyMaPostacWymaganaPrzezKsef()
    {
        byte[] xml = Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura(),
            Fabryka.DataWytworzenia);

        string link = KodyQr.LinkWeryfikacyjny("5252248481",
            new DateOnly(2026, 8, 8), xml, SrodowiskoKsef.Test);

        // Adres składa się z NIP-u sprzedawcy, daty w formacie dzień-miesiąc-rok
        // oraz skrótu SHA-256 pliku faktury.
        Assert.StartsWith("https://qr-test.ksef.mf.gov.pl/invoice/5252248481/08-08-2026/",
            link, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkZmieniaSieWrazZTrescaFaktury()
    {
        var data = new DateOnly(2026, 8, 8);
        byte[] pierwsza = Fa3Generator.ZbudujXml(Fabryka.PrzykladowaFaktura(),
            Fabryka.DataWytworzenia);

        Faktura zmieniona = Fabryka.PrzykladowaFaktura();
        zmieniona.Numer = "FV/2026/08/2";
        byte[] druga = Fa3Generator.ZbudujXml(zmieniona, Fabryka.DataWytworzenia);

        Assert.NotEqual(
            KodyQr.LinkWeryfikacyjny("5252248481", data, pierwsza, SrodowiskoKsef.Test),
            KodyQr.LinkWeryfikacyjny("5252248481", data, druga, SrodowiskoKsef.Test));
    }
}
