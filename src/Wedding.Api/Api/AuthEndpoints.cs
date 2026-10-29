using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Wedding.Api.Api;

public record LoginRequest(string Username, string Password);

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        app.MapPost("/api/admin/login", async (LoginRequest req, IConfiguration cfg, HttpContext ctx) =>
        {
            // Приоритет: ADMIN_USERNAME/ADMIN_PASSWORD (удобные имена для хостинга),
            // затем стандартные ключи конфигурации Admin:Username / Admin:Password.
            var user = Environment.GetEnvironmentVariable("ADMIN_USERNAME")
                       ?? cfg["Admin:Username"]
                       ?? "admin";
            var pass = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
                       ?? cfg["Admin:Password"]
                       ?? "admin";
            if (string.IsNullOrEmpty(req.Username) || req.Username != user || req.Password != pass)
                return Results.Unauthorized();

            var claims = new[] { new Claim(ClaimTypes.Name, user) };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/admin/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/admin/me", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated == true)
                return Results.Ok(new { authenticated = true, username = ctx.User.Identity.Name });
            return Results.Ok(new { authenticated = false });
        });
    }
}