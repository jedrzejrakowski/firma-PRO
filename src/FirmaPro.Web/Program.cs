using System.Globalization;
using FirmaPro.Dane;
using FirmaPro.Ksef;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Formularze przeglądarki wysyłają liczby z kropką dziesiętną - tego wymaga
// pole <input type="number"> niezależnie od języka użytkownika. Gdyby serwer
// pracował w polskiej kulturze, "145.50" nie dałoby się odczytać jako liczby
// i cena po cichu wracałaby jako błąd walidacji. Kultura jest więc ustalona
// na sztywno, zamiast zależeć od ustawień maszyny.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

WebApplicationBuilder budowniczy = WebApplication.CreateBuilder(args);

// --- baza danych ------------------------------------------------------------

string polaczenie = budowniczy.Configuration.GetConnectionString("Baza")
    ?? Environment.GetEnvironmentVariable("FIRMAPRO_DB")
    ?? "Host=localhost;Port=5432;Database=firmapro;Username=postgres;Password=postgres";

budowniczy.Services.AddDbContext<FirmaProDbContext>(opcje => opcje.UseNpgsql(polaczenie));

// Kontekst firmy odczytywany jest z ciasteczka logowania przy każdym żądaniu.
budowniczy.Services.AddHttpContextAccessor();
budowniczy.Services.AddScoped<IKontekstFirmy, KontekstFirmyZZadania>();

// --- uwierzytelnianie -------------------------------------------------------

bool trybDeweloperski = budowniczy.Environment.IsDevelopment();

budowniczy.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opcje =>
    {
        opcje.LoginPath = "/Logowanie";
        opcje.LogoutPath = "/Wyloguj";
        opcje.AccessDeniedPath = "/Logowanie";
        opcje.ExpireTimeSpan = TimeSpan.FromHours(8);
        opcje.SlidingExpiration = true;
        opcje.Cookie.HttpOnly = true;
        opcje.Cookie.SameSite = SameSiteMode.Lax;

        // Poza pracą nad programem ciasteczko logowania wychodzi wyłącznie
        // po HTTPS. W trybie deweloperskim serwer stoi na zwykłym HTTP,
        // więc ten sam warunek uniemożliwiłby zalogowanie się.
        opcje.Cookie.SecurePolicy = trybDeweloperski
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

budowniczy.Services.AddAuthorization();

// --- ochrona danych ---------------------------------------------------------

// Kluczami ochrony zaszyfrowany jest token KSeF i podpisane są ciasteczka
// logowania. Gdy powstają od nowa przy każdym starcie, po wymianie kontenera
// token przestaje się odczytywać - a program zgłasza wtedy zwyczajny „brak
// tokena", więc przyczyny nikt nie skojarzy ze skutkiem.
IDataProtectionBuilder ochronaDanych = budowniczy.Services
    .AddDataProtection()
    .SetApplicationName("FirmaPro");

if (UstawieniaStartu.KatalogKluczy(budowniczy.Configuration, trybDeweloperski)
    is string katalogKluczy)
{
    Directory.CreateDirectory(katalogKluczy);
    ochronaDanych.PersistKeysToFileSystem(new DirectoryInfo(katalogKluczy));
}

// --- praca za odwrotnym pośrednikiem ----------------------------------------

// HTTPS podaje pośrednik (Caddy), a do programu żądanie dociera po zwykłym
// HTTP wewnątrz sieci kontenerów. Bez tych nagłówków program uznałby
// połączenie za nieszyfrowane i nie wysłał ciasteczka logowania.
//
// Lista zaufanych pośredników jest wyczyszczona, bo adres kontenera nie jest
// z góry znany. Jest to bezpieczne tylko dlatego, że port programu nie jest
// wystawiony na zewnątrz - dociera do niego wyłącznie pośrednik. Nigdy nie
// publikuj portu aplikacji wprost.
budowniczy.Services.Configure<ForwardedHeadersOptions>(opcje =>
{
    opcje.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opcje.KnownIPNetworks.Clear();
    opcje.KnownProxies.Clear();
});

// --- usługi aplikacji -------------------------------------------------------

budowniczy.Services.AddSingleton<IPasswordHasher<object>, PasswordHasher<object>>();
budowniczy.Services.AddSingleton<IOchronaTokena, OchronaTokena>();
budowniczy.Services.AddScoped<UslugaNumeracji>();
budowniczy.Services.AddScoped<UslugaFaktur>();
budowniczy.Services.AddScoped<UslugaZakladania>();
budowniczy.Services.AddScoped<UslugaZakupow>();
budowniczy.Services.AddScoped<UslugaImportuZakupow>();
budowniczy.Services.AddScoped<UslugaRejestruVat>();
budowniczy.Services.AddScoped<UslugaDeklaracji>();

// Klient KSeF korzysta z puli połączeń, żeby nie wyczerpywać gniazd
// sieciowych. Adres bazowy ustawia fabryka - zależy od środowiska firmy.
budowniczy.Services.AddHttpClient(FabrykaKlientowKsef.NazwaKlientaHttp,
    http => http.Timeout = TimeSpan.FromSeconds(60));

budowniczy.Services.AddSingleton(TimeProvider.System);
budowniczy.Services.AddScoped<IFabrykaKlientowKsef, FabrykaKlientowKsef>();

budowniczy.Services.AddRazorPages(opcje =>
{
    // Domyślnie wszystko wymaga zalogowania; wyjątki wskazane są jawnie
    // na poszczególnych stronach.
    opcje.Conventions.AuthorizeFolder("/");
    opcje.Conventions.AllowAnonymousToPage("/Logowanie");
    opcje.Conventions.AllowAnonymousToPage("/Index");
});

WebApplication aplikacja = budowniczy.Build();

// --- przygotowanie bazy -----------------------------------------------------

using (IServiceScope zakres = aplikacja.Services.CreateScope())
{
    var baza = zakres.ServiceProvider.GetRequiredService<FirmaProDbContext>();
    await baza.Database.MigrateAsync();

    var zakladanie = zakres.ServiceProvider.GetRequiredService<UslugaZakladania>();
    ILogger dziennikStartu = zakres.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("FirmaPro.Start");

    // Konto podane w ustawieniach wdrożenia ma pierwszeństwo: świeża
    // instalacja bez żadnego konta byłaby zamknięta na głucho.
    if (UstawieniaStartu.PierwszeKontoZUstawien(aplikacja.Configuration) is PierwszeKonto konto)
    {
        await zakladanie.ZalozPierwszeKontoAsync(konto);
    }

    if (UstawieniaStartu.CzyZakladacDaneDemonstracyjne(aplikacja.Configuration, trybDeweloperski))
    {
        if (!trybDeweloperski)
        {
            Dziennik.DaneDemonstracyjnePozaDeweloperskim(dziennikStartu, UslugaZakladania.DemoEmail);
        }

        await zakladanie.ZalozDaneDemonstracyjneAsync();
    }
}

aplikacja.UseForwardedHeaders();

if (!trybDeweloperski)
{
    aplikacja.UseExceptionHandler("/Blad");
    aplikacja.UseHsts();
}

aplikacja.UseStaticFiles();
aplikacja.UseRouting();
aplikacja.UseAuthentication();
aplikacja.UseAuthorization();
aplikacja.MapRazorPages();

// Stan programu dla nadzoru nad kontenerem. Sprawdzamy połączenie z bazą,
// bo program bez bazy odpowiada na żądania, ale nie umie nic zrobić.
aplikacja.MapGet("/zdrowie", async (FirmaProDbContext baza, CancellationToken anulowanie) =>
        await baza.Database.CanConnectAsync(anulowanie)
            ? Results.Text("sprawny")
            : Results.Text("brak połączenia z bazą", statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

await aplikacja.RunAsync();

/// <summary>Punkt wejścia - udostępniony testom integracyjnym.</summary>
public partial class Program;
