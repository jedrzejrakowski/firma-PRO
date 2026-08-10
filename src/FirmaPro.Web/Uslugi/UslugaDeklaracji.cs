using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using FirmaPro.Jpk;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>Deklaracja za okres wraz z danymi potrzebnymi do jej pokazania.</summary>
public sealed record PodgladDeklaracji(
    DeklaracjaVat Deklaracja,
    RejestrVat Rejestr,
    bool OkresZamkniety,
    string? BrakujaceUstawienia);

/// <summary>
/// Składanie deklaracji JPK_V7 z rejestru VAT.
/// </summary>
/// <remarks>
/// Deklaracja powstaje na żądanie z tych samych dokumentów co rejestr.
/// Zapamiętywana jest wyłącznie nadwyżka przechodząca na następny okres -
/// bez tego poprawka w starej fakturze zmieniałaby wstecz deklaracje już
/// złożone w urzędzie.
/// </remarks>
public sealed class UslugaDeklaracji(
    FirmaProDbContext baza,
    UslugaRejestruVat uslugaRejestru)
{
    public async Task<PodgladDeklaracji> ZbudujAsync(OkresRozliczeniowy okres,
                                                     CancellationToken anulowanie = default)
    {
        ArgumentNullException.ThrowIfNull(okres);

        Firma firma = await WczytajFirmeAsync(anulowanie);
        RejestrVat rejestr = await uslugaRejestru.ZbudujAsync(okres, anulowanie);

        long nadwyzka = await NadwyzkaZPoprzedniegoOkresuAsync(okres, anulowanie);
        DeklaracjaVat deklaracja = DeklaracjaVat.Zbuduj(rejestr, nadwyzka);

        ZamkniecieOkresuVat? zamkniecie = await ZnajdzZamkniecieAsync(okres, anulowanie);

        return new PodgladDeklaracji(deklaracja, rejestr, zamkniecie is not null,
            BrakujaceUstawienia(firma));
    }

    /// <summary>Buduje plik JPK_V7M do pobrania.</summary>
    public async Task<byte[]> ZbudujPlikAsync(OkresRozliczeniowy okres,
                                              CelZlozenia cel,
                                              CancellationToken anulowanie = default)
    {
        Firma firma = await WczytajFirmeAsync(anulowanie);

        if (BrakujaceUstawienia(firma) is string brak)
        {
            throw new InvalidOperationException(brak);
        }

        PodgladDeklaracji podglad = await ZbudujAsync(okres, anulowanie);

        var dane = new DanePliku
        {
            Nip = firma.Nip,
            Nazwa = firma.Nazwa,
            KodUrzedu = firma.KodUrzeduSkarbowego!,
            Email = firma.Email,
            Telefon = firma.Telefon,
            Cel = cel
        };

        return GeneratorJpk.ZbudujPlik(podglad.Deklaracja, podglad.Rejestr, dane);
    }

    /// <summary>
    /// Zamyka okres, utrwalając kwotę przechodzącą na następny.
    /// </summary>
    /// <remarks>
    /// Od tej chwili późniejsze poprawki w dokumentach tego okresu nie zmienią
    /// już punktu wyjścia następnej deklaracji. Rozjazd z rejestrem jest tu
    /// zamierzony: deklaracja złożona w urzędzie ma zostać taka, jaka była.
    /// </remarks>
    public async Task ZamknijAsync(OkresRozliczeniowy okres,
                                   CancellationToken anulowanie = default)
    {
        PodgladDeklaracji podglad = await ZbudujAsync(okres, anulowanie);
        ZamkniecieOkresuVat? istniejace = await ZnajdzZamkniecieAsync(okres, anulowanie);

        if (istniejace is not null)
        {
            istniejace.NadwyzkaDoPrzeniesienia = podglad.Deklaracja.DoPrzeniesienia;
            istniejace.PodatekDoWplaty = podglad.Deklaracja.DoWplaty;
            istniejace.DataZamkniecia = DateTimeOffset.UtcNow;
        }
        else
        {
            baza.ZamknieciaOkresow.Add(new ZamkniecieOkresuVat
            {
                Rok = okres.Rok,
                Numer = okres.Numer,
                Typ = okres.Typ,
                NadwyzkaDoPrzeniesienia = podglad.Deklaracja.DoPrzeniesienia,
                PodatekDoWplaty = podglad.Deklaracja.DoWplaty,
                DataZamkniecia = DateTimeOffset.UtcNow
            });
        }

        await baza.SaveChangesAsync(anulowanie);
    }

    /// <summary>Otwiera okres z powrotem - do poprawienia i złożenia korekty.</summary>
    public async Task OtworzAsync(OkresRozliczeniowy okres,
                                  CancellationToken anulowanie = default)
    {
        ZamkniecieOkresuVat? zamkniecie = await ZnajdzZamkniecieAsync(okres, anulowanie);
        if (zamkniecie is null)
        {
            return;
        }

        baza.ZamknieciaOkresow.Remove(zamkniecie);
        await baza.SaveChangesAsync(anulowanie);
    }

    // ------------------------------------------------------------ pomocnicze

    private Task<Firma> WczytajFirmeAsync(CancellationToken anulowanie) =>
        baza.Firmy.SingleAsync(f => f.Id == baza.AktualnaFirmaId, anulowanie);

    /// <summary>
    /// Czego brakuje w ustawieniach, żeby dało się złożyć deklarację.
    /// </summary>
    /// <returns><c>null</c>, gdy wszystko jest uzupełnione.</returns>
    private static string? BrakujaceUstawienia(Firma firma) =>
        string.IsNullOrWhiteSpace(firma.KodUrzeduSkarbowego)
            ? "Nie ustawiono kodu urzędu skarbowego - uzupełnij go w Ustawieniach firmy."
            : null;

    private Task<ZamkniecieOkresuVat?> ZnajdzZamkniecieAsync(
        OkresRozliczeniowy okres, CancellationToken anulowanie) =>
        baza.ZamknieciaOkresow.FirstOrDefaultAsync(
            z => z.Rok == okres.Rok && z.Numer == okres.Numer && z.Typ == okres.Typ,
            anulowanie);

    private async Task<long> NadwyzkaZPoprzedniegoOkresuAsync(
        OkresRozliczeniowy okres, CancellationToken anulowanie)
    {
        OkresRozliczeniowy poprzedni = okres.Poprzedni;

        ZamkniecieOkresuVat? zamkniecie =
            await ZnajdzZamkniecieAsync(poprzedni, anulowanie);

        // Brak zamknięcia oznacza, że poprzedni okres nie został jeszcze
        // rozliczony. Zgadywanie kwoty na podstawie samych dokumentów byłoby
        // gorsze niż zero: podpowiadałoby liczbę, której nikt nie zatwierdził.
        return zamkniecie?.NadwyzkaDoPrzeniesienia ?? 0;
    }
}
