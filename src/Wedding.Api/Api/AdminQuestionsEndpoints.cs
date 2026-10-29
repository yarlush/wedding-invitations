using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;

namespace Wedding.Api.Api;

public record QuestionCreate(string Text, string Type, List<string>? Options, int? EventId, bool IsPerCompanion, bool IsRequired, int Order);
public record QuestionUpdate(string Text, string Type, List<string>? Options, int? EventId, bool IsPerCompanion, bool IsRequired, int Order);

public static class AdminQuestionsEndpoints
{
    public static void MapAdminQuestions(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin/questions");
        g.RequireAuthorization();

        g.MapGet("/", async (AppDbContext db) =>
        {
            var questions = await db.Questions
                .Include(q => q.Event)
                .OrderBy(q => q.Order)
                .ToListAsync();
            return Results.Ok(questions.Select(q => new
            {
                q.Id,
                q.Text,
                type = q.Type.ToString(),
                typeLabel = TypeLabel(q.Type),
                options = ParseOptions(q.Options),
                q.EventId,
                eventName = q.Event?.Name,
                q.IsPerCompanion,
                q.IsRequired,
                q.Order,
                q.IsActive
            }));
        });

        g.MapPost("/", async (QuestionCreate req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "Укажите текст вопроса" });
            if (!Enum.TryParse<QuestionType>(req.Type, true, out var type)) return Results.BadRequest(new { error = "Неизвестный тип вопроса" });
            if (req.EventId.HasValue)
            {
                var exists = await db.Events.AnyAsync(e => e.Id == req.EventId);
                if (!exists) return Results.BadRequest(new { error = "Событие не найдено" });
            }

            var q = new Question
            {
                Text = req.Text.Trim(),
                Type = type,
                Options = NormalizeOptions(req.Options, type),
                EventId = req.EventId,
                IsPerCompanion = req.IsPerCompanion,
                IsRequired = req.IsRequired,
                Order = req.Order
            };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            return Results.Ok(new { q.Id });
        });

        g.MapPut("/{id:int}", async (int id, QuestionUpdate req, AppDbContext db) =>
        {
            var q = await db.Questions.FindAsync(id);
            if (q == null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "Укажите текст вопроса" });
            if (!Enum.TryParse<QuestionType>(req.Type, true, out var type)) return Results.BadRequest(new { error = "Неизвестный тип вопроса" });
            if (req.EventId.HasValue && !await db.Events.AnyAsync(e => e.Id == req.EventId))
                return Results.BadRequest(new { error = "Событие не найдено" });

            var newOptions = NormalizeOptions(req.Options, type);

            // Если тип вопроса изменился, старые ответы больше не имеют смысла —
            // удаляем их, чтобы не осталось «висящих» значений.
            var answers = await db.Answers.Where(a => a.QuestionId == id).ToListAsync();
            var typeChanged = q.Type != type;
            var removedAnswers = 0;
            if (typeChanged)
            {
                removedAnswers = answers.Count;
                db.Answers.RemoveRange(answers);
            }
            else if (type is QuestionType.SingleChoice or QuestionType.MultiChoice)
            {
                // Варианты могли переименовать/удалить — вычищаем ответы, которых больше нет в списке.
                var allowed = ParseOptions(newOptions).ToHashSet();
                foreach (var a in answers)
                {
                    if (!ValueAllowed(a.Value, type, allowed))
                    {
                        db.Answers.Remove(a);
                        removedAnswers++;
                    }
                }
            }

            q.Text = req.Text.Trim();
            q.Type = type;
            q.Options = newOptions;
            q.EventId = req.EventId;
            q.IsPerCompanion = req.IsPerCompanion;
            q.IsRequired = req.IsRequired;
            q.Order = req.Order;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, removedAnswers });
        });

        // Сколько ответов затронет изменение (для предупреждения в интерфейсе).
        g.MapGet("/{id:int}/impact", async (int id, AppDbContext db) =>
        {
            var q = await db.Questions.FindAsync(id);
            if (q == null) return Results.NotFound();
            var answers = await db.Answers.CountAsync(a => a.QuestionId == id);
            return Results.Ok(new { answers });
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var q = await db.Questions.FindAsync(id);
            if (q == null) return Results.NotFound();
            var answers = await db.Answers.Where(a => a.QuestionId == id).ToListAsync();
            db.Answers.RemoveRange(answers);
            db.Questions.Remove(q);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }

    private static string NormalizeOptions(List<string>? options, QuestionType type)
    {
        if (type == QuestionType.Text || type == QuestionType.YesNo)
            return "";
        var list = (options ?? new List<string>())
            .Select(o => o.Trim())
            .Where(o => o.Length > 0)
            .ToList();
        return JsonSerializer.Serialize(list);
    }

    private static bool ValueAllowed(string value, QuestionType type, HashSet<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (type == QuestionType.SingleChoice) return allowed.Contains(value);
        try
        {
            var sel = JsonSerializer.Deserialize<List<string>>(value);
            return sel == null || sel.All(allowed.Contains);
        }
        catch { return false; }
    }

    private static string[] ParseOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(options) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string TypeLabel(QuestionType t) => t switch
    {
        QuestionType.Text => "Текст",
        QuestionType.SingleChoice => "Выбор одного",
        QuestionType.MultiChoice => "Выбор нескольких",
        QuestionType.YesNo => "Да / Нет",
        _ => t.ToString()
    };
}