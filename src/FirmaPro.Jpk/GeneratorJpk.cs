using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FirmaPro.Domena;

namespace FirmaPro.Jpk;

/// <summary>
/// Buduje plik JPK_V7M - ewidencję VAT wraz z deklaracją.
/// </summary>
/// <remarks>
/// <para>
/// <b>Struktura pliku nie została sprawdzona oficjalnym schematem XSD.</b>
/// W środowisku, w którym powstawał ten kod, serwisy Ministerstwa Finansów
/// są niedostępne, więc nazwy i kolejność elementów pochodzą z dokumentacji
/// struktury, a nie z samego schematu. Przy fakturach FA(3) walidacja
/// prawdziwym schematem wychwyciła błędy nie do przewidzenia - tutaj takiego
/// zabezpieczenia zabrakło.
/// </para>
/// <para>
/// Zanim plik trafi do urzędu, trzeba go sprawdzić: wgrać schemat do katalogu
/// <c>schematy/</c> (wtedy zrobi to test) albo skorzystać z bezpłatnej
/// aplikacji Klient JPK_WEB. Kwoty są natomiast obłożone testami i to one
/// decydują o wysokości podatku.
/// </para>
/// </remarks>
public static class GeneratorJpk
{
    /// <summary>Przestrzeń nazw struktury JPK_V7M w wersji obowiązującej od lutego 2026 r.</summary>
    public const string PrzestrzenNazw = "http://crd.gov.pl/wzor/2025/12/19/13775/";

    /// <summary>Przestrzeń nazw typów wspólnych używanych w nagłówku.</summary>
    public const string PrzestrzenTypow =
        "http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/";

    private static readonly XNamespace Tns = PrzestrzenNazw;
    private static readonly XNamespace Etd = PrzestrzenTypow;

    /// <summary>Składa dokument XML pliku JPK_V7M.</summary>
    public static XDocument ZbudujDokument(DeklaracjaVat deklaracja,
                                           RejestrVat rejestr,
                                           DanePliku dane)
    {
        ArgumentNullException.ThrowIfNull(deklaracja);
        ArgumentNullException.ThrowIfNull(rejestr);
        ArgumentNullException.ThrowIfNull(dane);

        if (deklaracja.Okres != rejestr.Okres)
        {
            throw new ArgumentException(
                "Deklaracja i rejestr dotyczą różnych okresów: " +
                $"{deklaracja.Okres.Nazwa} i {rejestr.Okres.Nazwa}.", nameof(rejestr));
        }

        var jpk = new XElement(Tns + "JPK",
            new XAttribute(XNamespace.Xmlns + "tns", PrzestrzenNazw),
            new XAttribute(XNamespace.Xmlns + "etd", PrzestrzenTypow),
            Naglowek(deklaracja.Okres, dane),
            Podmiot(dane),
            Deklaracja(deklaracja, dane),
            Ewidencja(rejestr));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), jpk);
    }

    /// <summary>Składa plik i zapisuje go w postaci bajtów.</summary>
    public static byte[] ZbudujPlik(DeklaracjaVat deklaracja,
                                    RejestrVat rejestr,
                                    DanePliku dane)
    {
        XDocument dokument = ZbudujDokument(deklaracja, rejestr, dane);

        var ustawienia = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  "
        };

        using var pamiec = new MemoryStream();
        using (XmlWriter pisarz = XmlWriter.Create(pamiec, ustawienia))
        {
            dokument.Save(pisarz);
        }

        return pamiec.ToArray();
    }

    // ----------------------------------------------------------- nagłówek

    private static XElement Naglowek(OkresRozliczeniowy okres, DanePliku dane)
    {
        var kodFormularza = new XElement(Tns + "KodFormularza", "JPK_V7M",
            new XAttribute("kodSystemowy", "JPK_V7M (3)"),
            new XAttribute("wersjaSchemy", "1-0"));

        return new XElement(Tns + "Naglowek",
            kodFormularza,
            new XElement(Tns + "WariantFormularza", 3),
            // Cel złożenia: 1 - deklaracja pierwotna, 2 - korekta.
            new XElement(Tns + "DataWytworzeniaJPK",
                dane.DataWytworzenia.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            new XElement(Tns + "NazwaSystemu", dane.NazwaSystemu),
            new XElement(Tns + "CelZlozenia", (int)dane.Cel),
            new XElement(Tns + "KodUrzedu", dane.KodUrzedu),
            new XElement(Tns + "Rok", okres.Rok),
            new XElement(Tns + "Miesiac", MiesiacNaglowka(okres)));
    }

    /// <summary>
    /// Numer miesiąca wpisywany do nagłówka.
    /// </summary>
    /// <remarks>
    /// Przy rozliczeniu kwartalnym ewidencję składa się co miesiąc, a plik
    /// niesie ostatni miesiąc kwartału. Program obsługuje na razie rozliczenie
    /// miesięczne - dla kwartału podaje ostatni miesiąc okresu, co odpowiada
    /// deklaracji składanej za cały kwartał.
    /// </remarks>
    private static int MiesiacNaglowka(OkresRozliczeniowy okres) =>
        okres.Typ == TypOkresu.Miesieczny ? okres.Numer : okres.Numer * 3;

    private static XElement Podmiot(DanePliku dane)
    {
        var identyfikator = new XElement(Tns + "OsobaNiefizyczna",
            new XElement(Etd + "NIP", dane.Nip),
            new XElement(Etd + "PelnaNazwa", dane.Nazwa));

        if (!string.IsNullOrWhiteSpace(dane.Email))
        {
            identyfikator.Add(new XElement(Etd + "Email", dane.Email));
        }

        if (!string.IsNullOrWhiteSpace(dane.Telefon))
        {
            identyfikator.Add(new XElement(Etd + "Telefon", dane.Telefon));
        }

        return new XElement(Tns + "Podmiot1",
            new XAttribute("rola", "Podatnik"),
            identyfikator);
    }

    // --------------------------------------------------------- deklaracja

    private static XElement Deklaracja(DeklaracjaVat deklaracja, DanePliku dane)
    {
        var naglowek = new XElement(Tns + "Naglowek",
            new XElement(Tns + "KodFormularzaDekl", "VAT-7",
                new XAttribute("kodSystemowy", "VAT-7 (23)"),
                new XAttribute("kodPodatku", "VAT"),
                new XAttribute("rodzajZobowiazania", "Z"),
                new XAttribute("wersjaSchemy", "1-0")),
            new XElement(Tns + "WariantFormularzaDekl", 23));

        var pozycje = new XElement(Tns + "PozycjeSzczegolowe");
        foreach ((int numer, long wartosc) in deklaracja.WypelnionePola)
        {
            pozycje.Add(new XElement(
                Tns + "P_" + numer.ToString(CultureInfo.InvariantCulture),
                wartosc.ToString(CultureInfo.InvariantCulture)));
        }

        return new XElement(Tns + "Deklaracja",
            naglowek,
            pozycje,
            // Oświadczenie o zapoznaniu się z pouczeniami składa się zawsze.
            new XElement(Tns + "Pouczenia", 1));
    }

    // --------------------------------------------------------- ewidencja

    private static XElement Ewidencja(RejestrVat rejestr)
    {
        var ewidencja = new XElement(Tns + "Ewidencja");

        int lp = 1;
        foreach (WpisSprzedazy wpis in rejestr.Sprzedaz.Wpisy)
        {
            ewidencja.Add(WierszSprzedazy(wpis, lp++));
        }

        ewidencja.Add(new XElement(Tns + "SprzedazCtrl",
            new XElement(Tns + "LiczbaWierszySprzedazy",
                rejestr.Sprzedaz.Wpisy.Count.ToString(CultureInfo.InvariantCulture)),
            new XElement(Tns + "PodatekNalezny",
                Kwoty.NaXml(rejestr.Sprzedaz.PodatekNalezny))));

        lp = 1;
        foreach (WpisZakupu wpis in rejestr.Zakupy.Wpisy.Where(w => w.Odliczany))
        {
            ewidencja.Add(WierszZakupu(wpis, lp++));
        }

        // Do sum kontrolnych wchodzą wyłącznie wiersze, które trafiły do
        // pliku - zakup bez prawa do odliczenia zostaje w rejestrze firmy,
        // ale w ewidencji przekazywanej urzędowi go nie ma.
        int liczbaZakupow = rejestr.Zakupy.Wpisy.Count(w => w.Odliczany);

        ewidencja.Add(new XElement(Tns + "ZakupCtrl",
            new XElement(Tns + "LiczbaWierszyZakupow",
                liczbaZakupow.ToString(CultureInfo.InvariantCulture)),
            new XElement(Tns + "PodatekNaliczony",
                Kwoty.NaXml(rejestr.Zakupy.PodatekNaliczony))));

        return ewidencja;
    }

    private static XElement WierszSprzedazy(WpisSprzedazy wpis, int lp)
    {
        var wiersz = new XElement(Tns + "SprzedazWiersz",
            new XElement(Tns + "LpSprzedazy", lp.ToString(CultureInfo.InvariantCulture)),
            new XElement(Tns + "NrKontrahenta", wpis.NabywcaNip ?? "BRAK"),
            new XElement(Tns + "NazwaKontrahenta", wpis.NabywcaNazwa),
            new XElement(Tns + "DowodSprzedazy", wpis.Numer),
            new XElement(Tns + "DataWystawienia", Data(wpis.DataWystawienia)),
            new XElement(Tns + "DataSprzedazy", Data(wpis.DataUjecia)));

        // Numer KSeF to nowość struktury obowiązującej od lutego 2026 r.
        if (!string.IsNullOrWhiteSpace(wpis.NumerKsef))
        {
            wiersz.Add(new XElement(Tns + "NrKSeF", wpis.NumerKsef));
        }

        foreach (KwotyWStawce kwoty in wpis.WedlugStawek)
        {
            PolaSprzedazy? pola = PolaJpk.DlaStawki(kwoty.Stawka);
            if (pola is null)
            {
                continue;
            }

            DodajPole(wiersz, pola.PoleNetto, kwoty.Netto);

            if (pola.PoleVat is int poleVat)
            {
                DodajPole(wiersz, poleVat, kwoty.Vat);
            }

            if (pola.PoleDodatkowe is int poleDodatkowe)
            {
                DodajPole(wiersz, poleDodatkowe, kwoty.Netto);
            }
        }

        return wiersz;
    }

    private static XElement WierszZakupu(WpisZakupu wpis, int lp)
    {
        var wiersz = new XElement(Tns + "ZakupWiersz",
            new XElement(Tns + "LpZakupu", lp.ToString(CultureInfo.InvariantCulture)),
            new XElement(Tns + "NrDostawcy", wpis.SprzedawcaNip ?? "BRAK"),
            new XElement(Tns + "NazwaDostawcy", wpis.SprzedawcaNazwa),
            new XElement(Tns + "DowodZakupu", wpis.Numer),
            new XElement(Tns + "DataZakupu", Data(wpis.DataWystawienia)),
            new XElement(Tns + "DataWplywu", Data(wpis.DataWplywu)));

        if (wpis.Rodzaj == RodzajZakupu.SrodkiTrwale)
        {
            DodajPole(wiersz, PolaJpk.NettoSrodkiTrwale, wpis.Netto);
            DodajPole(wiersz, PolaJpk.VatSrodkiTrwale, wpis.VatDoOdliczenia);
        }
        else
        {
            DodajPole(wiersz, PolaJpk.NettoPozostale, wpis.Netto);
            DodajPole(wiersz, PolaJpk.VatPozostale, wpis.VatDoOdliczenia);
        }

        return wiersz;
    }

    /// <summary>
    /// Dopisuje pole kwotowe ewidencji, pomijając kwoty zerowe.
    /// </summary>
    /// <remarks>
    /// Ewidencja - w odróżnieniu od deklaracji - podaje kwoty w groszach.
    /// </remarks>
    private static void DodajPole(XElement wiersz, int numer, decimal kwota)
    {
        if (kwota == 0)
        {
            return;
        }

        wiersz.Add(new XElement(
            Tns + "K_" + numer.ToString(CultureInfo.InvariantCulture),
            Kwoty.NaXml(kwota)));
    }

    private static string Data(DateOnly data) =>
        data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
