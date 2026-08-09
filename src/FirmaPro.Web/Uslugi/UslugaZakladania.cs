using FirmaPro.Dane;
using FirmaPro.Dane.Encje;
using FirmaPro.Domena;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Zakłada konto i firmę demonstracyjną przy pierwszym uruchomieniu.
/// </summary>
/// <remarks>
/// Dzięki temu po uruchomieniu programu widać działający system, a nie pusty
/// ekran logowania bez możliwości wejścia. Dane demonstracyjne zakładane są
/// tylko wtedy, gdy baza jest pusta - nigdy nie nadpisują niczego.
/// </remarks>
public sealed class UslugaZakladania(
    FirmaProDbContext baza,
    IPasswordHasher<object> haszowanie,
    ILogger<UslugaZakladania> dziennik)
{
    public const string DemoEmail = "demo@firmapro.pl";
    public const string DemoHaslo = "demo1234";

    public async Task ZalozDaneDemonstracyjneAsync(CancellationToken anulowanie = default)
    {
        if (await baza.Uzytkownicy.AnyAsync(anulowanie))
        {
            return;
        }

        var firma = new Firma
        {
            Nazwa = "Moja Firma sp. z o.o.",
            Nip = "5252248481",
            AdresLinia1 = "ul. Prosta 51",
            AdresLinia2 = "00-838 Warszawa",
            Email = "biuro@example.pl",
            Telefon = "+48221234567",
            RachunekBankowy = "88102010260000120200060290",
            NazwaBanku = "Bank Przykładowy S.A.",
            MiejsceWystawienia = "Warszawa",
            StopkaFaktury = "Dziękujemy za współpracę.",
            Srodowisko = SrodowiskoKsef.Test
        };

        var uzytkownik = new Uzytkownik
        {
            Email = DemoEmail,
            ImieINazwisko = "Konto demonstracyjne",
            HaszHasla = haszowanie.HashPassword(new object(), DemoHaslo)
        };

        baza.Firmy.Add(firma);
        baza.Uzytkownicy.Add(uzytkownik);
        baza.Czlonkostwa.Add(new CzlonkostwoWFirmie
        {
            Firma = firma,
            Uzytkownik = uzytkownik,
            Rola = RolaWFirmie.Wlasciciel
        });

        await baza.SaveChangesAsync(anulowanie);

        // Kontrahenci należą już do firmy, więc zapisujemy ich kontekstem
        // ustawionym na tę firmę - inaczej filtr izolacji odrzuciłby zapis.
        await ZalozKontrahentowAsync(firma.Id, anulowanie);

        // Hasła nie zapisujemy w dzienniku - nawet demonstracyjnego, żeby
        // nie utrwalać nawyku, który przy prawdziwym koncie byłby błędem.
        Dziennik.ZalozonoDaneDemonstracyjne(dziennik, DemoEmail);
    }

    private async Task ZalozKontrahentowAsync(Guid firmaId, CancellationToken anulowanie)
    {
        Kontrahent[] kontrahenci =
        [
            new()
            {
                FirmaId = firmaId,
                Nazwa = "Klient S.A.",
                Nip = "7010001453",
                AdresLinia1 = "ul. Długa 1",
                AdresLinia2 = "80-827 Gdańsk",
                Email = "kontakt@klient.example"
            },
            new()
            {
                FirmaId = firmaId,
                Nazwa = "Auto-Serwis Nowak sp. j.",
                Nip = "1180000001",
                AdresLinia1 = "ul. Puławska 120",
                AdresLinia2 = "02-620 Warszawa"
            },
            new()
            {
                FirmaId = firmaId,
                Nazwa = "Jan Kowalski",
                AdresLinia1 = "ul. Kwiatowa 5",
                AdresLinia2 = "00-001 Warszawa"
            }
        ];

        baza.Kontrahenci.AddRange(kontrahenci);
        await baza.SaveChangesAsync(anulowanie);
    }
}
