using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;
using Wedding.Api.Services;

namespace Wedding.Api.Api;

public record GuestCreate(string Name);
public record GuestUpdate(string Name);
public record GuestEventInput(int EventId, int CompanionsAllowed);
public record GuestEventsUpdate(List<GuestEventInput>? Events);

public static class AdminGuestsEndpoints
{
    public static void MapAdminGuests(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin/guests");
        g.RequireAuthorization();

        g.MapGet("/", async (AppDbContext db) =>
        {
            var guests = await db.Guests
                .Include(x => x.GuestEvents).ThenInclude(x => x.Event)
                .Include(x => x.Companions)
                .OrderBy(x => x.Name)
                .ToListAsync();

            var lastSub = await db.Submissions
                .GroupBy(s => s.GuestId)
                .Select(gr => new { GuestId = gr.Key, Last = gr.Max(s => s.SubmittedAt) })
                .ToDictionaryAsync(x => x.GuestId, x => x.Last);

            return Results.Ok(guests.Select(x => new
            {
                x.Id,
                x.Name,
                x.Token,
                x.CreatedAt,
                link = $"/invite/{x.Token}",
                lastSubmissionAt = lastSub.TryGetValue(x.Id, out var l) ? l : (DateTime?)null,
                companionName = x.Companions.FirstOrDefault()?.Name,
                events = x.GuestEvents.Select(ge => new
                {
                    ge.EventId,
                    ge.Event.Name,
                    ge.CompanionsAllowed,
                    ge.Attendance
                })
            }));
        });

        g.MapPost("/", async (GuestCreate req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Укажите имя гостя" });

            var guest = new Guest
            {
                Name = req.Name.Trim(),
                Token = AuthService.GenerateToken()
            };
            db.Guests.Add(guest);
            await db.SaveChangesAsync();
            return Results.Ok(new { guest.Id, guest.Name, guest.Token, link = $"/invite/{guest.Token}" });
        });

        g.MapPut("/{id:int}", async (int id, GuestUpdate req, AppDbContext db) =>
        {
            var guest = await db.Guests.FindAsync(id);
            if (guest == null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Укажите имя гостя" });
            guest.Name = req.Name.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var guest = await db.Guests.FindAsync(id);
            if (guest == null) return Results.NotFound();

            var companionIds = await db.Companions.Where(c => c.GuestId == id).Select(c => c.Id).ToListAsync();
            var answers = await db.Answers.Where(a => a.GuestId == id || (a.CompanionId != null && companionIds.Contains(a.CompanionId.Value))).ToListAsync();
            var subs = await db.Submissions.Where(s => s.GuestId == id).ToListAsync();
            var ges = await db.GuestEvents.Where(ge => ge.GuestId == id).ToListAsync();
            var comps = await db.Companions.Where(c => c.GuestId == id).ToListAsync();

            db.Answers.RemoveRange(answers);
            db.Submissions.RemoveRange(subs);
            db.GuestEvents.RemoveRange(ges);
            db.Companions.RemoveRange(comps);
            db.Guests.Remove(guest);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/{id:int}/regenerate-token", async (int id, AppDbContext db) =>
        {
            var guest = await db.Guests.FindAsync(id);
            if (guest == null) return Results.NotFound();
            guest.Token = AuthService.GenerateToken();
            await db.SaveChangesAsync();
            return Results.Ok(new { guest.Token, link = $"/invite/{guest.Token}" });
        });

        g.MapPut("/{id:int}/events", async (int id, GuestEventsUpdate req, AppDbContext db) =>
        {
            var guest = await db.Guests.FindAsync(id);
            if (guest == null) return Results.NotFound();

            var current = await db.GuestEvents.Where(ge => ge.GuestId == id).ToListAsync();
            var incoming = req.Events ?? new List<GuestEventInput>();

            foreach (var ge in current.ToList())
            {
                var match = incoming.FirstOrDefault(i => i.EventId == ge.EventId);
                if (match == null)
                {
                    db.GuestEvents.Remove(ge);
                }
                else
                {
                    ge.CompanionsAllowed = match.CompanionsAllowed;
                    incoming.Remove(match);
                }
            }
            foreach (var i in incoming)
            {
                db.GuestEvents.Add(new GuestEvent { GuestId = id, EventId = i.EventId, CompanionsAllowed = i.CompanionsAllowed });
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }
}