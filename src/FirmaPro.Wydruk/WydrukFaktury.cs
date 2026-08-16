using FirmaPro.Domena;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;

namespace FirmaPro.Wydruk;

/// <summary>
/// Wizualizacja faktury w formacie PDF.
/// </summary>
/// <remarks>
/// <para>
/// Faktura wystawiona w KSeF krąży jako plik XML, ale kontrahent chce
/// dostać coś, co da się przeczytać i podpiąć pod przelew. Wizualizacja
/// nie jest fakturą w rozumieniu przepisów - jest jej czytelnym obrazem,
/// dlatego niesie kod QR pozwalający sprawdzić oryginał w systemie.
/// </para>
/// <para>
/// Układ wynika z art. 106e ustawy o VAT: dane obu stron, numer i daty,
/// nazwa towaru, ilość, cena jednostkowa netto, stawka, wartość netto,
/// kwota podatku i kwota należności, a przy zwolnieniu - podstawa prawna.
/// </para>
/// </remarks>
public static class WydrukFaktury
{
    /// <summary>Buduje plik PDF z wizualizacją faktury.</summary>
    public static byte[] Utworz(Faktura faktura, OpcjeWydruku? opcje = null)
    {
        ArgumentNullException.ThrowIfNull(faktura);

        using var rysownik = new Rysownik(faktura, opcje ?? OpcjeWydruku.DlaProjektu());
        return rysownik.Buduj();
    }

    /// <summary>
    /// Prowadzi rysowanie po kolejnych stronach dokumentu.
    /// </summary>
    /// <remarks>
    /// Klasa trzyma bieżące położenie pisaka i sama zakłada nową stronę, gdy
    /// treść przestaje się mieścić. Dzięki temu poszczególne sekcje nie muszą
    /// wiedzieć, na której stronie się znajdą.
    /// </remarks>
    private sealed class Rysownik(Faktura faktura, OpcjeWydruku opcje) : IDisposable
    {
        private readonly PdfDocument _dokument = new();
        private readonly List<XGraphics> _strony = [];

        private XGraphics _rysik = null!;
        private PdfPage _strona = null!;
        private double _y;

        public byte[] Buduj()
        {
            _dokument.Info.Title = "Faktura " + faktura.Numer;
            _dokument.Info.Creator = "Firma PRO";
            _dokument.Info.Subject = "Wizualizacja faktury FA(3)";

            NowaStrona();

            Naglowek();
            OstrzezenieOProjekcie();
            DaneKorekty();
            Strony();
            TabelaPozycji();
            Ogon();

            StopkiZeStronami();

            using var pamiec = new MemoryStream();
            _dokument.Save(pamiec);

            return pamiec.ToArray();
        }

        public void Dispose()
        {
            // Rysiki żyją do samego zapisu dokumentu - każdy trzyma stronę,
            // na której dopisujemy numerację dopiero na końcu.
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

            _strona = strona;
            _rysik = XGraphics.FromPdfPage(strona);
            _strony.Add(_rysik);
            _y = Styl.MarginesGorny;
        }

        /// <summary>Zakłada nową stronę, gdy żądana wysokość się nie mieści.</summary>
        private void ZapewnijMiejsce(double potrzeba)
        {
            if (_y + potrzeba > Styl.DolnaGranica)
            {
                NowaStrona();
            }
        }

        /// <summary>
        /// Dopisuje numerację stron.
        /// </summary>
        /// <remarks>
        /// Wykonalne dopiero na końcu: dopóki rysujemy treść, nie wiadomo,
        /// ile stron ostatecznie powstanie.
        /// </remarks>
        private void StopkiZeStronami()
        {
            for (int numer = 0; numer < _strony.Count; numer++)
            {
                XGraphics rysik = _strony[numer];
                double y = Styl.WysokoscStrony - Styl.MarginesDolny + Styl.Mm(6);

                rysik.DrawString($"Strona {numer + 1} z {_strony.Count}",
                    Styl.Mala, Styl.TekstSzary,
                    new XRect(Styl.Lewa, y, Styl.SzerokoscTresci, Styl.Mm(4)),
                    XStringFormats.TopRight);

                rysik.DrawString("Wystawiono w programie Firma PRO",
                    Styl.Mala, Styl.TekstSzary,
                    new XRect(Styl.Lewa, y, Styl.SzerokoscTresci, Styl.Mm(4)),
                    XStringFormats.TopLeft);
            }
        }

        // ----------------------------------------------------------- sekcje

        private void Naglowek()
        {
            string tytul = OpisRodzaju(faktura.Rodzaj) + " " + faktura.Numer;
            _rysik.DrawString(tytul, Styl.Tytul, Styl.Granat, Styl.Lewa, _y + Styl.Mm(6));

            // Daty po prawej stronie, jedna pod drugą.
            double yData = _y;
            var pary = new List<(string Etykieta, string Wartosc)>();

            if (!string.IsNullOrWhiteSpace(faktura.MiejsceWystawienia))
            {
                pary.Add(("Miejsce wystawienia", faktura.MiejsceWystawienia!));
            }

            pary.Add(("Data wystawienia", Styl.Data(faktura.DataWystawienia)));

            if (faktura.DataSprzedazy is DateOnly sprzedaz)
            {
                pary.Add(("Data sprzedaży", Styl.Data(sprzedaz)));
            }

            if (!string.IsNullOrWhiteSpace(opcje.Egzemplarz))
            {
                pary.Add(("Egzemplarz", opcje.Egzemplarz!));
            }

            foreach ((string etykieta, string wartosc) in pary)
            {
                var obszar = new XRect(Styl.Lewa, yData, Styl.SzerokoscTresci, Styl.Mm(4));

                _rysik.DrawString(etykieta + ":", Styl.Mala, Styl.TekstSzary,
                    new XRect(obszar.X, obszar.Y, obszar.Width - Styl.Mm(30), obszar.Height),
                    XStringFormats.TopRight);

                _rysik.DrawString(wartosc, Styl.Zwykla, Styl.Tekst, obszar,
                    XStringFormats.TopRight);

                yData += Styl.Mm(4.4);
            }

            _y = Math.Max(_y + Styl.Mm(11), yData) + Styl.Mm(3);

            if (!string.IsNullOrWhiteSpace(opcje.NumerKsef))
            {
                _rysik.DrawString("Numer KSeF: " + opcje.NumerKsef,
                    Styl.MalaWyrozniona, Styl.Tekst, Styl.Lewa, _y);
                _y += Styl.Mm(5);
            }

            _rysik.DrawLine(Styl.Linia, Styl.Lewa, _y, Styl.Prawa, _y);
            _y += Styl.Mm(5);
        }

        /// <summary>
        /// Ostrzeżenie na wydruku dokumentu, którego nie ma w KSeF.
        /// </summary>
        /// <remarks>
        /// Bez tego kartka wyglądałaby jak gotowa faktura, mimo że w obrocie
        /// prawnym faktura zaczyna istnieć dopiero po przyjęciu przez system.
        /// Odbiorca ma prawo od razu widzieć, z czym ma do czynienia.
        /// </remarks>
        private void OstrzezenieOProjekcie()
        {
            if (!opcje.Projekt)
            {
                return;
            }

            double wysokosc = Styl.Mm(9);
            var ramka = new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, wysokosc);

            _rysik.DrawRectangle(Styl.TloOstrzezenia, ramka);
            _rysik.DrawRectangle(Styl.Linia, ramka);

            _rysik.DrawString("PROJEKT — dokument nie został wysłany do KSeF",
                Styl.Wyrozniona, Styl.Czerwien,
                new XRect(ramka.X + Styl.Mm(3), ramka.Y + Styl.Mm(1.4),
                          ramka.Width - Styl.Mm(6), Styl.Mm(4)),
                XStringFormats.TopLeft);

            _rysik.DrawString(
                "Nie jest fakturą w rozumieniu przepisów i nie stanowi podstawy do odliczenia podatku.",
                Styl.Mala, Styl.Tekst,
                new XRect(ramka.X + Styl.Mm(3), ramka.Y + Styl.Mm(5.2),
                          ramka.Width - Styl.Mm(6), Styl.Mm(4)),
                XStringFormats.TopLeft);

            _y += wysokosc + Styl.Mm(4);
        }

        /// <summary>
        /// Wskazanie faktury korygowanej i przyczyny korekty.
        /// </summary>
        /// <remarks>
        /// Art. 106j ustawy o VAT wymaga, żeby korekta wskazywała dane faktury,
        /// której dotyczy. Bez tego odbiorca nie ma jak powiązać dokumentu
        /// z pierwotną transakcją, a wydruk nie spełnia wymogów faktury
        /// korygującej.
        /// </remarks>
        private void DaneKorekty()
        {
            if (!faktura.CzyKorekta || faktura.Korygowane.Count == 0)
            {
                return;
            }

            var wiersze = new List<(string Etykieta, string Wartosc)>();

            foreach (DaneFakturyKorygowanej korygowana in faktura.Korygowane)
            {
                wiersze.Add(("Faktura korygowana",
                    $"{korygowana.Numer} z {Styl.Data(korygowana.DataWystawienia)}"));

                if (!string.IsNullOrWhiteSpace(korygowana.NumerKsef))
                {
                    wiersze.Add(("Numer KSeF", korygowana.NumerKsef!));
                }
            }

            if (!string.IsNullOrWhiteSpace(faktura.PrzyczynaKorekty))
            {
                wiersze.Add(("Przyczyna korekty", faktura.PrzyczynaKorekty!));
            }

            double wysokosc = Styl.Mm(4) + (wiersze.Count * Styl.Mm(4.4));
            ZapewnijMiejsce(wysokosc + Styl.Mm(4));

            var ramka = new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, wysokosc);
            _rysik.DrawRectangle(Styl.TloJasne, ramka);
            _rysik.DrawRectangle(Styl.LiniaJasna, ramka);

            double yWiersza = _y + Styl.Mm(3.6);
            foreach ((string etykieta, string wartosc) in wiersze)
            {
                _rysik.DrawString(etykieta, Styl.Mala, Styl.TekstSzary,
                    Styl.Lewa + Styl.Mm(3), yWiersza);

                _rysik.DrawString(wartosc, Styl.Wyrozniona, Styl.Tekst,
                    Styl.Lewa + Styl.Mm(34), yWiersza);

                yWiersza += Styl.Mm(4.4);
            }

            _y += wysokosc + Styl.Mm(4);
        }

        /// <summary>Rysuje obok siebie dane sprzedawcy i nabywcy.</summary>
        private void Strony()
        {
            double odstep = Styl.Mm(6);
            double szerokosc = (Styl.SzerokoscTresci - odstep) / 2;

            List<string> sprzedawca = OpisPodmiotu(faktura.Sprzedawca);
            List<string> nabywca = OpisPodmiotu(faktura.Nabywca);

            int wierszy = Math.Max(sprzedawca.Count, nabywca.Count);
            double wysokosc = Styl.Mm(6) + (wierszy * Styl.Mm(4.2)) + Styl.Mm(2);

            ZapewnijMiejsce(wysokosc);

            PudelkoPodmiotu("Sprzedawca", sprzedawca, Styl.Lewa, szerokosc, wysokosc);
            PudelkoPodmiotu("Nabywca", nabywca, Styl.Lewa + szerokosc + odstep,
                            szerokosc, wysokosc);

            _y += wysokosc + Styl.Mm(5);

            PodmiotyInne();
        }

        /// <summary>
        /// Podmioty trzecie wypisane pod stronami transakcji.
        /// </summary>
        /// <remarks>
        /// Krótko i jedną linijką na podmiot: wydruk firmowy ma prowadzić wzrok
        /// do kwoty i terminu, a nie tonąć w danych. Pełne dane każdego podmiotu
        /// pokazuje wizualizacja KSeF. Pominąć ich jednak nie można - nabywca
        /// musi wiedzieć, że towar odbiera oddział albo że zapłata idzie
        /// do faktora.
        /// </remarks>
        private void PodmiotyInne()
        {
            if (faktura.PodmiotyInne.Count == 0)
            {
                return;
            }

            List<string> linie = faktura.PodmiotyInne
                .Select(p => $"{p.NazwaRoli}: {p.Dane.Nazwa}"
                             + (string.IsNullOrWhiteSpace(p.Dane.Nip)
                                 ? string.Empty
                                 : $", NIP {p.Dane.Nip}")
                             + (p.Udzial is { } udzial
                                 ? $", udział {Styl.Ilosc(udzial)}%"
                                 : string.Empty))
                .ToList();

            ZapewnijMiejsce((linie.Count * Styl.Mm(4)) + Styl.Mm(8));

            foreach (string linia in linie)
            {
                foreach (string zawinieta in
                         ZawinTekst(linia, Styl.Mala, Styl.SzerokoscTresci))
                {
                    _rysik.DrawString(zawinieta, Styl.Mala, Styl.TekstSzary,
                        Styl.Lewa, _y);

                    _y += Styl.Mm(3.6);
                }
            }

            _y += Styl.Mm(4);
        }

        private void PudelkoPodmiotu(string tytul, List<string> wiersze,
                                     double x, double szerokosc, double wysokosc)
        {
            var ramka = new XRect(x, _y, szerokosc, wysokosc);
            _rysik.DrawRectangle(Styl.TloJasne, ramka);
            _rysik.DrawRectangle(Styl.LiniaJasna, ramka);

            _rysik.DrawString(tytul.ToUpperInvariant(), Styl.Mala, Styl.TekstSzary,
                x + Styl.Mm(3), _y + Styl.Mm(4));

            double yWiersza = _y + Styl.Mm(8);
            bool pierwszy = true;

            foreach (string wiersz in wiersze)
            {
                _rysik.DrawString(wiersz, pierwszy ? Styl.Wyrozniona : Styl.Zwykla,
                    Styl.Tekst, x + Styl.Mm(3), yWiersza);

                yWiersza += Styl.Mm(4.2);
                pierwszy = false;
            }
        }

        // ---------------------------------------------------- tabela pozycji

        /// <summary>Szerokości kolumn w milimetrach; razem 180 mm.</summary>
        private static readonly double[] Kolumny = [7, 50, 11, 15, 19, 13, 21, 20, 24];

        private static readonly string[] NaglowkiKolumn =
        [
            "Lp.", "Nazwa towaru lub usługi", "J.m.", "Ilość", "Cena netto",
            "Stawka", "Wartość netto", "Kwota VAT", "Wartość brutto"
        ];

        private void TabelaPozycji()
        {
            // Na korekcie pokazujemy obie wersje pozycji. Sama różnica
            // w podsumowaniu nie mówi odbiorcy, co właściwie się zmieniło.
            if (faktura.PozycjePrzedKorekta.Count > 0)
            {
                PodpisGrupy("Przed korektą");
                RysujPozycje(faktura.PozycjePrzedKorekta);
                PodpisGrupy("Po korekcie");
            }

            RysujPozycje(faktura.Pozycje);
        }

        /// <summary>Podpis nad grupą pozycji faktury korygującej.</summary>
        private void PodpisGrupy(string tekst)
        {
            ZapewnijMiejsce(Styl.Mm(10));

            _y += Styl.Mm(3);
            _rysik.DrawString(tekst.ToUpperInvariant(), Styl.MalaWyrozniona,
                Styl.TekstSzary, Styl.Lewa, _y + Styl.Mm(3));

            _y += Styl.Mm(4.5);
        }

        private void RysujPozycje(IReadOnlyList<PozycjaFaktury> pozycje)
        {
            ZapewnijMiejsce(Styl.Mm(22));
            NaglowekTabeli();

            int lp = 1;
            foreach (PozycjaFaktury pozycja in pozycje)
            {
                List<string> linieNazwy = ZawinTekst(
                    OpisPozycji(pozycja), Styl.Tabela, Styl.Mm(Kolumny[1]) - Styl.Mm(3));

                double wysokosc = Math.Max(Styl.Mm(5.5),
                    (linieNazwy.Count * Styl.Mm(3.4)) + Styl.Mm(2.2));

                // Wiersz nigdy nie jest dzielony między strony - przełamana
                // w połowie nazwa towaru jest nieczytelna.
                if (_y + wysokosc > Styl.DolnaGranica)
                {
                    NowaStrona();
                    NaglowekTabeli();
                }

                WierszPozycji(lp++, pozycja, linieNazwy, wysokosc);
            }
        }

        private void NaglowekTabeli()
        {
            double wysokosc = Styl.Mm(6);
            var ramka = new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, wysokosc);
            _rysik.DrawRectangle(Styl.TloNaglowka, ramka);

            double x = Styl.Lewa;
            for (int i = 0; i < Kolumny.Length; i++)
            {
                double szerokosc = Styl.Mm(Kolumny[i]);

                _rysik.DrawString(NaglowkiKolumn[i], Styl.TabelaNaglowek, Styl.Biel,
                    new XRect(x + Styl.Mm(1.5), _y + Styl.Mm(1.4),
                              szerokosc - Styl.Mm(3), Styl.Mm(4)),
                    FormatKolumny(i));

                x += szerokosc;
            }

            _y += wysokosc;
        }

        private void WierszPozycji(int lp, PozycjaFaktury pozycja,
                                   List<string> linieNazwy, double wysokosc)
        {
            string[] wartosci =
            [
                lp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Empty, // nazwa rysowana osobno, bo bywa wielolinijkowa
                pozycja.Jednostka,
                Styl.Ilosc(pozycja.Ilosc),
                Styl.Kwota(pozycja.CenaNetto),
                pozycja.Stawka.Opis,
                Styl.Kwota(pozycja.WartoscNetto),
                Styl.Kwota(pozycja.KwotaVat),
                Styl.Kwota(pozycja.WartoscBrutto)
            ];

            double x = Styl.Lewa;
            for (int i = 0; i < Kolumny.Length; i++)
            {
                double szerokosc = Styl.Mm(Kolumny[i]);

                if (i == 1)
                {
                    double yLinii = _y + Styl.Mm(1.6);
                    foreach (string linia in linieNazwy)
                    {
                        _rysik.DrawString(linia, Styl.Tabela, Styl.Tekst,
                            x + Styl.Mm(1.5), yLinii + Styl.Mm(2.4));
                        yLinii += Styl.Mm(3.4);
                    }
                }
                else
                {
                    _rysik.DrawString(wartosci[i], Styl.Tabela, Styl.Tekst,
                        new XRect(x + Styl.Mm(1.5), _y + Styl.Mm(1.6),
                                  szerokosc - Styl.Mm(3), Styl.Mm(4)),
                        FormatKolumny(i));
                }

                x += szerokosc;
            }

            _y += wysokosc;
            _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Styl.Prawa, _y);
        }

        /// <summary>Wyrównanie zawartości kolumny.</summary>
        private static XStringFormat FormatKolumny(int kolumna) => kolumna switch
        {
            0 or 2 or 5 => XStringFormats.TopCenter,
            1 => XStringFormats.TopLeft,
            _ => XStringFormats.TopRight
        };

        // ------------------------------------------------------------- ogon

        /// <summary>
        /// Rysuje zakończenie faktury: podsumowanie, płatność, kod QR i uwagi.
        /// </summary>
        /// <remarks>
        /// Te sekcje trzymamy razem na jednej stronie. Kwota do zapłaty
        /// oderwana od podsumowania stawek albo kod QR przeniesiony samotnie
        /// na kolejną kartkę wyglądają na usterkę wydruku, a przy dokumencie
        /// księgowym każda taka wątpliwość kosztuje telefon od kontrahenta.
        /// </remarks>
        private void Ogon()
        {
            PodsumowanieFaktury podsumowanie = faktura.Podsumowanie();
            List<(string Etykieta, string Wartosc)> platnosc = WierszePlatnosci();
            List<string> uwagi = LinieUwag();

            double wysokosc = WysokoscPodsumowania(podsumowanie)
                            + WysokoscPrzeliczenia()
                            + WysokoscPlatnosci(platnosc)
                            + WysokoscKoduQr()
                            + WysokoscUwag(uwagi);

            // Gdy zakończenie samo w sobie nie mieści się na stronie (bardzo
            // długa stopka), przenoszenie go w całości niczego nie da - wtedy
            // pozwalamy mu popłynąć dalej, zamiast zostawiać pustą kartkę.
            if (wysokosc <= Styl.DolnaGranica - Styl.MarginesGorny)
            {
                ZapewnijMiejsce(wysokosc);
            }

            Podsumowanie(podsumowanie);
            Platnosc(platnosc);
            KodWeryfikacyjny();
            Uwagi(uwagi);
        }

        private static double WysokoscPodsumowania(PodsumowanieFaktury podsumowanie) =>
            Styl.Mm(4) + Styl.Mm(5.5)
            + ((podsumowanie.WedlugStawek.Count + 1) * Styl.Mm(5))
            + Styl.Mm(4);

        /// <summary>Wiersz z kwotami w złotych i podpis z kursem pod tabelą.</summary>
        private double WysokoscPrzeliczenia() =>
            faktura.Walutowa && faktura.Kurs is not null ? Styl.Mm(5) + Styl.Mm(3) : 0;

        private static double WysokoscPlatnosci(List<(string, string)> wiersze) =>
            Styl.Mm(2)
            + Math.Max(wiersze.Count * Styl.Mm(4.4), Styl.Mm(14))
            + Styl.Mm(2) + Styl.Mm(8);

        private double WysokoscKoduQr() =>
            string.IsNullOrWhiteSpace(opcje.LinkWeryfikacyjny)
                ? 0
                : Styl.Mm(26) + Styl.Mm(4);

        private static double WysokoscUwag(List<string> linie) =>
            linie.Count == 0 ? 0 : Styl.Mm(5) + (linie.Count * Styl.Mm(3.6));

        /// <summary>Warunki płatności wypisywane pod tabelą.</summary>
        private List<(string Etykieta, string Wartosc)> WierszePlatnosci()
        {
            var wiersze = new List<(string, string)>();

            if (faktura.Platnosc.Forma is FormaPlatnosci forma)
            {
                wiersze.Add(("Forma płatności", OpisFormy(forma)));
            }

            if (faktura.Platnosc.Termin is DateOnly termin)
            {
                wiersze.Add(("Termin płatności", Styl.Data(termin)));
            }

            if (faktura.Platnosc.Zaplacono)
            {
                wiersze.Add(("Zapłacono", faktura.Platnosc.DataZaplaty is DateOnly zaplata
                    ? Styl.Data(zaplata)
                    : "tak"));
            }

            // Bez tych wierszy odbiorca faktury końcowej nie wie, skąd wzięła
            // się kwota do zapłaty mniejsza od wartości dostawy.
            foreach (DaneZaliczki zaliczka in faktura.Zaliczkowe)
            {
                wiersze.Add(($"Zaliczka {zaliczka.Numer}",
                    Styl.Kwota(zaliczka.Brutto) + " " + faktura.Waluta));
            }

            if (!string.IsNullOrWhiteSpace(faktura.Platnosc.Rachunek))
            {
                wiersze.Add(("Rachunek", Styl.Rachunek(faktura.Platnosc.Rachunek!)));
            }

            if (!string.IsNullOrWhiteSpace(faktura.Platnosc.NazwaBanku))
            {
                wiersze.Add(("Bank", faktura.Platnosc.NazwaBanku!));
            }

            return wiersze;
        }

        /// <summary>Uwagi drukowane na dole dokumentu.</summary>
        private List<string> LinieUwag()
        {
            var linie = new List<string>();

            if (!string.IsNullOrWhiteSpace(faktura.PodstawaZwolnienia))
            {
                linie.Add("Podstawa zwolnienia: " + faktura.PodstawaZwolnienia);
            }

            if (!string.IsNullOrWhiteSpace(faktura.Stopka))
            {
                linie.AddRange(ZawinTekst(faktura.Stopka!, Styl.Mala, Styl.SzerokoscTresci));
            }

            return linie;
        }

        // ------------------------------------------------------ podsumowanie

        private void Podsumowanie(PodsumowanieFaktury podsumowanie)
        {
            _y += Styl.Mm(4);

            // Tabela stawek wyrównana do prawej krawędzi treści.
            double[] kolumny = [22, 27, 24, 27];
            double szerokosc = kolumny.Sum(k => Styl.Mm(k));
            double lewa = Styl.Prawa - szerokosc;

            string[] naglowki = ["Stawka", "Netto", "VAT", "Brutto"];

            var ramkaNaglowka = new XRect(lewa, _y, szerokosc, Styl.Mm(5.5));
            _rysik.DrawRectangle(Styl.TloJasne, ramkaNaglowka);
            _rysik.DrawRectangle(Styl.LiniaJasna, ramkaNaglowka);

            double x = lewa;
            for (int i = 0; i < kolumny.Length; i++)
            {
                _rysik.DrawString(naglowki[i], Styl.MalaWyrozniona, Styl.TekstSzary,
                    new XRect(x + Styl.Mm(1.5), _y + Styl.Mm(1.4),
                              Styl.Mm(kolumny[i]) - Styl.Mm(3), Styl.Mm(4)),
                    i == 0 ? XStringFormats.TopLeft : XStringFormats.TopRight);

                x += Styl.Mm(kolumny[i]);
            }

            _y += Styl.Mm(5.5);

            foreach (PozycjaPodsumowania wiersz in podsumowanie.WedlugStawek)
            {
                string[] wartosci =
                [
                    wiersz.Stawka.Opis,
                    Styl.Kwota(wiersz.Netto),
                    Styl.Kwota(wiersz.Vat),
                    Styl.Kwota(wiersz.Brutto)
                ];

                RysujWierszPodsumowania(lewa, kolumny, wartosci, Styl.Tabela);
            }

            string[] razem =
            [
                "Razem",
                Styl.Kwota(podsumowanie.RazemNetto),
                Styl.Kwota(podsumowanie.RazemVat),
                Styl.Kwota(podsumowanie.RazemBrutto)
            ];

            RysujWierszPodsumowania(lewa, kolumny, razem, Styl.TabelaNaglowek);

            PrzeliczenieNaZlote(podsumowanie, lewa, kolumny);

            _rysik.DrawLine(Styl.Linia, lewa, _y, Styl.Prawa, _y);
            _y += Styl.Mm(4);

            PodpisKursu();
        }

        /// <summary>
        /// Wiersz z kwotami przeliczonymi na złote - tylko przy walucie obcej.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Faktura wystawiona w obcej walucie musi wykazywać kwotę podatku
        /// w złotych (art. 106e ust. 11 ustawy). Bez niej nabywca nie ma czego
        /// wpisać do własnego rejestru, a sam wydruk jest wadliwy. Netto
        /// i brutto stoją obok, bo faktura służy też za dowód w księgach
        /// obu stron, a tam wszystko liczy się w złotych.
        /// </para>
        /// <para>
        /// Wiersz jest wyszarzony i podpisany walutą, żeby nikt nie wziął go
        /// za kwotę do zapłaty - płatność idzie w walucie faktury, a ta stoi
        /// niżej, wytłuszczona, pod napisem „Do zapłaty".
        /// </para>
        /// <para>
        /// Przeliczamy stawka po stawce, dokładnie tak jak dokument wysyłany
        /// do KSeF. Przeliczenie samej sumy potrafi dać wynik różniący się
        /// o grosz, a wydruk i plik muszą mówić to samo. Brutto jest sumą
        /// dwóch pozostałych kwot - inaczej wiersz nie sumowałby się w poprzek.
        /// </para>
        /// </remarks>
        private void PrzeliczenieNaZlote(PodsumowanieFaktury podsumowanie, double lewa,
                                         double[] kolumny)
        {
            if (!faktura.Walutowa || faktura.Kurs is not KursWaluty kurs)
            {
                return;
            }

            decimal netto = podsumowanie.WedlugStawek
                .Sum(s => Przeliczenie.NaZlote(s.Netto, kurs));

            decimal vat = podsumowanie.WedlugStawek
                .Sum(s => Przeliczenie.NaZlote(s.Vat, kurs));

            string[] wZlotych =
            [
                "W PLN",
                Styl.Kwota(netto),
                Styl.Kwota(vat),
                Styl.Kwota(netto + vat)
            ];

            RysujWierszPodsumowania(lewa, kolumny, wZlotych, Styl.Tabela, Styl.TekstSzary);
        }

        /// <summary>
        /// Skąd wzięty kurs.
        /// </summary>
        /// <remarks>
        /// Numer tabeli i jej data rozstrzygają spór o przeliczenie - odbiorca
        /// może sprawdzić kurs u źródła, zamiast wierzyć wystawcy na słowo.
        /// </remarks>
        private void PodpisKursu()
        {
            if (!faktura.Walutowa || faktura.Kurs is not KursWaluty kurs)
            {
                return;
            }

            string tabela = string.IsNullOrWhiteSpace(kurs.Tabela)
                ? Styl.Data(kurs.ZDnia)
                : kurs.Tabela + " z " + Styl.Data(kurs.ZDnia);

            _rysik.DrawString(
                $"Kurs: 1 {kurs.Waluta} = {Styl.Kurs(kurs.Wartosc)} PLN (tabela NBP {tabela})",
                Styl.Mala, Styl.TekstSzary,
                new XRect(Styl.Lewa, _y - Styl.Mm(2), Styl.SzerokoscTresci, Styl.Mm(4)),
                XStringFormats.TopRight);

            _y += Styl.Mm(3);
        }

        private void RysujWierszPodsumowania(double lewa, double[] kolumny,
                                             string[] wartosci, XFont krój,
                                             XBrush? atrament = null)
        {
            double x = lewa;
            for (int i = 0; i < kolumny.Length; i++)
            {
                _rysik.DrawString(wartosci[i], krój, atrament ?? Styl.Tekst,
                    new XRect(x + Styl.Mm(1.5), _y + Styl.Mm(1.2),
                              Styl.Mm(kolumny[i]) - Styl.Mm(3), Styl.Mm(4)),
                    i == 0 ? XStringFormats.TopLeft : XStringFormats.TopRight);

                x += Styl.Mm(kolumny[i]);
            }

            _y += Styl.Mm(5);
        }

        // ----------------------------------------------------------- płatność

        private void Platnosc(List<(string Etykieta, string Wartosc)> wiersze)
        {
            _y += Styl.Mm(2);

            double yStart = _y;

            // Faktura końcowa obejmuje całą dostawę, ale nabywca zapłacił już
            // zaliczki - do zapłaty zostaje sama różnica (art. 106f ust. 3).
            decimal pozostaje = faktura.DoZaplaty;

            // Po prawej: kwota do zapłaty - to jej odbiorca szuka najpierw.
            string doZaplaty = Styl.Kwota(pozostaje) + " " + faktura.Waluta;

            _rysik.DrawString("Do zapłaty", Styl.Mala, Styl.TekstSzary,
                new XRect(Styl.Lewa, _y, Styl.SzerokoscTresci, Styl.Mm(4)),
                XStringFormats.TopRight);

            _rysik.DrawString(doZaplaty, Styl.DoZaplaty, Styl.Granat,
                new XRect(Styl.Lewa, _y + Styl.Mm(3.6), Styl.SzerokoscTresci, Styl.Mm(8)),
                XStringFormats.TopRight);

            // Po lewej: warunki płatności.
            foreach ((string etykieta, string wartosc) in wiersze)
            {
                _rysik.DrawString(etykieta, Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y + Styl.Mm(3));
                _rysik.DrawString(wartosc, Styl.Zwykla, Styl.Tekst,
                    Styl.Lewa + Styl.Mm(28), _y + Styl.Mm(3));

                _y += Styl.Mm(4.4);
            }

            _y = Math.Max(_y, yStart + Styl.Mm(14)) + Styl.Mm(2);

            // Kwota słownie - utrudnia podrobienie dokumentu.
            _rysik.DrawString("Słownie: " + Slownie.Kwota(pozostaje, faktura.Waluta),
                Styl.Zwykla, Styl.Tekst, Styl.Lewa, _y + Styl.Mm(3));

            _y += Styl.Mm(8);
        }

        /// <summary>Rysuje kod QR pozwalający sprawdzić fakturę w KSeF.</summary>
        private void KodWeryfikacyjny()
        {
            if (string.IsNullOrWhiteSpace(opcje.LinkWeryfikacyjny))
            {
                return;
            }

            double bok = WysokoscKoduQr() - Styl.Mm(4);

            RysujKodQr(opcje.LinkWeryfikacyjny!, Styl.Lewa, _y, bok);

            double xOpisu = Styl.Lewa + bok + Styl.Mm(4);

            _rysik.DrawString("Kod weryfikacyjny KSeF", Styl.Wyrozniona, Styl.Tekst,
                xOpisu, _y + Styl.Mm(5));

            _rysik.DrawString(
                "Zeskanuj, aby sprawdzić w Krajowym Systemie e-Faktur, czy ta faktura",
                Styl.Mala, Styl.TekstSzary, xOpisu, _y + Styl.Mm(10));

            _rysik.DrawString(
                "znajduje się w systemie i czy jej treść nie została zmieniona.",
                Styl.Mala, Styl.TekstSzary, xOpisu, _y + Styl.Mm(13.6));

            // Na papierze działa kod QR, ale fakturę częściej ogląda się na
            // ekranie - a wtedy skanowanie własnego monitora telefonem jest
            // drogą naokoło. Ten sam adres jest więc do kliknięcia.
            const string opisOdnosnika = "Otwórz fakturę w KSeF";
            double yOdnosnika = _y + Styl.Mm(18);

            _rysik.DrawString(opisOdnosnika, Styl.MalaWyrozniona, Styl.Granat,
                xOpisu, yOdnosnika);

            double szerokosc = _rysik.MeasureString(opisOdnosnika, Styl.MalaWyrozniona).Width;

            // Podkreślenie: bez niego nic nie sugeruje, że to odnośnik.
            _rysik.DrawLine(Styl.Linia, xOpisu, yOdnosnika + Styl.Mm(0.8),
                xOpisu + szerokosc, yOdnosnika + Styl.Mm(0.8));

            DodajOdnosnik(xOpisu, yOdnosnika - Styl.Mm(3), szerokosc, Styl.Mm(4),
                opcje.LinkWeryfikacyjny!);

            // Sam kod też jest klikalny - to najbardziej naturalne miejsce,
            // w które czytelnik celuje myszą.
            DodajOdnosnik(Styl.Lewa, _y, bok, bok, opcje.LinkWeryfikacyjny!);

            _y += bok + Styl.Mm(4);
        }

        /// <summary>
        /// Zaznacza obszar strony jako odnośnik do wskazanego adresu.
        /// </summary>
        /// <remarks>
        /// Odnośniki w PDF liczone są względem lewego dolnego rogu strony,
        /// a rysowanie prowadzimy od lewego górnego - przeliczenie robi
        /// przekształcenie z biblioteki, żeby nie powielać tu jej arytmetyki.
        /// </remarks>
        private void DodajOdnosnik(double x, double y, double szerokosc, double wysokosc,
                                   string adres)
        {
            XPoint lewyGorny = _rysik.Transformer.WorldToDefaultPage(new XPoint(x, y));
            XPoint prawyDolny = _rysik.Transformer.WorldToDefaultPage(
                new XPoint(x + szerokosc, y + wysokosc));

            _strona.AddWebLink(
                new PdfRectangle(
                    new XPoint(lewyGorny.X, _strona.Height.Point - prawyDolny.Y),
                    new XPoint(prawyDolny.X, _strona.Height.Point - lewyGorny.Y)),
                adres);
        }

        /// <summary>
        /// Rysuje kod QR z prostokątów, a nie z obrazka.
        /// </summary>
        /// <remarks>
        /// Kod narysowany wektorowo pozostaje ostry przy każdej rozdzielczości
        /// drukarki i nie rozmywa się przy powiększeniu na ekranie - a właśnie
        /// ostrość krawędzi decyduje o tym, czy telefon go odczyta. Mapa bitowa
        /// musiałaby mieć konkretną rozdzielczość dobraną w ciemno.
        /// </remarks>
        private void RysujKodQr(string tresc, double x, double y, double bok)
        {
            using var generator = new QRCodeGenerator();

            // Korekcja M znosi około 15% uszkodzeń - tyle, ile psuje wydruk
            // na słabej drukarce albo zagięcie kartki.
            using QRCodeData dane =
                generator.CreateQrCode(tresc, QRCodeGenerator.ECCLevel.M);

            int modulow = dane.ModuleMatrix.Count;
            double modul = bok / modulow;

            _rysik.DrawRectangle(Styl.Biel, x, y, bok, bok);

            for (int wiersz = 0; wiersz < modulow; wiersz++)
            {
                System.Collections.BitArray linia = dane.ModuleMatrix[wiersz];

                // Ciemne moduły w jednym wierszu łączymy w jeden prostokąt.
                // Rysowane osobno, sąsiadujące kwadraty potrafią zostawić
                // jasne szpary na krawędziach i utrudnić odczyt.
                int poczatek = -1;
                for (int kolumna = 0; kolumna <= modulow; kolumna++)
                {
                    bool ciemny = kolumna < modulow && linia[kolumna];

                    if (ciemny && poczatek < 0)
                    {
                        poczatek = kolumna;
                    }
                    else if (!ciemny && poczatek >= 0)
                    {
                        _rysik.DrawRectangle(Styl.Czern,
                            x + (poczatek * modul), y + (wiersz * modul),
                            (kolumna - poczatek) * modul, modul);

                        poczatek = -1;
                    }
                }
            }
        }

        // -------------------------------------------------------------- uwagi

        private void Uwagi(List<string> linie)
        {
            if (linie.Count == 0)
            {
                return;
            }

            _y += Styl.Mm(2);
            _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Styl.Prawa, _y);
            _y += Styl.Mm(3);

            foreach (string linia in linie)
            {
                _rysik.DrawString(linia, Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y);
                _y += Styl.Mm(3.6);
            }
        }

        // --------------------------------------------------------- pomocnicze

        /// <summary>Łamie tekst na linie mieszczące się w zadanej szerokości.</summary>
        private List<string> ZawinTekst(string tekst, XFont krój, double szerokosc)
        {
            var linie = new List<string>();
            string biezaca = string.Empty;

            foreach (string slowo in (tekst ?? string.Empty)
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
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

                // Pojedyncze słowo dłuższe niż kolumna (np. długi kod towaru)
                // trzeba przeciąć, inaczej wystawałoby poza tabelę.
                biezaca = _rysik.MeasureString(slowo, krój).Width <= szerokosc
                    ? slowo
                    : PrzytnijSlowo(slowo, krój, szerokosc, linie);
            }

            if (biezaca.Length > 0)
            {
                linie.Add(biezaca);
            }

            return linie.Count > 0 ? linie : [string.Empty];
        }

        private string PrzytnijSlowo(string slowo, XFont krój, double szerokosc,
                                     List<string> linie)
        {
            string reszta = slowo;

            while (_rysik.MeasureString(reszta, krój).Width > szerokosc && reszta.Length > 1)
            {
                int znakow = reszta.Length;
                while (znakow > 1 &&
                       _rysik.MeasureString(reszta[..znakow], krój).Width > szerokosc)
                {
                    znakow--;
                }

                linie.Add(reszta[..znakow]);
                reszta = reszta[znakow..];
            }

            return reszta;
        }

        private static List<string> OpisPodmiotu(Podmiot podmiot)
        {
            var linie = new List<string> { podmiot.Nazwa };

            if (!string.IsNullOrWhiteSpace(podmiot.Adres.Linia1))
            {
                linie.Add(podmiot.Adres.Linia1);
            }

            if (!string.IsNullOrWhiteSpace(podmiot.Adres.Linia2))
            {
                linie.Add(podmiot.Adres.Linia2!);
            }

            if (!string.IsNullOrWhiteSpace(podmiot.Nip))
            {
                linie.Add("NIP: " + podmiot.Nip);
            }
            else if (!string.IsNullOrWhiteSpace(podmiot.NrVatUe))
            {
                linie.Add("VAT UE: " + podmiot.KodUe + podmiot.NrVatUe);
            }

            if (!string.IsNullOrWhiteSpace(podmiot.Email))
            {
                linie.Add(podmiot.Email!);
            }

            if (!string.IsNullOrWhiteSpace(podmiot.Telefon))
            {
                linie.Add("tel. " + podmiot.Telefon);
            }

            return linie;
        }

        /// <summary>Nazwa pozycji wraz z oznaczeniami, które muszą być widoczne.</summary>
        private static string OpisPozycji(PozycjaFaktury pozycja)
        {
            var czesci = new List<string> { pozycja.Nazwa };

            if (!string.IsNullOrWhiteSpace(pozycja.Indeks))
            {
                czesci.Add("(indeks " + pozycja.Indeks + ")");
            }

            if (!string.IsNullOrWhiteSpace(pozycja.Pkwiu))
            {
                czesci.Add("PKWiU " + pozycja.Pkwiu);
            }

            if (!string.IsNullOrWhiteSpace(pozycja.Cn))
            {
                czesci.Add("CN " + pozycja.Cn);
            }

            if (!string.IsNullOrWhiteSpace(pozycja.Gtu))
            {
                czesci.Add(pozycja.Gtu!);
            }

            return string.Join(' ', czesci);
        }

        private static string OpisRodzaju(RodzajFaktury rodzaj) => rodzaj switch
        {
            RodzajFaktury.Korygujaca => "Faktura korygująca",
            RodzajFaktury.Zaliczkowa => "Faktura zaliczkowa",
            RodzajFaktury.Rozliczeniowa => "Faktura rozliczeniowa",
            RodzajFaktury.Uproszczona => "Faktura uproszczona",
            _ => "Faktura"
        };

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
    }
}
