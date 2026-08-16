using FirmaPro.Domena;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FirmaPro.Wydruk;

/// <summary>
/// Wizualizacja faktury w układzie Krajowego Systemu e-Faktur.
/// </summary>
/// <remarks>
/// <para>
/// To ten sam widok, który pokazuje aplikacja Ministerstwa i który każdy
/// program księgowy oddaje pod nazwą „wizualizacja KSeF": nagłówek systemu,
/// strony transakcji, szczegóły, pozycje, podsumowanie stawek, adnotacje
/// i rozliczenie. Układ jest rozpoznawalny i wszędzie taki sam - księgowa
/// wie, gdzie czego szukać, bez czytania od nowa cudzego wzoru.
/// </para>
/// <para>
/// Wydruk firmowy (<see cref="WydrukFaktury"/>) i ten są dwiema różnymi
/// rzeczami i oba mają rację bytu. Firmowy idzie do kontrahenta - ma logo,
/// kod QR i kwotę do zapłaty rzucającą się w oczy. Ten jest odwzorowaniem
/// dokumentu: pokazuje pola struktury FA(3) w kolejności i pod nazwami,
/// jakimi posługuje się system, także wtedy, gdy brzmią urzędowo.
/// </para>
/// <para>
/// Powstaje z faktury odczytanej z pliku XML, więc nadaje się tak samo do
/// faktur wystawionych przez nas, jak i do zakupowych pobranych z KSeF -
/// tam plik od dostawcy jest jedynym źródłem, jakie mamy.
/// </para>
/// </remarks>
public static class WydrukKsef
{
    /// <summary>Buduje plik PDF z wizualizacją w układzie KSeF.</summary>
    public static byte[] Utworz(Faktura faktura, OpcjeWydruku? opcje = null)
    {
        ArgumentNullException.ThrowIfNull(faktura);

        using var rysownik = new Rysownik(faktura, opcje ?? OpcjeWydruku.DlaProjektu());

        return rysownik.Buduj();
    }

    /// <summary>Nazwy rodzajów faktur - takie, jakimi posługuje się system.</summary>
    private static string OpisRodzaju(RodzajFaktury rodzaj) => rodzaj switch
    {
        RodzajFaktury.Vat => "Faktura podstawowa",
        RodzajFaktury.Korygujaca => "Faktura korygująca",
        RodzajFaktury.Zaliczkowa => "Faktura zaliczkowa",
        RodzajFaktury.Rozliczeniowa => "Faktura rozliczeniowa",
        RodzajFaktury.Uproszczona => "Faktura uproszczona",
        RodzajFaktury.KorektaZaliczkowej => "Korekta faktury zaliczkowej",
        RodzajFaktury.KorektaRozliczeniowej => "Korekta faktury rozliczeniowej",
        _ => "Faktura"
    };

    private sealed class Rysownik(Faktura faktura, OpcjeWydruku opcje) : IDisposable
    {
        private readonly PdfDocument _dokument = new();
        private readonly List<XGraphics> _strony = [];

        private XGraphics _rysik = null!;
        private double _y;

        public byte[] Buduj()
        {
            _dokument.Info.Title = "Wizualizacja KSeF - faktura " + faktura.Numer;
            _dokument.Info.Creator = "Firma PRO";
            _dokument.Info.Subject = "Wizualizacja faktury ustrukturyzowanej FA(3)";

            NowaStrona();

            Naglowek();
            StronyTransakcji();
            Szczegoly();
            Pozycje();
            PodsumowanieStawek();
            Adnotacje();
            Rozliczenie();

            StopkiZeStronami();

            using var pamiec = new MemoryStream();
            _dokument.Save(pamiec);

            return pamiec.ToArray();
        }

        public void Dispose()
        {
            foreach (XGraphics rysik in _strony)
            {
                rysik.Dispose();
            }

            _dokument.Dispose();
        }

        // ------------------------------------------------------------ strony

        private void NowaStrona()
        {
            PdfPage strona = _dokument.AddPage();
            strona.Width = XUnit.FromPoint(Styl.SzerokoscStrony);
            strona.Height = XUnit.FromPoint(Styl.WysokoscStrony);

            _rysik = XGraphics.FromPdfPage(strona);
            _strony.Add(_rysik);
            _y = Styl.MarginesGorny;
        }

        private void ZapewnijMiejsce(double potrzeba)
        {
            if (_y + potrzeba > Styl.DolnaGranica)
            {
                NowaStrona();
            }
        }

        private void StopkiZeStronami()
        {
            for (int numer = 0; numer < _strony.Count; numer++)
            {
                double y = Styl.WysokoscStrony - Styl.MarginesDolny + Styl.Mm(6);

                _strony[numer].DrawString(
                    "Wizualizacja faktury ustrukturyzowanej — nie jest fakturą "
                    + "w rozumieniu przepisów",
                    Styl.Mala, Styl.TekstSzary, Styl.Lewa, y);

                _strony[numer].DrawString(
                    $"Strona {numer + 1} z {_strony.Count}",
                    Styl.Mala, Styl.TekstSzary,
                    new XRect(Styl.Lewa, y - Styl.Mm(3), Styl.SzerokoscTresci, Styl.Mm(4)),
                    XStringFormats.TopRight);
            }
        }

        // ----------------------------------------------------------- nagłówek

        private void Naglowek()
        {
            _rysik.DrawString("Krajowy System e-Faktur", Styl.Tytul, Styl.Tekst,
                Styl.Lewa, _y + Styl.Mm(6));

            double xPrawy = Styl.Lewa;
            double szerokosc = Styl.SzerokoscTresci;

            _rysik.DrawString("Numer Faktury:", Styl.Mala, Styl.TekstSzary,
                new XRect(xPrawy, _y, szerokosc, Styl.Mm(4)), XStringFormats.TopRight);

            _rysik.DrawString(faktura.Numer, Styl.Tytul, Styl.Tekst,
                new XRect(xPrawy, _y + Styl.Mm(3), szerokosc, Styl.Mm(8)),
                XStringFormats.TopRight);

            _rysik.DrawString(OpisRodzaju(faktura.Rodzaj), Styl.Mala, Styl.TekstSzary,
                new XRect(xPrawy, _y + Styl.Mm(10), szerokosc, Styl.Mm(4)),
                XStringFormats.TopRight);

            if (!string.IsNullOrWhiteSpace(opcje.NumerKsef))
            {
                _rysik.DrawString("Numer KSeF: " + opcje.NumerKsef,
                    Styl.Mala, Styl.Tekst,
                    new XRect(xPrawy, _y + Styl.Mm(14), szerokosc, Styl.Mm(4)),
                    XStringFormats.TopRight);
            }

            _y += Styl.Mm(20);

            // Dokument nieobecny w systemie musi to mówić wprost - inaczej
            // wizualizacja wyglądałaby na dowód, którym nie jest.
            if (opcje.Projekt)
            {
                _rysik.DrawString(
                    "Dokument nie został jeszcze przesłany do KSeF — podgląd roboczy.",
                    Styl.MalaWyrozniona, Styl.Czerwien, Styl.Lewa, _y);

                _y += Styl.Mm(5);
            }

            Kreska();
        }

        // ------------------------------------------------------------- strony

        private void StronyTransakcji()
        {
            NaglowekSekcji("Sprzedawca", "Nabywca");

            double srodek = Styl.Lewa + (Styl.SzerokoscTresci / 2) + Styl.Mm(3);
            double yStart = _y;

            double poLewej = Podmiot(faktura.Sprzedawca, Styl.Lewa);
            _y = yStart;
            double poPrawej = Podmiot(faktura.Nabywca, srodek);

            _y = Math.Max(poLewej, poPrawej) + Styl.Mm(3);

            Kreska();

            PodmiotyInne();
        }

        /// <summary>
        /// Podmioty trzecie - każdy w osobnej sekcji, jak w aplikacji KSeF.
        /// </summary>
        /// <remarks>
        /// Rola stoi tuż pod nazwą, bo bez niej nie wiadomo, po co ten podmiot
        /// jest na fakturze: czy odbiera towar, czy przejął wierzytelność,
        /// czy dokłada się do zapłaty jako drugi nabywca.
        /// </remarks>
        private void PodmiotyInne()
        {
            int numer = 1;

            foreach (PodmiotInny podmiot in faktura.PodmiotyInne)
            {
                NaglowekSekcji($"Podmiot inny {numer++}");

                double szerokosc = Styl.SzerokoscTresci;

                Podmiot(podmiot.Dane, Styl.Lewa);

                Wiersz("Rola: " + podmiot.NazwaRoli, Styl.Lewa, szerokosc);

                if (podmiot.Udzial is { } udzial)
                {
                    Wiersz("Udział: " + Styl.Ilosc(udzial) + "%", Styl.Lewa, szerokosc);
                }

                if (!string.IsNullOrWhiteSpace(podmiot.NrKlienta))
                {
                    Wiersz("Numer klienta: " + podmiot.NrKlienta, Styl.Lewa, szerokosc);
                }

                _y += Styl.Mm(3);

                Kreska();
            }
        }

        /// <summary>Rysuje jedną stronę transakcji i zwraca dolną krawędź.</summary>
        private double Podmiot(Podmiot podmiot, double x)
        {
            double szerokosc = (Styl.SzerokoscTresci / 2) - Styl.Mm(3);

            if (!string.IsNullOrWhiteSpace(podmiot.Nip))
            {
                Wiersz("NIP: " + podmiot.Nip, x, szerokosc);
            }
            else if (!string.IsNullOrWhiteSpace(podmiot.NrVatUe))
            {
                Wiersz($"Nr VAT UE: {podmiot.KodUe} {podmiot.NrVatUe}", x, szerokosc);
            }
            else
            {
                Wiersz("Brak identyfikatora podatkowego", x, szerokosc);
            }

            Wiersz("Nazwa: " + podmiot.Nazwa, x, szerokosc);

            if (string.IsNullOrWhiteSpace(podmiot.Adres.Linia1))
            {
                return _y;
            }

            _y += Styl.Mm(2);
            Wiersz("Adres", x, szerokosc, Styl.Wyrozniona);
            Wiersz(podmiot.Adres.Jednolinijkowy, x, szerokosc);
            Wiersz(NazwaKraju(podmiot.Adres.KodKraju), x, szerokosc);

            return _y;
        }

        /// <summary>
        /// Nazwa kraju wypisywana pod adresem.
        /// </summary>
        /// <remarks>
        /// Struktura niesie sam kod. Rozwijamy tylko Polskę - to ona pada
        /// w niemal każdym dokumencie, a zgadywanie pozostałych nazw z dwóch
        /// liter kończyłoby się prędzej czy później pomyłką.
        /// </remarks>
        private static string NazwaKraju(string kod) =>
            string.Equals(kod, "PL", StringComparison.OrdinalIgnoreCase) ? "Polska" : kod;

        // ---------------------------------------------------------- szczegóły

        private void Szczegoly()
        {
            NaglowekSekcji("Szczegóły");

            double polowa = Styl.SzerokoscTresci / 2;

            _rysik.DrawString(
                "Data wystawienia, z zastrzeżeniem art. 106na ust. 1 ustawy:",
                Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y);

            _rysik.DrawString(Styl.Data(faktura.DataWystawienia), Styl.Zwykla, Styl.Tekst,
                Styl.Lewa, _y + Styl.Mm(4));

            if (faktura.DataSprzedazy is { } sprzedaz)
            {
                double x = Styl.Lewa + polowa;

                _rysik.DrawString(
                    "Data dokonania lub zakończenia dostawy towarów"
                    + " lub wykonania usługi:",
                    Styl.Mala, Styl.TekstSzary, x, _y);

                _rysik.DrawString(Styl.Data(sprzedaz), Styl.Zwykla, Styl.Tekst,
                    x, _y + Styl.Mm(4));
            }

            _y += Styl.Mm(8);

            Kreska();
        }

        // ------------------------------------------------------------ pozycje

        /// <summary>Szerokości kolumn tabeli pozycji, w milimetrach.</summary>
        private static readonly double[] KolumnyPozycji = [8, 62, 22, 13, 13, 20, 42];

        private void Pozycje()
        {
            NaglowekSekcji("Pozycje");

            _rysik.DrawString(
                $"Faktura wystawiona w cenach netto w walucie {faktura.Waluta}",
                Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y);

            _y += Styl.Mm(5);

            string[] naglowki =
            [
                "Lp.", "Nazwa towaru lub usługi", "Cena jedn. netto", "Ilość",
                "Miara", "Stawka podatku", "Wartość sprzedaży netto"
            ];

            NaglowekTabeli(KolumnyPozycji, naglowki);

            int lp = 1;

            foreach (PozycjaFaktury pozycja in faktura.PozycjePrzedKorekta)
            {
                WierszPozycji(pozycja, lp++, "korekta — stan przed");
            }

            foreach (PozycjaFaktury pozycja in faktura.Pozycje)
            {
                WierszPozycji(pozycja, lp++, null);
            }

            _y += Styl.Mm(3);

            PodsumowanieFaktury podsumowanie = faktura.Podsumowanie();

            _rysik.DrawString(
                "Kwota należności ogółem: "
                + Styl.Kwota(podsumowanie.RazemBrutto) + " " + faktura.Waluta,
                Styl.Wyrozniona, Styl.Tekst,
                new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, Styl.Mm(5)),
                XStringFormats.TopRight);

            _y += Styl.Mm(7);

            Kreska();
        }

        private void WierszPozycji(PozycjaFaktury pozycja, int lp, string? adnotacja)
        {
            // Długie nazwy towarów łamiemy, zamiast pozwolić im wyjść poza
            // kolumnę - w tabeli urzędowej nazwa bywa całym zdaniem.
            List<string> nazwa = ZawinTekst(pozycja.Nazwa, Styl.Tabela,
                Styl.Mm(KolumnyPozycji[1]) - Styl.Mm(3));

            if (adnotacja is not null)
            {
                nazwa.Add(adnotacja);
            }

            double wysokosc = Math.Max(Styl.Mm(5), (nazwa.Count * Styl.Mm(3.6)) + Styl.Mm(1.5));

            ZapewnijMiejsce(wysokosc + Styl.Mm(8));

            string[] wartosci =
            [
                lp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Empty,
                Styl.Kwota(pozycja.CenaNetto),
                Styl.Ilosc(pozycja.Ilosc),
                pozycja.Jednostka,
                pozycja.Stawka.Opis,
                Styl.Kwota(pozycja.WartoscNetto)
            ];

            double x = Styl.Lewa;

            for (int i = 0; i < KolumnyPozycji.Length; i++)
            {
                if (i != 1)
                {
                    _rysik.DrawString(wartosci[i], Styl.Tabela, Styl.Tekst,
                        new XRect(x + Styl.Mm(1.5), _y + Styl.Mm(1.2),
                                  Styl.Mm(KolumnyPozycji[i]) - Styl.Mm(3), Styl.Mm(4)),
                        Wyrownanie(i));
                }

                x += Styl.Mm(KolumnyPozycji[i]);
            }

            double xNazwy = Styl.Lewa + Styl.Mm(KolumnyPozycji[0]) + Styl.Mm(1.5);
            double yNazwy = _y + Styl.Mm(1.2);

            foreach (string linia in nazwa)
            {
                _rysik.DrawString(linia, Styl.Tabela, Styl.Tekst, xNazwy, yNazwy + Styl.Mm(2.6));
                yNazwy += Styl.Mm(3.6);
            }

            _y += wysokosc;

            _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Styl.Prawa, _y);
        }

        private static XStringFormat Wyrownanie(int kolumna) => kolumna switch
        {
            0 or 3 or 4 or 5 => XStringFormats.TopCenter,
            1 => XStringFormats.TopLeft,
            _ => XStringFormats.TopRight
        };

        // ------------------------------------------------- podsumowanie stawek

        private void PodsumowanieStawek()
        {
            NaglowekSekcji("Podsumowanie stawek podatku");

            double[] kolumny = [8, 45, 42, 42, 43];

            NaglowekTabeli(kolumny,
                ["Lp.", "Stawka podatku", "Kwota netto", "Kwota podatku", "Kwota brutto"]);

            int lp = 1;

            foreach (PozycjaPodsumowania wiersz in faktura.Podsumowanie().WedlugStawek)
            {
                ZapewnijMiejsce(Styl.Mm(10));

                string[] wartosci =
                [
                    lp++.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    wiersz.Stawka.Opis,
                    Styl.Kwota(wiersz.Netto),
                    Styl.Kwota(wiersz.Vat),
                    Styl.Kwota(wiersz.Brutto)
                ];

                double x = Styl.Lewa;

                for (int i = 0; i < kolumny.Length; i++)
                {
                    _rysik.DrawString(wartosci[i], Styl.Tabela, Styl.Tekst,
                        new XRect(x + Styl.Mm(1.5), _y + Styl.Mm(1.2),
                                  Styl.Mm(kolumny[i]) - Styl.Mm(3), Styl.Mm(4)),
                        i switch
                        {
                            0 => XStringFormats.TopCenter,
                            1 => XStringFormats.TopLeft,
                            _ => XStringFormats.TopRight
                        });

                    x += Styl.Mm(kolumny[i]);
                }

                _y += Styl.Mm(5);
                _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Styl.Prawa, _y);
            }

            PrzeliczenieNaZlote();

            _y += Styl.Mm(3);

            Kreska();
        }

        /// <summary>
        /// Kwoty w złotych przy fakturze walutowej.
        /// </summary>
        /// <remarks>
        /// Faktura w obcej walucie musi wykazywać podatek w złotych
        /// (art. 106e ust. 11 ustawy), a kurs jest w strukturze osobnym polem
        /// przy każdym wierszu - wizualizacja ma je pokazać, bo są treścią
        /// dokumentu, a nie ozdobą.
        /// </remarks>
        private void PrzeliczenieNaZlote()
        {
            if (!faktura.Walutowa || faktura.Kurs is not { } kurs)
            {
                return;
            }

            _y += Styl.Mm(2);

            decimal vat = faktura.Podsumowanie().WedlugStawek
                .Sum(s => Przeliczenie.NaZlote(s.Vat, kurs));

            _rysik.DrawString(
                $"Kurs waluty: 1 {kurs.Waluta} = {Styl.Kurs(kurs.Wartosc)} PLN"
                + (string.IsNullOrWhiteSpace(kurs.Tabela)
                    ? string.Empty
                    : $" (tabela NBP {kurs.Tabela} z {Styl.Data(kurs.ZDnia)})"),
                Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y + Styl.Mm(3));

            _rysik.DrawString(
                "Kwota podatku w złotych: " + Styl.Kwota(vat) + " PLN",
                Styl.MalaWyrozniona, Styl.Tekst,
                new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, Styl.Mm(5)),
                XStringFormats.TopRight);

            _y += Styl.Mm(5);
        }

        // ---------------------------------------------------------- adnotacje

        /// <summary>
        /// Adnotacje wymagane przez strukturę.
        /// </summary>
        /// <remarks>
        /// Wypisujemy wyłącznie okoliczności, które zachodzą. Lista wszystkich
        /// znaczników z dopiskiem „nie" zajęłaby pół strony i utopiła w sobie
        /// tę jedną, która akurat coś znaczy.
        /// </remarks>
        private void Adnotacje()
        {
            NaglowekSekcji("Adnotacje");

            var linie = new List<string>();

            if (faktura.MaOdwrotneObciazenie)
            {
                linie.Add("Odwrotne obciążenie");
            }

            if (faktura.MaSprzedazZwolniona)
            {
                linie.Add("Zwolnienie z podatku — podstawa: "
                          + (faktura.PodstawaZwolnienia ?? "nie wskazano"));
            }

            if (!string.IsNullOrWhiteSpace(faktura.PrzyczynaKorekty))
            {
                linie.Add("Przyczyna korekty: " + faktura.PrzyczynaKorekty);
            }

            foreach (DaneFakturyKorygowanej korygowana in faktura.Korygowane)
            {
                linie.Add($"Faktura korygowana: {korygowana.Numer}"
                          + $" z {Styl.Data(korygowana.DataWystawienia)}"
                          + (string.IsNullOrWhiteSpace(korygowana.NumerKsef)
                              ? " (poza KSeF)"
                              : $", numer KSeF {korygowana.NumerKsef}"));
            }

            if (linie.Count == 0)
            {
                linie.Add("Brak adnotacji szczególnych.");
            }

            foreach (string linia in linie)
            {
                ZapewnijMiejsce(Styl.Mm(8));

                foreach (string zawinieta in
                         ZawinTekst(linia, Styl.Zwykla, Styl.SzerokoscTresci))
                {
                    _rysik.DrawString(zawinieta, Styl.Zwykla, Styl.Tekst, Styl.Lewa, _y);
                    _y += Styl.Mm(4);
                }
            }

            _y += Styl.Mm(2);

            Kreska();
        }

        // --------------------------------------------------------- rozliczenie

        /// <summary>
        /// Rozliczenie - odliczenia i kwota do zapłaty.
        /// </summary>
        /// <remarks>
        /// Odliczeniem są zaliczki zafakturowane wcześniej: nabywca zapłacił
        /// już ich część, więc do zapłaty zostaje różnica (art. 106f ust. 3).
        /// Bez tej sekcji faktura końcowa wyglądałaby na żądanie zapłaty całej
        /// wartości dostawy po raz drugi.
        /// </remarks>
        private void Rozliczenie()
        {
            NaglowekSekcji("Rozliczenie");

            ZapewnijMiejsce(Styl.Mm(30));

            decimal odliczenia = faktura.ZafakturowaneZaliczkami;

            if (faktura.Zaliczkowe.Count > 0)
            {
                double[] kolumny = [90, 90];

                NaglowekTabeli(kolumny, ["Powód odliczenia", "Kwota odliczenia"]);

                foreach (DaneZaliczki zaliczka in faktura.Zaliczkowe)
                {
                    string powod = "Faktura zaliczkowa " + (
                        string.IsNullOrWhiteSpace(zaliczka.Numer)
                            ? zaliczka.NumerKsef ?? string.Empty
                            : zaliczka.Numer);

                    _rysik.DrawString(powod, Styl.Tabela, Styl.Tekst,
                        Styl.Lewa + Styl.Mm(1.5), _y + Styl.Mm(3.6));

                    _rysik.DrawString(Styl.Kwota(zaliczka.Brutto), Styl.Tabela, Styl.Tekst,
                        new XRect(Styl.Lewa + Styl.Mm(kolumny[0]), _y + Styl.Mm(1.2),
                                  Styl.Mm(kolumny[1]) - Styl.Mm(3), Styl.Mm(4)),
                        XStringFormats.TopRight);

                    _y += Styl.Mm(5);
                    _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Styl.Prawa, _y);
                }

                _y += Styl.Mm(2);

                _rysik.DrawString("Suma odliczeń: " + Styl.Kwota(odliczenia)
                                  + " " + faktura.Waluta,
                    Styl.Mala, Styl.TekstSzary,
                    new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, Styl.Mm(4)),
                    XStringFormats.TopRight);

                _y += Styl.Mm(6);
            }

            _rysik.DrawString(
                "Do zapłaty: " + Styl.Kwota(faktura.DoZaplaty) + " " + faktura.Waluta,
                Styl.DoZaplaty, Styl.Tekst,
                new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, Styl.Mm(8)),
                XStringFormats.TopRight);

            _y += Styl.Mm(10);

            WarunkiPlatnosci();
        }

        private void WarunkiPlatnosci()
        {
            var wiersze = new List<(string, string)>();

            if (faktura.Platnosc.Forma is { } forma)
            {
                wiersze.Add(("Forma płatności", OpisFormy(forma)));
            }

            if (faktura.Platnosc.Termin is { } termin)
            {
                wiersze.Add(("Termin płatności", Styl.Data(termin)));
            }

            if (faktura.Platnosc.Zaplacono)
            {
                wiersze.Add(("Zapłacono", faktura.Platnosc.DataZaplaty is { } dzien
                    ? Styl.Data(dzien)
                    : "tak"));
            }

            if (!string.IsNullOrWhiteSpace(faktura.Platnosc.Rachunek))
            {
                wiersze.Add(("Rachunek", Styl.Rachunek(faktura.Platnosc.Rachunek!)));
            }

            if (wiersze.Count == 0)
            {
                return;
            }

            ZapewnijMiejsce((wiersze.Count * Styl.Mm(4)) + Styl.Mm(6));

            foreach ((string etykieta, string wartosc) in wiersze)
            {
                _rysik.DrawString(etykieta, Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y);
                _rysik.DrawString(wartosc, Styl.Zwykla, Styl.Tekst,
                    Styl.Lewa + Styl.Mm(35), _y);

                _y += Styl.Mm(4.4);
            }
        }

        private static string OpisFormy(FormaPlatnosci forma) => forma switch
        {
            FormaPlatnosci.Gotowka => "gotówka",
            FormaPlatnosci.Karta => "karta",
            FormaPlatnosci.Bon => "bon",
            FormaPlatnosci.Czek => "czek",
            FormaPlatnosci.Kredyt => "kredyt",
            FormaPlatnosci.Przelew => "przelew",
            FormaPlatnosci.Mobilna => "płatność mobilna",
            _ => forma.ToString()
        };

        // -------------------------------------------------------- pomocnicze

        /// <summary>Rysuje jedną linijkę tekstu i przesuwa pisak niżej.</summary>
        private void Wiersz(string tekst, double x, double szerokosc, XFont? krój = null)
        {
            XFont uzyty = krój ?? Styl.Zwykla;

            foreach (string linia in ZawinTekst(tekst, uzyty, szerokosc))
            {
                _rysik.DrawString(linia, uzyty, Styl.Tekst, x, _y + Styl.Mm(3));
                _y += Styl.Mm(4);
            }
        }

        private void Kreska()
        {
            _rysik.DrawLine(Styl.Linia, Styl.Lewa, _y, Styl.Prawa, _y);
            _y += Styl.Mm(5);
        }

        private void NaglowekSekcji(string tytul, string? drugi = null)
        {
            ZapewnijMiejsce(Styl.Mm(16));

            _rysik.DrawString(tytul, Styl.NaglowekSekcji, Styl.Tekst, Styl.Lewa, _y);

            if (drugi is not null)
            {
                _rysik.DrawString(drugi, Styl.NaglowekSekcji, Styl.Tekst,
                    Styl.Lewa + (Styl.SzerokoscTresci / 2) + Styl.Mm(3), _y);
            }

            _y += Styl.Mm(6);
        }

        private void NaglowekTabeli(double[] kolumny, string[] naglowki)
        {
            ZapewnijMiejsce(Styl.Mm(14));

            double szerokosc = kolumny.Sum(k => Styl.Mm(k));
            var ramka = new XRect(Styl.Lewa, _y, szerokosc, Styl.Mm(6.5));

            _rysik.DrawRectangle(Styl.TloJasne, ramka);
            _rysik.DrawRectangle(Styl.LiniaJasna, ramka);

            double x = Styl.Lewa;

            for (int i = 0; i < kolumny.Length; i++)
            {
                // Nagłówki bywają dłuższe niż kolumna - łamiemy je, zamiast
                // pozwolić im wejść na sąsiednią.
                List<string> linie = ZawinTekst(naglowki[i], Styl.MalaWyrozniona,
                    Styl.Mm(kolumny[i]) - Styl.Mm(3));

                double yTekstu = _y + (linie.Count > 1 ? Styl.Mm(0.6) : Styl.Mm(1.8));

                foreach (string linia in linie)
                {
                    _rysik.DrawString(linia, Styl.MalaWyrozniona, Styl.TekstSzary,
                        new XRect(x + Styl.Mm(1.5), yTekstu,
                                  Styl.Mm(kolumny[i]) - Styl.Mm(3), Styl.Mm(3.4)),
                        i == 1 ? XStringFormats.TopLeft : XStringFormats.TopCenter);

                    yTekstu += Styl.Mm(2.8);
                }

                x += Styl.Mm(kolumny[i]);
            }

            _y += Styl.Mm(6.5);
        }

        private List<string> ZawinTekst(string tekst, XFont krój, double szerokosc)
        {
            var linie = new List<string>();

            if (string.IsNullOrWhiteSpace(tekst))
            {
                return linie;
            }

            string biezaca = string.Empty;

            foreach (string slowo in tekst.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string proba = biezaca.Length == 0 ? slowo : biezaca + " " + slowo;

                if (_rysik.MeasureString(proba, krój).Width <= szerokosc)
                {
                    biezaca = proba;
                    continue;
                }

                if (biezaca.Length > 0)
                {
                    linie.Add(biezaca);
                }

                biezaca = slowo;
            }

            if (biezaca.Length > 0)
            {
                linie.Add(biezaca);
            }

            return linie;
        }
    }
}
