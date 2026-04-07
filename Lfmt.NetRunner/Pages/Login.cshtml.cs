using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;

namespace Lfmt.NetRunner.Pages;

public class LoginModel : PageModel
{
    private readonly NetRunnerConfig _config;

    public string? Error { get; set; }

    public LoginModel(NetRunnerConfig config)
    {
        _config = config;
    }

    public IActionResult OnGet()
    {
        if (!_config.AuthEnabled)
            return Redirect("/");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string username, string password)
    {
        if (!_config.AuthEnabled)
            return Redirect("/");

        if (username != _config.AuthUser || password != _config.AuthPassword)
        {
            Error = "Invalid credentials";
            return Page();
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Redirect("/");
    }
}
