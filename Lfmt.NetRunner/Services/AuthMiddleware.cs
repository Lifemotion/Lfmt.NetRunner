using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Lfmt.NetRunner.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Lfmt.NetRunner.Services;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly NetRunnerConfig _config;

    public AuthMiddleware(RequestDelegate next, NetRunnerConfig config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_config.AuthEnabled)
        {
            await _next(context);
            return;
        }

        // Allow login page and static files without auth
        var path = context.Request.Path.Value ?? "";
        if (path == "/Login" || path.StartsWith("/lib/") || path.StartsWith("/css/") ||
            path.StartsWith("/js/") || path.EndsWith(".ico"))
        {
            await _next(context);
            return;
        }

        // Webhook endpoint uses its own HMAC auth
        if (path.StartsWith("/api/webhook/"))
        {
            await _next(context);
            return;
        }

        // Check cookie auth (for UI)
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        // Check Basic auth (for API)
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            if (TryBasicAuth(authHeader.ToString()))
            {
                await _next(context);
                return;
            }
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Invalid credentials\"}");
            return;
        }

        // API requests without auth → 401
        if (path.StartsWith("/api/"))
        {
            context.Response.StatusCode = 401;
            context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"NetRunner\"";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Authentication required\"}");
            return;
        }

        // UI requests without auth → redirect to login
        context.Response.Redirect("/Login");
    }

    private bool TryBasicAuth(string header)
    {
        if (!AuthenticationHeaderValue.TryParse(header, out var parsed))
            return false;
        if (parsed.Scheme != "Basic" || string.IsNullOrEmpty(parsed.Parameter))
            return false;

        var bytes = Convert.FromBase64String(parsed.Parameter);
        var credentials = Encoding.UTF8.GetString(bytes).Split(':', 2);
        if (credentials.Length != 2) return false;

        return credentials[0] == _config.AuthUser && credentials[1] == _config.AuthPassword;
    }
}
