using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;
using Wedding.Api.Services;

namespace Wedding.Api.Api;

public static class AdminToolsEndpoints
{
    public static void MapAdminTools(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin/tools");
        g.RequireAuthorization();

        g.MapGet("/stats", async (AppDbContext db) =>
        {
            return Results.Ok(new
            {
                guests = await db.Guests.CountAsync(),
                events = await db.Events.CountAsync(),
                questions = await db.Questions.CountAsync(),
                activeQuestions = await db.Questions.CountAsync(q => q.IsActive),
                answers = await db.Answers.CountAsync(),
                submissions = await db.Submissions.CountAsync(),
                companions = await db.Companions.CountAsync(),
                attendanceYes = await db.GuestEvents.CountAsync(ge => ge.Attendance == Attendance.Yes),
                attendanceNo = await db.GuestEvents.CountAsync(ge => ge.Attendance == Attendance.No)
            });
        });

        // Удалить все ответы и историю отправок. Гости остаются.
        g.MapPost("/clear-answers", async (AppDbContext db) =>
        {
            var answers = await db.Answers.CountAsync();
            var submissions = await db.Submissions.CountAsync();
            db.Answers.RemoveRange(db.Answers);
            db.Submissions.RemoveRange(db.Submissions);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, removedAnswers = answers, removedSubmissions = submissions });
        });

        // Сбросить отметки «придёт/не придёт» у всех гостей.
        g.MapPost("/reset-attendance", async (AppDbContext db) =>
        {
            var ges = await db.GuestEvents.ToListAsync();
            foreach (var ge in ges) ge.Attendance = Attendance.NotAnswered;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, reset = ges.Count });
        });

        // Сбросить всё сразу: ответы, историю, спутников и отметки посещения.
        g.MapPost("/reset-all", async (AppDbContext db) =>
        {
            var answers = await db.Answers.CountAsync();
            var submissions = await db.Submissions.CountAsync();
            var companions = await db.Companions.CountAsync();
            db.Answers.RemoveRange(db.Answers);
            db.Submissions.RemoveRange(db.Submissions);
            db.Companions.RemoveRange(db.Companions);
            var ges = await db.GuestEvents.ToListAsync();
            foreach (var ge in ges) ge.Attendance = Attendance.NotAnswered;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, removedAnswers = answers, removedSubmissions = submissions, removedCompanions = companions, resetAttendance = ges.Count });
        });

        // Удалить историю отправок, оставив текущие ответы.
        g.MapPost("/clear-submissions", async (AppDbContext db) =>
        {
            var submissions = await db.Submissions.CountAsync();
            db.Submissions.RemoveRange(db.Submissions);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, removedSubmissions = submissions });
        });

        // Сбросить ответы и отметки одного гостя (для повторного прохождения).
        g.MapPost("/guests/{id:int}/clear", async (int id, AppDbContext db) =>
        {
            var guest = await db.Guests.FindAsync(id);
            if (guest == null) return Results.NotFound();

            var companionIds = await db.Companions.Where(c => c.GuestId == id).Select(c => c.Id).ToListAsync();
            var answers = await db.Answers
                .Where(a => a.GuestId == id || (a.CompanionId != null && companionIds.Contains(a.CompanionId.Value)))
                .ToListAsync();
            var subs = await db.Submissions.Where(s => s.GuestId == id).ToListAsync();
            var comps = await db.Companions.Where(c => c.GuestId == id).ToListAsync();

            db.Answers.RemoveRange(answers);
            db.Submissions.RemoveRange(subs);
            db.Companions.RemoveRange(comps);
            var ges = await db.GuestEvents.Where(ge => ge.GuestId == id).ToListAsync();
            foreach (var ge in ges) ge.Attendance = Attendance.NotAnswered;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, removedAnswers = answers.Count, resetAttendance = ges.Count });
        });

        // Выдать всем гостям новые токены (сбросить все ссылки).
        g.MapPost("/reset-tokens", async (AppDbContext db) =>
        {
            var guests = await db.Guests.ToListAsync();
            foreach (var guest in guests) guest.Token = AuthService.GenerateToken();
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, updated = guests.Count });
        });

        // Демо-данные: заполнить ответы случайными значениями (для проверки интерфейса).
        g.MapPost("/fill-demo-answers", async (AppDbContext db) =>
        {
            var rnd = new Random();
            var now = DateTime.UtcNow;
            var guests = await db.Guests.Include(x => x.GuestEvents).ToListAsync();
            var questions = await db.Questions.Where(q => q.IsActive).ToListAsync();

            // 1. Сначала чистим старое и сохраняем — чтобы не было конфликтов FK при вставке.
            db.Answers.RemoveRange(db.Answers);
            db.Submissions.RemoveRange(db.Submissions);
            db.Companions.RemoveRange(db.Companions);
            await db.SaveChangesAsync();

            // 2. Спутники: создаём и сохраняем, чтобы получить их Id.
            var companionByGuest = new Dictionary<int, Companion>();
            foreach (var guest in guests)
            {
                var allowed = guest.GuestEvents.Any(x => x.CompanionsAllowed > 0);
                if (allowed && rnd.Next(0, 2) == 0)
                {
                    var comp = new Companion { GuestId = guest.Id, Name = PickName(rnd) };
                    companionByGuest[guest.Id] = comp;
                    db.Companions.Add(comp);
                }
            }
            await db.SaveChangesAsync();

            // 3. Ответы и история отправок.
            var createdAnswers = 0;
            var createdSubs = 0;
            foreach (var guest in guests)
            {
                foreach (var ge in guest.GuestEvents)
                    ge.Attendance = rnd.Next(0, 5) == 0 ? Attendance.No : Attendance.Yes;

                var invitedIds = guest.GuestEvents.Select(x => x.EventId).ToHashSet();
                companionByGuest.TryGetValue(guest.Id, out var comp);

                foreach (var q in questions.Where(q => !q.IsPerCompanion && (q.EventId == null || invitedIds.Contains(q.EventId.Value))))
                {
                    db.Answers.Add(new Answer
                    {
                        GuestId = guest.Id,
                        QuestionId = q.Id,
                        Value = RandomValue(q, rnd),
                        SubmittedAt = now
                    });
                    createdAnswers++;
                }

                if (comp != null)
                {
                    foreach (var q in questions.Where(q => q.IsPerCompanion && (q.EventId == null || invitedIds.Contains(q.EventId.Value))))
                    {
                        db.Answers.Add(new Answer
                        {
                            GuestId = guest.Id,
                            CompanionId = comp.Id,
                            QuestionId = q.Id,
                            Value = RandomValue(q, rnd),
                            SubmittedAt = now
                        });
                        createdAnswers++;
                    }
                }

                db.Submissions.Add(new Submission
                {
                    GuestId = guest.Id,
                    SubmittedAt = now,
                    CompanionName = comp?.Name,
                    AnswersJson = "{\"demo\":true}"
                });
                createdSubs++;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, answers = createdAnswers, submissions = createdSubs });
        });
    }

    private static readonly string[] Names =
    {
        "Алексей", "Дмитрий", "Сергей", "Анна", "Екатерина", "Ольга", "Мария", "Иван", "Пётр", "Наталья"
    };

    private static string PickName(Random rnd) => Names[rnd.Next(Names.Length)];

    private static string RandomValue(Question q, Random rnd) => q.Type switch
    {
        QuestionType.Text => "Демо-ответ " + rnd.Next(1, 100),
        QuestionType.YesNo => rnd.Next(0, 2) == 0 ? "да" : "нет",
        QuestionType.SingleChoice => FirstOption(q, rnd),
        QuestionType.MultiChoice => MultiOptions(q, rnd),
        _ => ""
    };

    private static string[] ParseOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options)) return Array.Empty<string>();
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(options) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string FirstOption(Question q, Random rnd)
    {
        var opts = ParseOptions(q.Options);
        return opts.Length == 0 ? "" : opts[rnd.Next(opts.Length)];
    }

    private static string MultiOptions(Question q, Random rnd)
    {
        var opts = ParseOptions(q.Options);
        if (opts.Length == 0) return "";
        var picked = opts.Where(_ => rnd.Next(0, 2) == 0).ToList();
        if (picked.Count == 0) picked.Add(opts[0]);
        return System.Text.Json.JsonSerializer.Serialize(picked);
    }
}
