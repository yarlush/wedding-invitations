using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;
using Wedding.Api.Services;

namespace Wedding.Api.Api;

public record TemplateUpdate(string? Html);

public static class AdminTemplateEndpoints
{
    public static void MapAdminTemplate(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin/template");
        g.RequireAuthorization();

        g.MapGet("/", async (AppDbContext db) =>
        {
            var html = await db.Settings.FirstOrDefaultAsync(s => s.Key == TemplateService.TemplateKey);
            return Results.Ok(new { html = html?.Value ?? "" });
        });

        g.MapPut("/", async (TemplateUpdate req, AppDbContext db) =>
        {
            var html = req.Html ?? "";
            var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == TemplateService.TemplateKey);
            if (setting == null)
            {
                db.Settings.Add(new Setting { Key = TemplateService.TemplateKey, Value = html });
            }
            else
            {
                setting.Value = html;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/preview", async (AppDbContext db, InvitationService svc) =>
        {
            var guest = await db.Guests.Include(x => x.GuestEvents)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (guest == null)
            {
                var empty = new InvitationService.InvitationData
                {
                    Guest = new Guest { Name = "Иван Иванов", Token = "preview" },
                };
                return Results.Ok(new { html = svc.BuildPage(empty) });
            }

            var data = await svc.LoadAsync(guest);
            return Results.Ok(new { html = svc.BuildPage(data) });
        });

        g.MapGet("/confirmation", async (AppDbContext db) =>
        {
            var html = await db.Settings.FirstOrDefaultAsync(s => s.Key == TemplateService.ConfirmationKey);
            return Results.Ok(new { html = html?.Value ?? "" });
        });

        g.MapPut("/confirmation", async (TemplateUpdate req, AppDbContext db) =>
        {
            var html = req.Html ?? "";
            var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == TemplateService.ConfirmationKey);
            if (setting == null)
                db.Settings.Add(new Setting { Key = TemplateService.ConfirmationKey, Value = html });
            else
                setting.Value = html;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/confirmation/preview", async (AppDbContext db, InvitationService svc) =>
        {
            var guest = await db.Guests.Include(x => x.GuestEvents)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (guest == null)
            {
                var empty = new Guest { Name = "Иван Иванов", Token = "preview" };
                return Results.Ok(new { html = await svc.BuildConfirmationPageAsync(empty, forceSubmitted: true) });
            }

            var html = await svc.BuildConfirmationPageAsync(guest, forceSubmitted: true);
            return Results.Ok(new { html });
        });
    }
}