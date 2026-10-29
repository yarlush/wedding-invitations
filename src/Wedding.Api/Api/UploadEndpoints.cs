using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;

namespace Wedding.Api.Api;

public record UploadRequest(string Name, string DataBase64);

public static class UploadEndpoints
{
    public static void MapUploads(this WebApplication app)
    {
        app.MapGet("/uploads/{id:int}", async (int id, AppDbContext db) =>
        {
            var up = await db.Uploads.FindAsync(id);
            if (up == null) return Results.NotFound();
            return Results.File(up.Data, up.ContentType);
        });

        var g = app.MapGroup("/api/admin/uploads");
        g.RequireAuthorization();

        g.MapGet("/", async (AppDbContext db) =>
        {
            var uploads = await db.Uploads.OrderByDescending(u => u.CreatedAt).ToListAsync();
            return Results.Ok(uploads.Select(u => new { u.Id, u.Name, url = $"/uploads/{u.Id}", u.CreatedAt }));
        });

        g.MapPost("/", async (UploadRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrEmpty(req.DataBase64))
                return Results.BadRequest(new { error = "Пустой файл" });

            byte[] data;
            try { data = Convert.FromBase64String(req.DataBase64); }
            catch { return Results.BadRequest(new { error = "Некорректные данные" }); }
            if (data.Length == 0)
                return Results.BadRequest(new { error = "Файл пуст" });
            if (data.Length > 10 * 1024 * 1024)
                return Results.BadRequest(new { error = "Файл больше 10 МБ" });

            var name = string.IsNullOrWhiteSpace(req.Name) ? "image.png" : req.Name.Trim();
            var up = new Upload { Name = name, ContentType = GuessContentType(name, data), Data = data };
            db.Uploads.Add(up);
            await db.SaveChangesAsync();

            return Results.Ok(new { up.Id, up.Name, url = $"/uploads/{up.Id}" });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var up = await db.Uploads.FindAsync(id);
            if (up == null) return Results.NotFound();
            db.Uploads.Remove(up);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }

    private static string GuessContentType(string name, byte[] data)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".avif" => "image/avif",
            ".bmp" => "image/bmp",
            _ => data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF ? "image/jpeg"
                : data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 ? "image/png"
                : "application/octet-stream"
        };
    }
}