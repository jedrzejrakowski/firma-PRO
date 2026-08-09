using FirmaPro.Domena;
using System.Net.Http;

namespace FirmaPro.Ksef;

/// <summary>
/// Tworzy klienta KSeF dla wskazanego środowiska.
/// </summary>
/// <remarks>
/// Każda firma może pracować na innym środowisku - jedna dopiero testuje
/// integrację, druga wystawia już faktury produkcyjne. Adres API zależy od
/// tego wyboru, więc klient nie może być rejestrowany raz na całą aplikację;
/// powstaje na żądanie, dla konkretnej firmy.
/// </remarks>
public interface IFabrykaKlientowKsef
{
    /// <summary>Tworzy klienta rozmawiającego ze wskazanym środowiskiem.</summary>
    IKlientKsef Utworz(SrodowiskoKsef srodowisko);
}

/// <inheritdoc />
public sealed class FabrykaKlientowKsef(IHttpClientFactory fabrykaHttp,
                                        TimeProvider? czas = null) : IFabrykaKlientowKsef
{
    /// <summary>Nazwa klienta HTTP zarejestrowanego w kontenerze.</summary>
    public const string NazwaKlientaHttp = "ksef";

    public IKlientKsef Utworz(SrodowiskoKsef srodowisko)
    {
        HttpClient http = fabrykaHttp.CreateClient(NazwaKlientaHttp);
        http.BaseAddress = new Uri(AdresyKsef.Api(srodowisko) + "/");

        return new KlientKsef(http, srodowisko, czas);
    }
}
