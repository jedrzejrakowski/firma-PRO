namespace FirmaPro.Domena;

/// <summary>Pozycja zamówienia, na poczet którego wpłacono zaliczkę.</summary>
/// <remarks>
/// Odpowiada wierszowi <c>ZamowienieWiersz</c> schematu FA(3) - to on opisuje
/// całe zamówienie, podczas gdy wiersze samej faktury pokazują wyłącznie
/// otrzymaną zaliczkę.
/// </remarks>
public sealed class PozycjaZamowienia
{
    public string Nazwa { get; set; } = string.Empty;
    public string Jednostka { get; set; } = "szt.";
    public decimal Ilosc { get; set; } = 1m;
    public decimal CenaNetto { get; set; }
    public StawkaVat Stawka { get; set; } = StawkaVat.Vat23;
    public string? Gtu { get; set; }

    public decimal WartoscNetto => Kwoty.Zaokraglij(Ilosc * CenaNetto);
    public decimal KwotaVat => Stawka.PodatekOd(WartoscNetto);
    public decimal WartoscBrutto => WartoscNetto + KwotaVat;
}

/// <summary>Zaliczka przypadająca na jedną stawkę podatku.</summary>
public sealed record CzescZaliczki(StawkaVat Stawka, decimal Netto, decimal Vat)
{
    public decimal Brutto => Netto + Vat;
}

/// <summary>
/// Rozbicie otrzymanej zaliczki na stawki podatku.
/// </summary>
/// <remarks>
/// <para>
/// Faktura zaliczkowa nie dokumentuje towaru, tylko pieniądze, które wpłynęły
/// przed dostawą. Kwotę trzeba jednak wykazać w rozbiciu na stawki - bo od
/// tego zależy podatek. Dzieli się ją proporcjonalnie do wartości brutto
/// pozycji zamówienia, a podatek liczy metodą „w stu": zaliczka jest kwotą
/// brutto, a nie netto.
/// </para>
/// <para>
/// Grosze z zaokrągleń nie mogą przepaść ani się namnożyć: suma rozbicia musi
/// co do grosza równać się wpłaconej kwocie, inaczej faktura nie zgadzałaby
/// się z przelewem, a KSeF odrzuciłby dokument.
/// </para>
/// </remarks>
public static class Zaliczka
{
    /// <summary>Dzieli zaliczkę brutto na części przypadające na stawki.</summary>
    public static IReadOnlyList<CzescZaliczki> Rozbij(
        IReadOnlyList<PozycjaZamowienia> zamowienie, decimal zaliczkaBrutto)
    {
        ArgumentNullException.ThrowIfNull(zamowienie);

        if (zaliczkaBrutto <= 0 || zamowienie.Count == 0)
        {
            return [];
        }

        // Pozycje w tej samej stawce składają się na jedną kwotę - faktura
        // pokazuje zaliczkę w podziale na stawki, a nie na towary.
        List<(StawkaVat Stawka, decimal Brutto)> wedlugStawek = [.. zamowienie
            .GroupBy(p => p.Stawka)
            .Select(g => (Stawka: g.Key, Brutto: g.Sum(p => p.WartoscBrutto)))
            .Where(g => g.Brutto > 0)
            .OrderBy(g => g.Stawka.Kolejnosc)];

        decimal calosc = wedlugStawek.Sum(g => g.Brutto);

        if (calosc <= 0)
        {
            return [];
        }

        decimal zaliczka = Kwoty.Zaokraglij(zaliczkaBrutto);

        // Najpierw udziały brutto, każdy zaokrąglony do grosza...
        List<decimal> udzialy = [.. wedlugStawek.Select(
            g => Kwoty.Zaokraglij(zaliczka * g.Brutto / calosc))];

        // ...a potem różnica z zaokrągleń dopisana do największego udziału.
        // Tam jest najmniej widoczna, a suma zgadza się co do grosza.
        decimal roznica = zaliczka - udzialy.Sum();

        if (roznica != 0)
        {
            int najwiekszy = udzialy.IndexOf(udzialy.Max());
            udzialy[najwiekszy] += roznica;
        }

        List<CzescZaliczki> czesci = [];

        for (int i = 0; i < wedlugStawek.Count; i++)
        {
            StawkaVat stawka = wedlugStawek[i].Stawka;
            decimal brutto = udzialy[i];

            if (brutto == 0)
            {
                continue;
            }

            // Metoda „w stu": z kwoty brutto wyliczamy netto, a podatek jest
            // resztą. Dzięki temu netto i VAT zawsze sumują się do wpłaty.
            decimal netto = stawka.NaliczaPodatek
                ? Kwoty.Zaokraglij(brutto / (1 + (stawka.Procent!.Value / 100m)))
                : brutto;

            czesci.Add(new CzescZaliczki(stawka, netto, brutto - netto));
        }

        return czesci;
    }

    /// <summary>Wartość całego zamówienia z podatkiem (pole WartoscZamowienia).</summary>
    public static decimal WartoscZamowienia(IReadOnlyList<PozycjaZamowienia> zamowienie)
    {
        ArgumentNullException.ThrowIfNull(zamowienie);
        return Kwoty.Zaokraglij(zamowienie.Sum(p => p.WartoscBrutto));
    }
}
