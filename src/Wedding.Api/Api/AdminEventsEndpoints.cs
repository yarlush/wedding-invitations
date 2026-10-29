using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;

namespace Wedding.Api.Api;

public record EventCreate(string Name, DateTime Date, string Location, string Description, int Order);
public record EventUpdate(string Name, DateTime Date, string Location, string Description, int Order);

public static class AdminEventsEndpoints
{
    public static void MapAdminEvents(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin/events");
        g.RequireAuthorization();

        g.MapGet("/", async (AppDbContext db) =>
        {
            var events = await db.Events.OrderBy(e => e.Order).ToListAsync();
            var guestCounts = await db.GuestEvents
                .GroupBy(ge => ge.EventId)
                .Select(gr => new { EventId = gr.Key, Count = gr.Count() })
                .ToDictionaryAsync(x => x.EventId, x => x.Count);

            return Results.Ok(events.Select(e => new
            {
                e.Id,
                e.Name,
                e.Date,
                e.Location,
                e.Description,
                e.Order,
                guests = guestCounts.TryGetValue(e.Id, out var c) ? c : 0
            }));
        });

        g.MapPost("/", async (EventCreate req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Укажите название события" });

            var ev = new Event
            {
                Name = req.Name.Trim(),
                Date = req.Date,
                Location = req.Location ?? "",
                Description = req.Description ?? "",
                Order = req.Order
            };
            db.Events.Add(ev);
            await db.SaveChangesAsync();
            return Results.Ok(new { ev.Id });
        });

        g.MapPut("/{id:int}", async (int id, EventUpdate req, AppDbContext db) =>
        {
            var ev = await db.Events.FindAsync(id);
            if (ev == null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Укажите название события" });

            ev.Name = req.Name.Trim();
            ev.Date = req.Date;
            ev.Location = req.Location ?? "";
            ev.Description = req.Description ?? "";
            ev.Order = req.Order;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var ev = await db.Events.FindAsync(id);
            if (ev == null) return Results.NotFound();

            var qIds = await db.Questions.Where(q => q.EventId == id).Select(q => q.Id).ToListAsync();
            var answers = await db.Answers.Where(a => qIds.Contains(a.QuestionId)).ToListAsync();
            var questions = await db.Questions.Where(q => q.EventId == id).ToListAsync();
            var ges = await db.GuestEvents.Where(ge => ge.EventId == id).ToListAsync();

            db.Answers.RemoveRange(answers);
            db.Questions.RemoveRange(questions);
            db.GuestEvents.RemoveRange(ges);
            db.Events.Remove(ev);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }
}