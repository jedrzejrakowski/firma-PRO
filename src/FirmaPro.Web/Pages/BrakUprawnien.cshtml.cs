using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FirmaPro.Web.Pages;

/// <summary>
/// Strona pokazywana, gdy rola nie pozwala na daną czynność.
/// </summary>
/// <remarks>
/// Osobna strona zamiast gołego kodu 403: użytkownik ma zrozumieć, że to
/// kwestia uprawnień, a nie awarii programu.
/// </remarks>
public sealed class BrakUprawnienModel : PageModel;
