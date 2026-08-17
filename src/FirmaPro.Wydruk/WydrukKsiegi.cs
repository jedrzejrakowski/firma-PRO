using System.Globalization;
using FirmaPro.Domena;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FirmaPro.Wydruk;

/// <summary>
/// Wydruk podatkowej księgi przychodów i rozchodów oraz ewidencji przychodów.
/// </summary>
/// <remarks>
/// <para>
/// Plik CSV jest wygodny dla księgowej, ale <b>księgą</b> jest to, co da się
/// wydrukować i przechowywać. Podatnik ma obowiązek przechowywać księgę wraz
/// z dowodami, na których podstawie powstały zapisy - arkusz kalkulacyjny
/// tego nie zastąpi.
/// </para>
/// <para>
/// Strona jest pozioma, bo szesnaście kolumn rozporządzenia nie mieści się
/// w pionie w rozmiarze, który dałoby się przeczytać.
/// </para>
/// </remarks>
public static class WydrukKsiegi
{
    /// <summary>Buduje wydruk księgi przychodów i rozchodów.</summary>
    public static byte[] Utworz(Kpir ksiega, string nazwaFirmy, string nip)
    {
        ArgumentNullException.ThrowIfNull(ksiega);

        using var rysownik = new Rysownik(nazwaFirmy, nip, ksiega.Okres);

        return rysownik.BudujKsiege(ksiega);
    }

    /// <summary>Buduje wydruk ewidencji przychodów.</summary>
    public static byte[] Utworz(EwidencjaRyczaltu ewidencja, string nazwaFirmy, string nip)
    {
        ArgumentNullException.ThrowIfNull(ewidencja);

        using var rysownik = new Rysownik(nazwaFirmy, nip, ewidencja.Okres);

        return rysownik.BudujEwidencje(ewidencja);
    }

    private sealed class Rysownik(string nazwaFirmy, string nip, OkresRozliczeniowy okres)
        : IDisposable
    {
        private readonly PdfDocument _dokument = new();
        private readonly List<XGraphics> _strony = [];

        private XGraphics _rysik = null!;
        private double _y;
        private string _tytul = string.Empty;
        private double[] _kolumny = [];
        private string[] _naglowki = [];

        /// <summary>Szerokość arkusza poziomego.</summary>
        private static double Szerokosc => Styl.WysokoscStrony;

        private static double Wysokosc => Styl.SzerokoscStrony;

        private static double Prawa => Szerokosc - Styl.MarginesPrawy;

        private static double SzerokoscTresci => Prawa - Styl.Lewa;

        private static double DolnaGranica => Wysokosc - Styl.MarginesDolny;

        // --------------------------------------------------------------- KPiR

        /// <summary>
        /// Szerokości kolumn księgi w milimetrach.
        /// </summary>
        /// <remarks>
        /// Razem 267 mm - tyle zostaje na arkuszu poziomym po marginesach.
        /// Kolumny kwotowe są węższe od opisowych, bo liczba zajmuje mniej
        /// miejsca niż nazwa kontrahenta.
        /// </remarks>
        private static readonly double[] KolumnyKsiegi =
            [7, 15, 20, 26, 26, 28, 16, 14, 16, 16, 14, 14, 16, 16, 11, 12];

        private static readonly string[] NaglowkiKsiegi =
        [
            "Lp.", "Data", "Nr dowodu", "Kontrahent", "Adres", "Opis zdarzenia",
            "7 Sprze- daż", "8 Pozo- stałe", "9 Razem przychód",
            "10 Zakup towarów", "11 Koszty uboczne", "12 Wyna- grodzenia",
            "13 Pozo- stałe", "14 Razem wydatki", "15 B+R", "16 Uwagi"
        ];

        /// <summary>Kolumny kwotowe w kolejności z wydruku.</summary>
        private static readonly KolumnaKpir?[] KolejnoscKwot =
        [
            KolumnaKpir.SprzedazTowarowIUslug,
            KolumnaKpir.PozostalePrzychody,
            null,                               // 9 - suma, pusta w wierszu
            KolumnaKpir.ZakupTowarow,
            KolumnaKpir.KosztyUboczneZakupu,
            KolumnaKpir.Wynagrodzenia,
            KolumnaKpir.PozostaleWydatki,
            null,                               // 14 - suma, pusta w wierszu
            KolumnaKpir.BadaniaIRozwoj
        ];

        public byte[] BudujKsiege(Kpir ksiega)
        {
            _tytul = "Podatkowa księga przychodów i rozchodów";
            _kolumny = KolumnyKsiegi;
            _naglowki = NaglowkiKsiegi;

            _dokument.Info.Title = _tytul + " — " + okres.Nazwa;
            _dokument.Info.Creator = "Firma PRO";

            NowaStrona();

            int lp = 1;

            foreach (WpisKsiegi wpis in ksiega.Wpisy)
            {
                WierszKsiegi(lp++, wpis);
            }

            SumyKsiegi(ksiega);
            Stopki();

            return Zapisz();
        }

        private void WierszKsiegi(int lp, WpisKsiegi wpis)
        {
            List<string> nazwa = Zawin(wpis.Kontrahent, Styl.Tabela,
                Styl.Mm(_kolumny[3]) - Styl.Mm(2));
            List<string> adres = Zawin(wpis.Adres, Styl.Tabela,
                Styl.Mm(_kolumny[4]) - Styl.Mm(2));
            List<string> opis = Zawin(wpis.Opis, Styl.Tabela,
                Styl.Mm(_kolumny[5]) - Styl.Mm(2));
            List<string> uwagi = Zawin(wpis.Uwagi, Styl.Tabela,
                Styl.Mm(_kolumny[15]) - Styl.Mm(2));

            int linii = Math.Max(1, Math.Max(Math.Max(opis.Count, uwagi.Count),
                                             Math.Max(nazwa.Count, adres.Count)));
            double wysokosc = (linii * Styl.Mm(3.4)) + Styl.Mm(1.6);

            ZapewnijMiejsce(wysokosc);

            var wartosci = new List<string>
            {
                lp.ToString(CultureInfo.InvariantCulture),
                Styl.Data(wpis.Data),
                wpis.NumerDowodu,
                string.Empty,
                string.Empty,
                string.Empty
            };

            foreach (KolumnaKpir? kolumna in KolejnoscKwot)
            {
                wartosci.Add(kolumna == wpis.Kolumna ? Styl.Kwota(wpis.Kwota) : string.Empty);
            }

            wartosci.Add(string.Empty);

            RysujWiersz([.. wartosci], Styl.Tabela, wysokosc);

            // Kolumny łamane rysujemy osobno, bo mogą mieć kilka linijek.
            RysujZawiniete(nazwa, 3, wysokosc);
            RysujZawiniete(adres, 4, wysokosc);
            RysujZawiniete(opis, 5, wysokosc);
            RysujZawiniete(uwagi, 15, wysokosc);

            _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Prawa, _y);
        }

        private void SumyKsiegi(Kpir ksiega)
        {
            ZapewnijMiejsce(Styl.Mm(16));

            WierszSumy("Razem okres",
            [
                ksiega.Kolumna(KolumnaKpir.SprzedazTowarowIUslug),
                ksiega.Kolumna(KolumnaKpir.PozostalePrzychody),
                ksiega.RazemPrzychod,
                ksiega.Kolumna(KolumnaKpir.ZakupTowarow),
                ksiega.Kolumna(KolumnaKpir.KosztyUboczneZakupu),
                ksiega.Kolumna(KolumnaKpir.Wynagrodzenia),
                ksiega.Kolumna(KolumnaKpir.PozostaleWydatki),
                ksiega.RazemWydatki,
                ksiega.Kolumna(KolumnaKpir.BadaniaIRozwoj)
            ]);

            WierszSumy("Narastająco od 1 stycznia",
            [
                ksiega.Narastajaco(KolumnaKpir.SprzedazTowarowIUslug),
                ksiega.Narastajaco(KolumnaKpir.PozostalePrzychody),
                ksiega.PrzychodNarastajaco,
                ksiega.Narastajaco(KolumnaKpir.ZakupTowarow),
                ksiega.Narastajaco(KolumnaKpir.KosztyUboczneZakupu),
                ksiega.Narastajaco(KolumnaKpir.Wynagrodzenia),
                ksiega.Narastajaco(KolumnaKpir.PozostaleWydatki),
                ksiega.WydatkiNarastajaco,
                ksiega.Narastajaco(KolumnaKpir.BadaniaIRozwoj)
            ]);

            _y += Styl.Mm(4);

            _rysik.DrawString(
                "Dochód narastająco: " + Styl.Kwota(ksiega.DochodNarastajaco) + " zł",
                Styl.Wyrozniona, Styl.Tekst,
                new XRect(Styl.Lewa, _y, SzerokoscTresci, Styl.Mm(5)),
                XStringFormats.TopRight);

            _y += Styl.Mm(6);

            // Zastrzeżenie na papierze, nie tylko na ekranie - wydruk bywa
            // jedynym, co trafia do księgowej albo do segregatora.
            _rysik.DrawString(
                "Dochód nie uwzględnia spisu z natury ani odpisów "
                + "amortyzacyjnych — wymaga sprawdzenia przed rozliczeniem.",
                Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y);
        }

        private void WierszSumy(string etykieta, decimal[] kwoty)
        {
            double wysokosc = Styl.Mm(5.5);

            ZapewnijMiejsce(wysokosc);

            var ramka = new XRect(Styl.Lewa, _y, SzerokoscTresci, wysokosc);
            _rysik.DrawRectangle(Styl.TloJasne, ramka);

            var wartosci = new List<string> { string.Empty, string.Empty, string.Empty,
                                              etykieta, string.Empty, string.Empty };

            wartosci.AddRange(kwoty.Select(Styl.Kwota));
            wartosci.Add(string.Empty);

            RysujWiersz([.. wartosci], Styl.TabelaNaglowek, wysokosc);

            _rysik.DrawLine(Styl.Linia, Styl.Lewa, _y, Prawa, _y);
        }

        // ---------------------------------------------------------- ewidencja

        private static readonly double[] KolumnyEwidencji = [12, 26, 40, 90, 30, 40];

        private static readonly string[] NaglowkiEwidencji =
            ["Lp.", "Data", "Nr dowodu", "Opis", "Stawka", "Przychód"];

        public byte[] BudujEwidencje(EwidencjaRyczaltu ewidencja)
        {
            _tytul = "Ewidencja przychodów";
            _kolumny = KolumnyEwidencji;
            _naglowki = NaglowkiEwidencji;

            _dokument.Info.Title = _tytul + " — " + okres.Nazwa;
            _dokument.Info.Creator = "Firma PRO";

            NowaStrona();

            int lp = 1;

            foreach (WpisRyczaltu wpis in ewidencja.Wpisy)
            {
                List<string> opis = Zawin(wpis.Opis, Styl.Tabela,
                    Styl.Mm(_kolumny[3]) - Styl.Mm(2));

                double wysokosc = (Math.Max(1, opis.Count) * Styl.Mm(3.4)) + Styl.Mm(1.6);

                ZapewnijMiejsce(wysokosc);

                RysujWiersz(
                [
                    lp++.ToString(CultureInfo.InvariantCulture),
                    Styl.Data(wpis.Data),
                    wpis.NumerDowodu,
                    string.Empty,
                    StawkiRyczaltu.NaTekst(wpis.Stawka),
                    Styl.Kwota(wpis.Kwota)
                ], Styl.Tabela, wysokosc);

                RysujZawiniete(opis, 3, wysokosc);

                _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Prawa, _y);
            }

            PodatekRyczaltu(ewidencja);
            Stopki();

            return Zapisz();
        }

        /// <summary>
        /// Podatek w podziale na stawki.
        /// </summary>
        /// <remarks>
        /// Osobna tabelka, bo podatek liczy się w każdej stawce oddzielnie -
        /// jedna kwota pod ewidencją sugerowałaby, że policzono go od sumy.
        /// </remarks>
        private void PodatekRyczaltu(EwidencjaRyczaltu ewidencja)
        {
            _y += Styl.Mm(6);

            ZapewnijMiejsce(Styl.Mm(30));

            _rysik.DrawString("Podatek według stawek", Styl.NaglowekSekcji, Styl.Tekst,
                Styl.Lewa, _y);

            _y += Styl.Mm(6);

            _kolumny = [30, 45, 40, 45, 40];
            _naglowki = ["Stawka", "Przychód okresu", "Podatek okresu",
                         "Przychód narastająco", "Podatek narastająco"];

            NaglowekTabeli();

            foreach (PrzychodWStawce stawka in ewidencja.NarastajacoWedlugStawek)
            {
                PrzychodWStawce? okresu = ewidencja.WedlugStawek
                    .FirstOrDefault(s => s.Stawka == stawka.Stawka);

                ZapewnijMiejsce(Styl.Mm(6));

                RysujWiersz(
                [
                    StawkiRyczaltu.NaTekst(stawka.Stawka),
                    Styl.Kwota(okresu?.Przychod ?? 0m),
                    Kwoty.ZloteNaTekst(okresu?.Podatek ?? 0L),
                    Styl.Kwota(stawka.Przychod),
                    Kwoty.ZloteNaTekst(stawka.Podatek)
                ], Styl.Tabela, Styl.Mm(5));

                _rysik.DrawLine(Styl.LiniaJasna, Styl.Lewa, _y, Prawa, _y);
            }

            _y += Styl.Mm(4);

            _rysik.DrawString(
                "Podatek narastająco: "
                + Kwoty.ZloteNaTekst(ewidencja.PodatekNarastajaco) + " zł",
                Styl.Wyrozniona, Styl.Tekst,
                new XRect(Styl.Lewa, _y, SzerokoscTresci, Styl.Mm(5)),
                XStringFormats.TopRight);

            _y += Styl.Mm(6);

            _rysik.DrawString(
                "Podatek policzony od samego przychodu, bez odliczenia składek "
                + "na ubezpieczenie społeczne i zdrowotne.",
                Styl.Mala, Styl.TekstSzary, Styl.Lewa, _y);
        }

        // -------------------------------------------------------- rysowanie

        private void RysujWiersz(string[] wartosci, XFont krój, double wysokosc)
        {
            double x = Styl.Lewa;

            for (int i = 0; i < _kolumny.Length && i < wartosci.Length; i++)
            {
                if (wartosci[i].Length > 0)
                {
                    _rysik.DrawString(wartosci[i], krój, Styl.Tekst,
                        new XRect(x + Styl.Mm(1), _y + Styl.Mm(1),
                                  Styl.Mm(_kolumny[i]) - Styl.Mm(2), Styl.Mm(3.4)),
                        Wyrownanie(i));
                }

                x += Styl.Mm(_kolumny[i]);
            }

            _y += wysokosc;
        }

        private void RysujZawiniete(List<string> linie, int kolumna, double wysokosc)
        {
            double x = Styl.Lewa;

            for (int i = 0; i < kolumna; i++)
            {
                x += Styl.Mm(_kolumny[i]);
            }

            double y = _y - wysokosc + Styl.Mm(1);

            foreach (string linia in linie)
            {
                _rysik.DrawString(linia, Styl.Tabela, Styl.Tekst,
                    x + Styl.Mm(1), y + Styl.Mm(2.6));

                y += Styl.Mm(3.4);
            }
        }

        /// <summary>Kwoty do prawej, reszta do lewej - tak czyta się tabelę liczb.</summary>
        private XStringFormat Wyrownanie(int kolumna) =>
            _kolumny.Length == KolumnyKsiegi.Length && kolumna >= 6 && kolumna <= 14
                ? XStringFormats.TopRight
                : _kolumny.Length == KolumnyEwidencji.Length && kolumna == 5
                    ? XStringFormats.TopRight
                    : _kolumny.Length == 5 && kolumna > 0
                        ? XStringFormats.TopRight
                        : XStringFormats.TopLeft;

        private void NaglowekTabeli()
        {
            double wysokosc = Styl.Mm(8);

            var ramka = new XRect(Styl.Lewa, _y, SzerokoscTresci, wysokosc);
            _rysik.DrawRectangle(Styl.TloJasne, ramka);
            _rysik.DrawRectangle(Styl.LiniaJasna, ramka);

            double x = Styl.Lewa;

            for (int i = 0; i < _kolumny.Length; i++)
            {
                List<string> linie = Zawin(_naglowki[i], Styl.MalaWyrozniona,
                    Styl.Mm(_kolumny[i]) - Styl.Mm(2));

                double y = _y + (linie.Count > 1 ? Styl.Mm(1) : Styl.Mm(2.4));

                foreach (string linia in linie)
                {
                    _rysik.DrawString(linia, Styl.MalaWyrozniona, Styl.TekstSzary,
                        new XRect(x + Styl.Mm(1), y, Styl.Mm(_kolumny[i]) - Styl.Mm(2),
                                  Styl.Mm(3)),
                        XStringFormats.TopLeft);

                    y += Styl.Mm(2.8);
                }

                x += Styl.Mm(_kolumny[i]);
            }

            _y += wysokosc;
        }

        // ------------------------------------------------------------ strony

        private void NowaStrona()
        {
            PdfPage strona = _dokument.AddPage();
            strona.Width = XUnit.FromPoint(Szerokosc);
            strona.Height = XUnit.FromPoint(Wysokosc);

            _rysik = XGraphics.FromPdfPage(strona);
            _strony.Add(_rysik);
            _y = Styl.MarginesGorny;

            Naglowek();
            NaglowekTabeli();
        }

        private void Naglowek()
        {
            _rysik.DrawString(_tytul, Styl.Tytul, Styl.Granat, Styl.Lewa, _y + Styl.Mm(5));

            _rysik.DrawString(nazwaFirmy + ", NIP " + nip, Styl.Zwykla, Styl.Tekst,
                new XRect(Styl.Lewa, _y, SzerokoscTresci, Styl.Mm(5)),
                XStringFormats.TopRight);

            _rysik.DrawString(okres.Nazwa, Styl.Wyrozniona, Styl.Tekst,
                new XRect(Styl.Lewa, _y + Styl.Mm(4.5), SzerokoscTresci, Styl.Mm(5)),
                XStringFormats.TopRight);

            _y += Styl.Mm(11);
        }

        private void ZapewnijMiejsce(double potrzeba)
        {
            if (_y + potrzeba > DolnaGranica)
            {
                NowaStrona();
            }
        }

        private void Stopki()
        {
            for (int numer = 0; numer < _strony.Count; numer++)
            {
                double y = Wysokosc - Styl.MarginesDolny + Styl.Mm(5);

                _strony[numer].DrawString($"Strona {numer + 1} z {_strony.Count}",
                    Styl.Mala, Styl.TekstSzary,
                    new XRect(Styl.Lewa, y, SzerokoscTresci, Styl.Mm(4)),
                    XStringFormats.TopRight);

                _strony[numer].DrawString("Wydrukowano w programie Firma PRO",
                    Styl.Mala, Styl.TekstSzary, Styl.Lewa, y + Styl.Mm(3));
            }
        }

        private byte[] Zapisz()
        {
            using var pamiec = new MemoryStream();
            _dokument.Save(pamiec);

            return pamiec.ToArray();
        }

        private List<string> Zawin(string? tekst, XFont krój, double szerokosc)
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

        public void Dispose()
        {
            foreach (XGraphics rysik in _strony)
            {
                rysik.Dispose();
            }

            _dokument.Dispose();
        }
    }
}
