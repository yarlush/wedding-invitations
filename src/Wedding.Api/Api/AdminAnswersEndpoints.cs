using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;
using Wedding.Api.Services;

namespace Wedding.Api.Api;

public static class AdminAnswersEndpoints
{
    public static void MapAdminAnswers(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin");
        g.RequireAuthorization();

        g.MapGet("/answers", async (AppDbContext db) =>
        {
            var questions = await db.Questions.Where(q => q.IsActive).OrderBy(q => q.Order).ToListAsync();
            var guests = await db.Guests.Include(x => x.Companions).OrderBy(x => x.Name).ToListAsync();
            var guestEvents = await db.GuestEvents.Include(ge => ge.Event).ToListAsync();
            var answers = await db.Answers.ToListAsync();
            var submissions = await db.Submissions
                .OrderByDescending(s => s.SubmittedAt)
                .Take(200)
                .ToListAsync();

            var general = questions.Where(q => !q.IsPerCompanion && q.EventId == null).ToList();
            var perCompGeneral = questions.Where(q => q.IsPerCompanion && q.EventId == null).ToList();

            var ansByGuest = answers.Where(a => a.CompanionId == null)
                .GroupBy(a => a.GuestId)
                .ToDictionary(gr => gr.Key, gr => gr.ToDictionary(a => a.QuestionId, a => a.Value));
            var ansByCompanion = answers.Where(a => a.CompanionId != null)
                .GroupBy(a => a.CompanionId!.Value)
                .ToDictionary(gr => gr.Key, gr => gr.ToDictionary(a => a.QuestionId, a => a.Value));
            var companionOf = guests.Where(x => x.Companions.Count > 0)
                .ToDictionary(x => x.Id, x => x.Companions[0]);

            var eventPayload = guestEvents
                .GroupBy(ge => ge.EventId)
                .OrderBy(gr => gr.First().Event.Order)
                .Select(gr =>
                {
                    var ev = gr.First().Event;
                    var guestQ = general.Concat(questions.Where(q => q.EventId == ev.Id && !q.IsPerCompanion)).ToList();
                    var compQ = perCompGeneral.Concat(questions.Where(q => q.EventId == ev.Id && q.IsPerCompanion)).ToList();

                    return new
                    {
                        ev.Id,
                        ev.Name,
                        ev.Date,
                        questions = guestQ.Select(q => new { q.Id, q.Text, type = q.Type.ToString() }),
                        companionQuestions = compQ.Select(q => new { q.Id, q.Text, type = q.Type.ToString() }),
                        guests = gr.Select(ge =>
                        {
                            var guest = guests.FirstOrDefault(x => x.Id == ge.GuestId);
                            if (guest == null) return null;
                            var hasComp = ge.CompanionsAllowed > 0 && companionOf.TryGetValue(guest.Id, out var _);
                            return new
                            {
                                guestId = guest.Id,
                                guestName = guest.Name,
                                attendance = ge.Attendance.ToString(),
                                companionName = hasComp ? companionOf[guest.Id].Name : null,
                                answers = guestQ.ToDictionary(q => q.Id.ToString(), q =>
                                    ansByGuest.TryGetValue(guest.Id, out var m) && m.TryGetValue(q.Id, out var v) ? Display(q, v) : ""),
                                companionAnswers = hasComp && ansByCompanion.TryGetValue(companionOf[guest.Id].Id, out var cm)
                                    ? compQ.ToDictionary(q => q.Id.ToString(), q => cm.TryGetValue(q.Id, out var v) ? Display(q, v) : "")
                                    : null
                            };
                        }).Where(x => x != null)
                    };
                }).ToList();

            return Results.Ok(new
            {
                generalQuestions = general.Select(q => new { q.Id, q.Text, type = q.Type.ToString() }),
                events = eventPayload,
                submissions = submissions.Select(s => new
                {
                    s.Id,
                    s.GuestId,
                    guestName = guests.FirstOrDefault(x => x.Id == s.GuestId)?.Name ?? "?",
                    s.SubmittedAt,
                    s.CompanionName,
                    answers = DeserializeSnapshot(s.AnswersJson)
                })
            });
        });

        g.MapGet("/export.xlsx", (ExcelService excel) =>
        {
            var bytes = excel.BuildExcel();
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ответы-гостей-{DateTime.Now:yyyy-MM-dd}.xlsx");
        });
    }

    private static JsonElement? DeserializeSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return null; }
    }

    private static string Display(Question q, string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return q.Type switch
        {
            QuestionType.YesNo => value == "да" ? "Да" : value == "нет" ? "Нет" : value,
            QuestionType.MultiChoice => JoinMulti(value),
            _ => value
        };
    }

    private static string JoinMulti(string value)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(value);
            return list == null ? value : string.Join(", ", list);
        }
        catch { return value; }
    }
}