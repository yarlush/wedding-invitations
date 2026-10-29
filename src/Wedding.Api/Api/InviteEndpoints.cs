using Wedding.Api.Services;

namespace Wedding.Api.Api;

public static class InviteEndpoints
{
    public static void MapInvite(this WebApplication app)
    {
        app.MapGet("/invite/{token}", async (string token, InvitationService svc, HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

            var guest = svc.FindByToken(token);
            if (guest == null)
                return Results.Content(NotFoundPage, "text/html; charset=utf-8", statusCode: 404);
            var data = await svc.LoadAsync(guest);
            return Results.Content(svc.BuildPage(data), "text/html; charset=utf-8");
        });

        app.MapGet("/confirm/{token}", async (string token, InvitationService svc, HttpContext ctx) =>
        {
            ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

            var guest = svc.FindByToken(token);
            if (guest == null)
                return Results.Content(NotFoundPage, "text/html; charset=utf-8", statusCode: 404);
            var html = await svc.BuildConfirmationPageAsync(guest);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapPost("/api/invite/{token}", async (string token, InvitationService.InviteSubmitRequest? req, InvitationService svc) =>
        {
            var guest = svc.FindByToken(token);
            if (guest == null)
                return Results.NotFound(new { error = "Ссылка недействительна" });
            if (req == null)
                return Results.BadRequest(new { error = "Пустой запрос" });

            var result = await svc.SubmitAsync(guest, req);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            return Results.Ok(new { ok = true, savedAt = result.SavedAt });
        });
    }

    private const string NotFoundPage = """
<!DOCTYPE html>
<html lang="ru">
<head><meta charset="utf-8"><title>Приглашение</title></head>
<body style="font-family:sans-serif;text-align:center;padding:80px 20px;color:#444;">
  <h1>Ссылка недействительна</h1>
  <p>Проверьте адрес или свяжитесь с организаторами.</p>
</body>
</html>
""";
}