using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Ksef;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Faktura z KSeF wraz z informacją, czy jest już w rejestrze.</summary>
public sealed record ZnalezionaFaktura(FakturaZakupowa Dane, bool JuzWRejestrze);

/// <summary>Wynik importu wybranych faktur.</summary>
public sealed record WynikImportu(int Zaimportowano, int Pominieto, string? Blad)
{
    public bool Udalo => Blad is null;
}

/// <summary>Decyzja użytkownika co do jednej importowanej faktury.</summary>
public sealed record DecyzjaImportu(
    string NumerKsef,
    RodzajZakupu Rodzaj,
    bool Odliczany);

/// <summary>
/// Pobieranie faktur zakupu z KSeF do rejestru VAT.
/// </summary>
/// <remarks>
/// <para>
/// Import nie jest automatyczny i nie może być. KSeF wie, ile faktura
/// kosztowała, ale nie wie dwóch rzeczy, które decydują o podatku: czy zakup
/// jest środkiem trwałym i czy w ogóle przysługuje od niego odliczenie.
/// To rozstrzygnięcia nabywcy, więc program pokazuje listę i pyta.
/// </para>
/// <para>
/// Metadane z KSeF niosą kwoty netto i VAT łącznie, bez rozbicia na stawki.
/// Rejestrowi i deklaracji to wystarcza - zakupy wykazuje się w nich sumami,
/// a nie w podziale na stawki, w odróżnieniu od sprzedaży.
/// </para>
/// </remarks>
public sealed class UslugaImportuZakupow(
    FirmaProDbContext baza,
    IFabrykaKlientowKsef fabrykaKlientow,
    IOchronaTokena ochronaTokena)
{
    /// <summary>
    /// Pobiera z KSeF faktury zakupowe za okres.
    /// </summary>
    /// <remarks>
    /// Faktury już wpisane do rejestru zostają na liście, ale oznaczone -
    /// ukrycie ich kazałoby użytkownikowi zgadywać, czy czegoś nie brakuje.
    /// </remarks>
    public async Task<IReadOnlyList<ZnalezionaFaktura>> SzukajAsync(
        DateOnly dataOd, DateOnly dataDo, CancellationToken anulowanie = default)
    {
        (IKlientKsef klient, Firma firma) = await PrzygotujAsync(anulowanie);

        try
        {
            await UwierzytelnienieKsef.ZalogujAsync(klient, firma, ochronaTokena, anulowanie);

            IReadOnlyList<FakturaZakupowa> znalezione =
                await klient.PobierzFakturyZakupoweAsync(dataOd, dataDo, anulowanie);

            List<string> numery = znalezione.Select(f => f.NumerKsef).ToList();

            HashSet<string> juzMamy = (await baza.FakturyZakupu
                    .Where(f => f.NumerKsef != null && numery.Contains(f.NumerKsef))
                    .Select(f => f.NumerKsef!)
                    .ToListAsync(anulowanie))
                .ToHashSet(StringComparer.Ordinal);

            return znalezione
                .OrderByDescending(f => f.DataWystawienia)
                .Select(f => new ZnalezionaFaktura(f, juzMamy.Contains(f.NumerKsef)))
                .ToList();
        }
        finally
        {
            await ZamknijCicho(klient, anulowanie);
        }
    }

    /// <summary>Wpisuje wybrane faktury do rejestru zakupów.</summary>
    public async Task<WynikImportu> ImportujAsync(
        DateOnly dataOd,
        DateOnly dataDo,
        IReadOnlyList<DecyzjaImportu> decyzje,
        CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(decyzje);

        if (decyzje.Count == 0)
        {
            return new WynikImportu(0, 0, null);
        }

        Firma firma = await WczytajFirmeAsync(anulowanie);

        IReadOnlyList<ZnalezionaFaktura> znalezione =
            await SzukajAsync(dataOd, dataDo, anulowanie);

        Dictionary<string, FakturaZakupowa> wedlugNumeru = znalezione
            .ToDictionary(f => f.Dane.NumerKsef, f => f.Dane, StringComparer.Ordinal);

        int zaimportowano = 0;
        int pominieto = 0;

        var doPobrania = new List<FakturaZakupu>();

        foreach (DecyzjaImportu decyzja in decyzje)
        {
            if (!wedlugNumeru.TryGetValue(decyzja.NumerKsef, out FakturaZakupowa? dane))
            {
                pominieto++;
                continue;
            }

            bool jest = await baza.FakturyZakupu
                .AnyAsync(f => f.NumerKsef == decyzja.NumerKsef, anulowanie);

            if (jest)
            {
                pominieto++;
                continue;
            }

            FakturaZakupu encja = NaEncje(dane, decyzja, firma.TypOkresuVat);

            baza.FakturyZakupu.Add(encja);
            doPobrania.Add(encja);
            zaimportowano++;
        }

        await PobierzPlikiAsync(firma, doPobrania, anulowanie);

        await baza.SaveChangesAsync(anulowanie);

        return new WynikImportu(zaimportowano, pominieto, null);
    }

    /// <summary>
    /// Dociąga z KSeF pliki XML zaimportowanych faktur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Metadane niosą same sumy - dość do rejestru VAT, za mało, żeby zobaczyć,
    /// za co zapłacono. Plik pozwala wydrukować wizualizację faktury dostawcy,
    /// a przy sporze jest jedynym dowodem jej treści, jaki mamy u siebie.
    /// </para>
    /// <para>
    /// Nieudane pobranie nie zatrzymuje importu, a pojedyncza faktura nie do
    /// pobrania nie przekreśla pozostałych. Dokumenty mają trafić do rejestru
    /// nawet wtedy, gdy KSeF akurat nie oddaje plików: podatek rozlicza się
    /// w terminie, a wizualizację da się dociągnąć później.
    /// </para>
    /// <para>
    /// Wszystkie pliki bierzemy w jednej sesji. Logowanie do KSeF jest drogie -
    /// osobna sesja na każdą fakturę przy imporcie całego miesiąca oznaczałaby
    /// kilkadziesiąt niepotrzebnych uwierzytelnień.
    /// </para>
    /// </remarks>
    private async Task PobierzPlikiAsync(Firma firma, List<FakturaZakupu> faktury,
                                         CancellationToken anulowanie)
    {
        if (faktury.Count == 0)
        {
            return;
        }

        IKlientKsef klient = fabrykaKlientow.Utworz(firma.Srodowisko);

        try
        {
            await UwierzytelnienieKsef.ZalogujAsync(klient, firma, ochronaTokena, anulowanie);

            foreach (FakturaZakupu faktura in faktury)
            {
                if (string.IsNullOrWhiteSpace(faktura.NumerKsef))
                {
                    continue;
                }

                try
                {
                    faktura.XmlKsef =
                        await klient.PobierzXmlFakturyAsync(faktura.NumerKsef!, anulowanie);
                }
                catch (BladKsefException)
                {
                    // Zostaje sam wpis w rejestrze - wizualizację da się
                    // dociągnąć przy następnym imporcie.
                }
            }
        }
        catch (BladKsefException)
        {
            // Nie udało się nawet zalogować - import i tak ma się dokończyć.
        }
        finally
        {
            await ZamknijCicho(klient, anulowanie);
        }
    }

    // ------------------------------------------------------------ pomocnicze

    private static FakturaZakupu NaEncje(FakturaZakupowa dane, DecyzjaImportu decyzja,
                                         TypOkresu typOkresu)
    {
        DateOnly wystawienia = dane.DataWystawienia
            ?? DateOnly.FromDateTime(dane.DataPrzyjecia?.UtcDateTime ?? DateTime.UtcNow);

        // Za datę wpływu przyjmujemy dzień przyjęcia faktury przez KSeF.
        // Od tej chwili nabywca ma do niej dostęp, więc to ona wyznacza
        // najwcześniejszy możliwy okres odliczenia (art. 86 ust. 10b pkt 1).
        DateOnly wplywu = dane.DataPrzyjecia is DateTimeOffset przyjecie
            ? DateOnly.FromDateTime(przyjecie.UtcDateTime)
            : wystawienia;

        OkresRozliczeniowy okres = TerminyVat.NajwczesniejszyOkresOdliczenia(
            wystawienia, wplywu, typOkresu);

        return new FakturaZakupu
        {
            Numer = dane.Numer,
            DataWystawienia = wystawienia,
            DataWplywu = wplywu,
            DataObowiazkuPodatkowego = wystawienia,
            DataUjecia = okres.PierwszyDzien,
            SprzedawcaNazwa = string.IsNullOrWhiteSpace(dane.SprzedawcaNazwa)
                ? "(nazwa nieznana)"
                : dane.SprzedawcaNazwa,
            SprzedawcaNip = string.IsNullOrWhiteSpace(dane.SprzedawcaNip)
                ? null
                : dane.SprzedawcaNip,
            Waluta = dane.Waluta,
            Rodzaj = decyzja.Rodzaj,
            Odliczany = decyzja.Odliczany,
            RazemNetto = Kwoty.Zaokraglij(dane.Netto),
            RazemVat = Kwoty.Zaokraglij(dane.Vat),
            RazemBrutto = Kwoty.Zaokraglij(dane.Brutto),
            NumerKsef = dane.NumerKsef,
            Uwagi = "Pobrano z KSeF"
        };
    }

    private Task<Firma> WczytajFirmeAsync(CancellationToken anulowanie) =>
        baza.Firmy.SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

    private async Task<(IKlientKsef Klient, Firma Firma)> PrzygotujAsync(
        CancellationToken anulowanie)
    {
        Firma firma = await WczytajFirmeAsync(anulowanie);

        return (fabrykaKlientow.Utworz(firma.Srodowisko), firma);
    }

    private static async Task ZamknijCicho(IKlientKsef klient, CancellationToken anulowanie)
    {
        try
        {
            await klient.ZamknijSesjeAsync(anulowanie);
        }
        catch (BladKsefException)
        {
            // Zamknięcie sesji to sprzątanie - błąd na tym etapie nie może
            // przesłonić właściwego wyniku.
        }
    }
}
