using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace FirmaPro.Web.Uslugi;

/// <summary>
/// Szyfrowanie tokena KSeF przed zapisaniem go w bazie.
/// </summary>
/// <remarks>
/// <para>
/// Token KSeF pozwala wystawiać faktury w imieniu firmy - to najbardziej
/// wrażliwa dana w całym systemie. Nie może leżeć w bazie otwartym tekstem,
/// bo wyciek kopii bazy oznaczałby możliwość wystawiania faktur w imieniu
/// wszystkich klientów naraz.
/// </para>
/// <para>
/// Na razie używamy wbudowanego mechanizmu ochrony danych ASP.NET Core.
/// Docelowo, przy wdrożeniu produkcyjnym, klucze powinny trafić do
/// zewnętrznego magazynu sekretów - wtedy zmienia się tylko implementacja
/// tego interfejsu.
/// </para>
/// </remarks>
public interface IOchronaTokena
{
    /// <summary>Szyfruje token przed zapisem.</summary>
    byte[] Zaszyfruj(string token);

    /// <summary>Odszyfrowuje token odczytany z bazy.</summary>
    string? Odszyfruj(byte[] zaszyfrowany);
}

/// <inheritdoc />
public sealed class OchronaTokena : IOchronaTokena
{
    // Cel ochrony wiąże zaszyfrowane dane z ich przeznaczeniem - szyfrogram
    // z jednego celu nie da się odczytać w innym.
    private const string CelOchrony = "FirmaPro.TokenKsef.v1";

    private readonly IDataProtector _ochrona;

    public OchronaTokena(IDataProtectionProvider dostawca)
    {
        ArgumentNullException.ThrowIfNull(dostawca);
        _ochrona = dostawca.CreateProtector(CelOchrony);
    }

    public byte[] Zaszyfruj(string token) =>
        _ochrona.Protect(Encoding.UTF8.GetBytes(token));

    public string? Odszyfruj(byte[] zaszyfrowany)
    {
        if (zaszyfrowany is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(_ochrona.Unprotect(zaszyfrowany));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Zmiana kluczy ochrony albo uszkodzony zapis - traktujemy tak,
            // jakby tokena nie było, zamiast wywracać całą stronę.
            return null;
        }
    }
}
