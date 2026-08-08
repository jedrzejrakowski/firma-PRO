using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FirmaPro.Domena;
using FirmaPro.Ksef;

namespace FirmaPro.Testy;

/// <summary>
/// Narzędzia wspólne dla testów: dostęp do schematu XSD i przykładowa faktura.
/// </summary>
internal static class Fabryka
{
    private const string PrzestrzenTypowWspolnych =
        "http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/";

    private static readonly Lazy<XmlSchemaSet> Schematy = new(WczytajSchematy);

    /// <summary>Katalog ze schematami - szukany w górę od katalogu testów.</summary>
    private static string KatalogSchematow()
    {
        var katalog = new DirectoryInfo(AppContext.BaseDirectory);
        while (katalog is not null)
        {
            string kandydat = Path.Combine(katalog.FullName, "schematy");
            if (Directory.Exists(kandydat))
            {
                return kandydat;
            }

            katalog = katalog.Parent;
        }

        throw new DirectoryNotFoundException(
            "Nie znaleziono katalogu 'schematy' ze schematem FA(3).");
    }

    /// <summary>
    /// Wczytuje schemat FA(3) razem z lokalnym odpowiednikiem schematu typów
    /// wspólnych, dzięki czemu testy działają bez dostępu do internetu.
    /// </summary>
    private static XmlSchemaSet WczytajSchematy()
    {
        string katalog = KatalogSchematow();
        var zestaw = new XmlSchemaSet();
        zestaw.Add(PrzestrzenTypowWspolnych,
            Path.Combine(katalog, "StrukturyDanych_v10-0E_lokalny.xsd"));
        zestaw.Add(Fa3Generator.Ns.NamespaceName,
            Path.Combine(katalog, "schemat_FA(3)_v1-0E.xsd"));
        zestaw.Compile();
        return zestaw;
    }

    /// <summary>
    /// Sprawdza dokument oficjalnym schematem i zwraca listę uchybień.
    /// Pusta lista oznacza dokument zgodny ze wzorem.
    /// </summary>
    internal static IReadOnlyList<string> BledyWalidacjiXsd(byte[] xml)
    {
        var bledy = new List<string>();
        var ustawienia = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schematy.Value
        };
        ustawienia.ValidationEventHandler += (_, zdarzenie) => bledy.Add(zdarzenie.Message);

        using var strumien = new MemoryStream(xml);
        using XmlReader czytnik = XmlReader.Create(strumien, ustawienia);
        while (czytnik.Read())
        {
        }

        return bledy;
    }

    /// <summary>Wyciąga treść pierwszego elementu o podanej nazwie.</summary>
    internal static string? Wartosc(byte[] xml, string nazwaElementu)
    {
        XDocument dokument = XDocument.Parse(System.Text.Encoding.UTF8.GetString(xml));
        return dokument.Descendants(Fa3Generator.Ns + nazwaElementu).FirstOrDefault()?.Value;
    }

    /// <summary>Poprawna faktura, którą testy modyfikują pod swoje potrzeby.</summary>
    internal static Faktura PrzykladowaFaktura() => new()
    {
        Numer = "FV/2026/08/1",
        DataWystawienia = new DateOnly(2026, 8, 8),
        DataSprzedazy = new DateOnly(2026, 8, 5),
        MiejsceWystawienia = "Warszawa",
        Waluta = "PLN",
        Rodzaj = RodzajFaktury.Vat,
        Sprzedawca = new Podmiot
        {
            Nazwa = "Moja Firma sp. z o.o.",
            Nip = "5252248481",
            Adres = new Adres
            {
                KodKraju = "PL",
                Linia1 = "ul. Prosta 51",
                Linia2 = "00-838 Warszawa"
            },
            Email = "biuro@example.pl",
            Telefon = "+48221234567"
        },
        Nabywca = new Podmiot
        {
            Nazwa = "Klient S.A.",
            Nip = "7010001453",
            Adres = new Adres
            {
                KodKraju = "PL",
                Linia1 = "ul. Długa 1",
                Linia2 = "80-827 Gdańsk"
            }
        },
        Pozycje =
        [
            new PozycjaFaktury
            {
                Nazwa = "Usługa programistyczna",
                Jednostka = "godz.",
                Ilosc = 10m,
                CenaNetto = 150.00m,
                Stawka = StawkaVat.Vat23
            },
            new PozycjaFaktury
            {
                Nazwa = "Szkolenie",
                Jednostka = "szt.",
                Ilosc = 2m,
                CenaNetto = 500.00m,
                Stawka = StawkaVat.Vat8
            }
        ],
        Platnosc = new WarunkiPlatnosci
        {
            Forma = FormaPlatnosci.Przelew,
            Termin = new DateOnly(2026, 8, 22),
            Rachunek = "88102010260000120200060290",
            NazwaBanku = "Bank Przykładowy S.A."
        },
        Stopka = "Dziękujemy za współpracę."
    };

    /// <summary>Znacznik czasu ustalony, żeby wynik testów był powtarzalny.</summary>
    internal static readonly DateTimeOffset DataWytworzenia =
        new(2026, 8, 8, 10, 30, 0, TimeSpan.Zero);
}
