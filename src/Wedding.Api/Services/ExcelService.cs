using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;

namespace Wedding.Api.Services;

public class ExcelService
{
    private readonly AppDbContext _db;

    public ExcelService(AppDbContext db)
    {
        _db = db;
    }

    public byte[] BuildExcel()
    {
        var guests = _db.Guests.Include(g => g.Companions).OrderBy(g => g.Name).ToList();
        var guestEvents = _db.GuestEvents.Include(ge => ge.Event).ToList();
        var answers = _db.Answers.Include(a => a.Question).ToList();
        var questions = _db.Questions.Where(q => q.IsActive).OrderBy(q => q.Order).ToList();
        var events = _db.Events.OrderBy(e => e.Order).ToList();

        var generalQuestions = questions.Where(q => !q.IsPerCompanion && q.EventId == null).ToList();
        var perCompanionGeneral = questions.Where(q => q.IsPerCompanion && q.EventId == null).ToList();

        var ansByGuest = answers.Where(a => a.CompanionId == null)
            .GroupBy(a => a.GuestId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.QuestionId, a => a.Value));
        var ansByCompanion = answers.Where(a => a.CompanionId != null)
            .GroupBy(a => a.CompanionId!.Value)
            .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.QuestionId, a => a.Value));

        var companionOf = guests.Where(g => g.Companions.Count > 0)
            .ToDictionary(g => g.Id, g => g.Companions[0]);

        using var wb = new XLWorkbook();

        // --- Итоги ---
        var wsSummary = wb.Worksheets.Add("Итоги");
        string[] summaryHeaders = { "Событие", "Приглашено", "Придут", "Не придут", "Не ответили", "Спутники", "Итого людей" };
        for (int c = 0; c < summaryHeaders.Length; c++)
            wsSummary.Cell(1, c + 1).Value = summaryHeaders[c];

        var row = 2;
        long totInvited = 0, totYes = 0, totNo = 0, totNot = 0, totComp = 0, totPeople = 0;
        foreach (var ev in events)
        {
            var list = guestEvents.Where(ge => ge.EventId == ev.Id).ToList();
            var invited = list.Count;
            var yes = list.Count(ge => ge.Attendance == Attendance.Yes);
            var no = list.Count(ge => ge.Attendance == Attendance.No);
            var notAns = invited - yes - no;
            var comp = list.Count(ge => ge.Attendance == Attendance.Yes && companionOf.ContainsKey(ge.GuestId));
            var people = yes + comp;

            wsSummary.Cell(row, 1).Value = ev.Name;
            wsSummary.Cell(row, 2).Value = invited;
            wsSummary.Cell(row, 3).Value = yes;
            wsSummary.Cell(row, 4).Value = no;
            wsSummary.Cell(row, 5).Value = notAns;
            wsSummary.Cell(row, 6).Value = comp;
            wsSummary.Cell(row, 7).Value = people;

            totInvited += invited; totYes += yes; totNo += no; totNot += notAns; totComp += comp; totPeople += people;
            row++;
        }
        wsSummary.Cell(row, 1).Value = "Итого";
        wsSummary.Cell(row, 2).Value = totInvited;
        wsSummary.Cell(row, 3).Value = totYes;
        wsSummary.Cell(row, 4).Value = totNo;
        wsSummary.Cell(row, 5).Value = totNot;
        wsSummary.Cell(row, 6).Value = totComp;
        wsSummary.Cell(row, 7).Value = totPeople;
        wsSummary.Columns(1, 7).AdjustToContents();

        // --- Лист на каждое событие ---
        foreach (var ev in events)
        {
            var list = guestEvents.Where(ge => ge.EventId == ev.Id).ToList();
            if (list.Count == 0) continue;

            var guestQ = generalQuestions
                .Concat(questions.Where(q => q.EventId == ev.Id && !q.IsPerCompanion))
                .ToList();
            var compQ = perCompanionGeneral
                .Concat(questions.Where(q => q.EventId == ev.Id && q.IsPerCompanion))
                .ToList();

            var sheet = wb.Worksheets.Add(SanitizeSheetName(ev.Name));
            var col = 1;
            sheet.Cell(1, col++).Value = "Гость";
            sheet.Cell(1, col++).Value = "Придёт";
            sheet.Cell(1, col++).Value = "+1";
            var guestColStart = col;
            foreach (var q in guestQ) sheet.Cell(1, col++).Value = q.Text;
            var compColStart = col;
            foreach (var q in compQ) sheet.Cell(1, col++).Value = q.Text;

            var r = 2;
            foreach (var ge in list)
            {
                var guest = guests.FirstOrDefault(g => g.Id == ge.GuestId);
                if (guest == null) continue;
                var c = 1;
                sheet.Cell(r, c++).Value = guest.Name;
                sheet.Cell(r, c++).Value = AttText(ge.Attendance);
                var hasCompanion = ge.CompanionsAllowed > 0 && companionOf.ContainsKey(guest.Id);
                sheet.Cell(r, c++).Value = hasCompanion ? companionOf[guest.Id].Name : "—";

                if (ansByGuest.TryGetValue(guest.Id, out var gAns))
                    for (int i = 0; i < guestQ.Count; i++)
                        sheet.Cell(r, guestColStart + i).Value = ValueFor(guestQ[i], gAns.TryGetValue(guestQ[i].Id, out var v) ? v : "");
                r++;

                if (hasCompanion)
                {
                    var comp = companionOf[guest.Id];
                    c = 1;
                    sheet.Cell(r, c++).Value = "(+1) " + comp.Name;
                    sheet.Cell(r, c++).Value = AttText(ge.Attendance);
                    sheet.Cell(r, c++).Value = comp.Name;
                    if (ansByCompanion.TryGetValue(comp.Id, out var cAns))
                        for (int i = 0; i < compQ.Count; i++)
                            sheet.Cell(r, compColStart + i).Value = ValueFor(compQ[i], cAns.TryGetValue(compQ[i].Id, out var v) ? v : "");
                    r++;
                }
            }
            sheet.Columns(1, col - 1).AdjustToContents();
        }

        // --- Общие вопросы ---
        if (generalQuestions.Count > 0 || perCompanionGeneral.Count > 0)
        {
            var wsGeneral = wb.Worksheets.Add("Общие вопросы");
            var col = 1;
            wsGeneral.Cell(1, col++).Value = "Гость";
            foreach (var q in generalQuestions) wsGeneral.Cell(1, col++).Value = q.Text;
            foreach (var q in perCompanionGeneral) wsGeneral.Cell(1, col++).Value = q.Text + " (+1)";

            var r = 2;
            foreach (var guest in guests)
            {
                var c = 1;
                wsGeneral.Cell(r, c++).Value = guest.Name;
                ansByGuest.TryGetValue(guest.Id, out var gAns);
                foreach (var q in generalQuestions)
                    wsGeneral.Cell(r, c++).Value = ValueFor(q, gAns != null && gAns.TryGetValue(q.Id, out var v) ? v : "");
                if (companionOf.TryGetValue(guest.Id, out var comp) && ansByCompanion.TryGetValue(comp.Id, out var cAns))
                {
                    foreach (var q in perCompanionGeneral)
                        wsGeneral.Cell(r, c++).Value = ValueFor(q, cAns.TryGetValue(q.Id, out var v) ? v : "");
                }
                else
                {
                    foreach (var _ in perCompanionGeneral) wsGeneral.Cell(r, c++).Value = "";
                }
                r++;
            }
            wsGeneral.Columns(1, col - 1).AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string AttText(Attendance a) => a switch
    {
        Attendance.Yes => "Да",
        Attendance.No => "Нет",
        _ => "—"
    };

    private static string ValueFor(Question q, string value)
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

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '*', '?', ':', '[', ']' };
        var cleaned = new string(name.Where(ch => !invalid.Contains(ch)).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Событие";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}