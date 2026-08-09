using System.Globalization;
using FirmaPro.Dane;
using FirmaPro.Ksef;
using FirmaPro.Web.Uslugi;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    });

budowniczy.Services.AddAuthorization();
budowniczy.Services.AddDataProtection();

// --- usługi aplikacji -------------------------------------------------------

budowniczy.Services.AddSingleton<IPasswordHasher<object>, PasswordHasher<object>>();
budowniczy.Services.AddSingleton<IOchronaTokena, OchronaTokena>();
budowniczy.Services.AddScoped<UslugaNumeracji>();
budowniczy.Services.AddScoped<UslugaFaktur>();
budowniczy.Services.AddScoped<UslugaZakladania>();
budowniczy.Services.AddScoped<UslugaZakupow>();
budowniczy.Services.AddScoped<UslugaRejestruVat>();

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

    // Przy pierwszym uruchomieniu zakładamy konto i firmę demonstracyjną,
    // żeby dało się od razu zobaczyć działający program.
    var zakladanie = zakres.ServiceProvider.GetRequiredService<UslugaZakladania>();
    await zakladanie.ZalozDaneDemonstracyjneAsync();
}

if (!aplikacja.Environment.IsDevelopment())
{
    aplikacja.UseExceptionHandler("/Blad");
    aplikacja.UseHsts();
}

aplikacja.UseStaticFiles();
aplikacja.UseRouting();
aplikacja.UseAuthentication();
aplikacja.UseAuthorization();
aplikacja.MapRazorPages();

await aplikacja.RunAsync();

/// <summary>Punkt wejścia - udostępniony testom integracyjnym.</summary>
public partial class Program;
