using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wedding.Api.Data;

namespace Wedding.Api.Services;

public class InvitationService
{
    private readonly AppDbContext _db;
    private readonly TemplateService _template;

    public InvitationService(AppDbContext db, TemplateService template)
    {
        _db = db;
        _template = template;
    }

    public Guest? FindByToken(string token) =>
        _db.Guests.Include(g => g.Companions).FirstOrDefault(g => g.Token == token);

    public record QuestionView(int Id, string Text, string Type, string[] Options, bool IsRequired, int? EventId);
    public record EventView(int Id, string Name, string Date, string Location, string Description, bool PlusOne, string Attendance);

    public class InvitationData
    {
        public Guest Guest { get; init; } = null!;
        public List<EventView> Events { get; init; } = new();
        public List<QuestionView> Questions { get; init; } = new();
        public List<QuestionView> CompanionQuestions { get; init; } = new();
        public Dictionary<int, string> GuestAnswers { get; init; } = new();
        public Dictionary<int, string> CompanionAnswers { get; init; } = new();
        public string? CompanionName { get; init; }
    }

    public async Task<InvitationData> LoadAsync(Guest guest)
    {
        var events = await _db.GuestEvents
            .Where(ge => ge.GuestId == guest.Id)
            .Include(ge => ge.Event)
            .OrderBy(ge => ge.Event.Order)
            .ToListAsync();

        var invitedEventIds = events.Select(e => e.EventId).ToList();
        var questions = await _db.Questions
            .Where(q => q.IsActive && (q.EventId == null || invitedEventIds.Contains(q.EventId.Value)))
            .OrderBy(q => q.Order)
            .ToListAsync();

        var answers = await _db.Answers
            .Where(a => a.GuestId == guest.Id)
            .ToListAsync();

        var companion = guest.Companions.FirstOrDefault();
        var companionAnswers = new Dictionary<int, string>();
        if (companion != null)
        {
            foreach (var a in answers.Where(x => x.CompanionId == companion.Id))
                companionAnswers[a.QuestionId] = a.Value;
        }

        return new InvitationData
        {
            Guest = guest,
            Events = events.Select(e => new EventView(
                e.EventId,
                e.Event.Name,
                FormatDate(e.Event.Date),
                e.Event.Location,
                e.Event.Description,
                e.CompanionsAllowed > 0,
                e.Attendance switch
                {
                    Attendance.Yes => "yes",
                    Attendance.No => "no",
                    _ => ""
                })).ToList(),
            Questions = questions.Where(q => !q.IsPerCompanion).Select(ToView).ToList(),
            CompanionQuestions = questions.Where(q => q.IsPerCompanion).Select(ToView).ToList(),
            GuestAnswers = answers.Where(a => a.CompanionId == null).ToDictionary(a => a.QuestionId, a => a.Value),
            CompanionAnswers = companionAnswers,
            CompanionName = companion?.Name
        };
    }

    private static QuestionView ToView(Question q) => new(
        q.Id, q.Text, q.Type.ToString(), ParseOptions(q.Options), q.IsRequired, q.EventId);

    private static string[] ParseOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(options) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string FormatDate(DateTime d)
    {
        var s = d.ToString("dd MMMM yyyy", new CultureInfo("ru-RU"));
        if (d.Hour != 0 || d.Minute != 0)
            s += " в " + d.ToString("HH:mm");
        return s;
    }

    public string BuildPage(InvitationData data)
    {
        var eventsHtml = BuildEventsHtml(data);
        var companionHtml = data.Events.Any(e => e.PlusOne)
            ? BuildCompanionHtml(data)
            : null;
        var questionsHtml = BuildQuestionsHtml(data);
        var script = BuildScript(data.Guest.Token);
        return _template.Build(GetTemplate(), data.Guest.Name, eventsHtml, companionHtml, questionsHtml, script);
    }

    public async Task<string> BuildConfirmationPageAsync(Guest guest, bool forceSubmitted = false)
    {
        var submitted = forceSubmitted || await _db.Submissions.AnyAsync(s => s.GuestId == guest.Id);
        var data = await LoadAsync(guest);

        string message;
        string backLink;
        if (submitted)
        {
            message = "Ваш ответ сохранён. Если что-то изменится — откройте приглашение и отправьте ответ заново.";
            backLink = $"<a class=\"btn secondary back\" href=\"/invite/{guest.Token}\">Изменить ответ</a>";
        }
        else
        {
            message = "Вы ещё не отправили ответ на приглашение.";
            backLink = $"<a class=\"btn back\" href=\"/invite/{guest.Token}\">Открыть приглашение</a>";
        }

        var summary = submitted ? BuildSummaryHtml(data) : "";
        return _template.BuildConfirmation(
            GetSetting(TemplateService.ConfirmationKey), data.Guest.Name, message, summary, backLink);
    }

    private string GetSetting(string key) =>
        _db.Settings.FirstOrDefault(s => s.Key == key)?.Value ?? "";

    private static string BuildSummaryHtml(InvitationData data)
    {
        var sb = new StringBuilder();
        foreach (var e in data.Events)
        {
            var val = e.Attendance switch
            {
                "yes" => "Приду",
                "no" => "Не приду",
                _ => "Не указано"
            };
            sb.Append($"<div class=\"row\"><span>{E(e.Name)}</span><span class=\"val\">{val}</span></div>");
        }
        if (!string.IsNullOrEmpty(data.CompanionName))
            sb.Append($"<div class=\"row\"><span>Спутник (+1)</span><span class=\"val\">{E(data.CompanionName)}</span></div>");
        foreach (var q in data.Questions)
        {
            var value = data.GuestAnswers.GetValueOrDefault(q.Id, "");
            if (string.IsNullOrEmpty(value)) continue;
            sb.Append($"<div class=\"row\"><span>{E(q.Text)}</span><span class=\"val\">{E(DisplayValue(q.Type, value))}</span></div>");
        }
        foreach (var q in data.CompanionQuestions)
        {
            var value = data.CompanionAnswers.GetValueOrDefault(q.Id, "");
            if (string.IsNullOrEmpty(value)) continue;
            var suffix = q.Text.Contains("(+1)") || q.Text.Contains("спутник") || q.Text.Contains("Спутник") ? "" : " (+1)";
            sb.Append($"<div class=\"row\"><span>{E(q.Text)}{suffix}</span><span class=\"val\">{E(DisplayValue(q.Type, value))}</span></div>");
        }
        return sb.ToString();
    }

    private static string DisplayValue(string type, string value)
    {
        return type switch
        {
            "YesNo" => value == "да" ? "Да" : value == "нет" ? "Нет" : value,
            "MultiChoice" => JoinMulti(value),
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

    private string GetTemplate() => GetSetting(TemplateService.TemplateKey);

    private static string BuildEventsHtml(InvitationData data)
    {
        var sb = new StringBuilder();
        foreach (var e in data.Events)
        {
            sb.Append("<div class=\"card reveal\">");
            sb.Append($"<h3>{E(e.Name)}</h3>");
            sb.Append($"<p>Дата: <b>{E(e.Date)}</b></p>");
            if (!string.IsNullOrEmpty(e.Location)) sb.Append($"<p>Место: {E(e.Location)}</p>");
            if (!string.IsNullOrEmpty(e.Description)) sb.Append($"<p>{E(e.Description)}</p>");
            if (e.PlusOne) sb.Append("<p><i>Вы можете прийти с +1</i></p>");
            sb.Append("<div class=\"attendance\">");
            sb.Append("<p><b>Вы придёте?</b></p>");
            sb.Append($"<label><input type=\"radio\" name=\"att{e.Id}\" value=\"yes\"{(e.Attendance == "yes" ? " checked" : "")}> Да</label> ");
            sb.Append($"<label><input type=\"radio\" name=\"att{e.Id}\" value=\"no\"{(e.Attendance == "no" ? " checked" : "")}> Нет</label>");
            sb.Append("</div></div>");
        }
        return sb.ToString();
    }

    private static string BuildCompanionHtml(InvitationData data)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"companion reveal\">");
        sb.Append("<p><b>Ваш спутник (пара)</b></p>");
        var hasCompanion = !string.IsNullOrEmpty(data.CompanionName);
        sb.Append("<div class=\"companion-choice\">");
        sb.Append($"<label><input type=\"radio\" name=\"has-companion\" value=\"no\"{(hasCompanion ? "" : " checked")}> Приду без пары</label> ");
        sb.Append($"<label><input type=\"radio\" name=\"has-companion\" value=\"yes\"{(hasCompanion ? " checked" : "")}> Приду с парой</label>");
        sb.Append("</div>");
        sb.Append("<div id=\"companion-details\">");
        sb.Append($"<input type=\"text\" id=\"companion-name\" placeholder=\"Имя спутника\" value=\"{E(data.CompanionName ?? "")}\">");
        foreach (var q in data.CompanionQuestions)
            sb.Append(BuildQuestionBlock(data, q, "cq"));
        sb.Append("</div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildQuestionsHtml(InvitationData data)
    {
        var sb = new StringBuilder();
        foreach (var q in data.Questions)
            sb.Append(BuildQuestionBlock(data, q, "q"));
        return sb.ToString();
    }

    private static string BuildQuestionBlock(InvitationData data, QuestionView q, string prefix)
    {
        var isCompanion = prefix == "cq";
        var sb = new StringBuilder();
        var eventAttr = q.EventId.HasValue ? $" data-event=\"{q.EventId.Value}\"" : "";
        sb.Append($"<div class=\"question{(isCompanion ? " companion-question" : "")} reveal\" data-qid=\"{q.Id}\"{eventAttr}>");
        sb.Append($"<p>{E(q.Text)} {(q.IsRequired ? "<span class=\"req\">*</span>" : "")}</p>");
        var name = $"{prefix}{q.Id}";
        var current = (isCompanion ? data.CompanionAnswers : data.GuestAnswers).GetValueOrDefault(q.Id, "");

        switch (q.Type)
        {
            case "Text":
                sb.Append($"<input type=\"text\" name=\"{name}\" value=\"{E(current)}\">");
                break;
            case "YesNo":
                sb.Append(Radio(name, "да", current, "Да"));
                sb.Append(Radio(name, "нет", current, "Нет"));
                break;
            case "SingleChoice":
                foreach (var opt in q.Options) sb.Append(Radio(name, opt, current, opt));
                break;
            case "MultiChoice":
                foreach (var opt in q.Options) sb.Append(Checkbox(name, opt, current, opt));
                break;
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Radio(string name, string value, string current, string label)
    {
        var selected = current == value;
        return $"<label><input type=\"radio\" name=\"{name}\" value=\"{E(value)}\"{(selected ? " checked" : "")}> {E(label)}</label> ";
    }

    private static string Checkbox(string name, string value, string current, string label)
    {
        var checkedAttr = MultiContains(current, value) ? " checked" : "";
        return $"<label><input type=\"checkbox\" name=\"{name}\" value=\"{E(value)}\"{checkedAttr}> {E(label)}</label> ";
    }

    private static bool MultiContains(string current, string value)
    {
        if (string.IsNullOrWhiteSpace(current)) return false;
        try
        {
            var sel = JsonSerializer.Deserialize<List<string>>(current);
            return sel?.Contains(value) == true;
        }
        catch { return false; }
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string BuildScript(string token)
    {
        return $$"""
(function () {
    var btn = document.getElementById('submit-btn');
    var msg = document.getElementById('message');
    if (!btn || !msg) return;

    var compInput = document.getElementById('companion-name');
    var compRadio = document.querySelector('input[name="has-companion"]');

    function showMsg(cls, text) {
        msg.className = cls;
        msg.textContent = text;
        msg.style.display = text ? 'block' : 'none';
    }

    function isAnswered(q) {
        if (!q.querySelector('.req')) return true;
        var inputs = q.querySelectorAll('input, select, textarea');
        for (var i = 0; i < inputs.length; i++) {
            var el = inputs[i];
            if (el.type === 'checkbox' || el.type === 'radio') {
                if (el.checked) return true;
            } else if (el.value && el.value.trim()) {
                return true;
            }
        }
        return false;
    }

    function collect(q, map) {
        var qid = q.getAttribute('data-qid');
        var inputs = q.querySelectorAll('input, select, textarea');
        if (!inputs.length) return;
        var first = inputs[0];
        if (first.type === 'checkbox') {
            var vals = [];
            q.querySelectorAll('[name="' + first.name + '"]:checked').forEach(function (c) { vals.push(c.value); });
            map[qid] = JSON.stringify(vals);
        } else if (first.type === 'radio') {
            var checked = q.querySelector('[name="' + first.name + '"]:checked');
            map[qid] = checked ? checked.value : '';
        } else {
            map[qid] = first.value;
        }
    }

    function companionSelected() {
        if (!compRadio) return false;
        var checked = document.querySelector('input[name="has-companion"]:checked');
        return !!checked && checked.value === 'yes';
    }

    function eventAttending(q) {
        var eventId = q.getAttribute('data-event');
        if (!eventId) return true;
        var checked = document.querySelector('input[name="att' + eventId + '"]:checked');
        return !checked || checked.value !== 'no';
    }

    function syncQuestions() {
        var blocks = document.querySelectorAll('.question[data-event]');
        for (var i = 0; i < blocks.length; i++) {
            var q = blocks[i];
            var show = eventAttending(q);
            q.style.display = show ? '' : 'none';
            if (show) q.classList.remove('skipped', 'invalid');
            else q.classList.add('skipped');
        }
    }

    function syncCompanion() {
        var details = document.getElementById('companion-details');
        if (details) details.style.display = companionSelected() ? '' : 'none';
    }
    if (compRadio) {
        var radios = document.querySelectorAll('input[name="has-companion"]');
        for (var i = 0; i < radios.length; i++) radios[i].addEventListener('change', syncCompanion);
    }
    var attendanceRadios = document.querySelectorAll('input[type=radio][name^="att"]');
    for (var r = 0; r < attendanceRadios.length; r++)
        attendanceRadios[r].addEventListener('change', syncQuestions);
    syncCompanion();
    syncQuestions();

    btn.addEventListener('click', async function () {
        var blocks = Array.prototype.slice.call(document.querySelectorAll('.question[data-qid]'));
        blocks.forEach(function (q) { q.classList.remove('invalid'); });
        if (compInput) compInput.classList.remove('invalid');

        var cq = {};
        if (compInput) {
            blocks.forEach(function (q) {
                if (q.classList.contains('companion-question') && !q.classList.contains('skipped'))
                    collect(q, cq);
            });
        }

        var useCompanion;
        if (compRadio) {
            useCompanion = companionSelected();
        } else if (compInput) {
            var cqHasValue = false;
            Object.keys(cq).forEach(function (k) {
                var v = cq[k];
                if (typeof v === 'string' ? v.trim() !== '' : v.length > 0) cqHasValue = true;
            });
            useCompanion = compInput.value.trim() !== '' || cqHasValue;
        } else {
            useCompanion = false;
        }

        var firstInvalid = null;
        blocks.forEach(function (q) {
            if (q.classList.contains('skipped')) return;
            if (q.classList.contains('companion-question') && !useCompanion) return;
            if (!isAnswered(q)) {
                q.classList.add('invalid');
                if (!firstInvalid) firstInvalid = q;
            }
        });

        if (useCompanion && compInput && !compInput.value.trim()) {
            compInput.classList.add('invalid');
            showMsg('err', 'Укажите имя спутника (пары).');
            compInput.focus();
            return;
        }

        if (firstInvalid) {
            showMsg('err', 'Пожалуйста, ответьте на выделенные обязательные вопросы.');
            firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
            return;
        }

        var answers = {};
        blocks.forEach(function (q) {
            if (q.classList.contains('companion-question') || q.classList.contains('skipped')) return;
            collect(q, answers);
        });

        var attendance = {};
        document.querySelectorAll('.attendance').forEach(function (a) {
            var checked = a.querySelector('input[type=radio]:checked');
            if (checked) attendance[checked.name.replace('att', '')] = checked.value;
        });

        var companion = null;
        if (compInput && useCompanion) companion = { name: compInput.value.trim(), answers: cq };

        var payload = { answers: answers, attendance: attendance, companion: companion };

        var btnText = btn.textContent;
        btn.disabled = true;
        btn.textContent = 'Отправляем...';
        showMsg('', '');

        var controller = new AbortController();
        var timer = setTimeout(function () { controller.abort(); }, 25000);
        try {
            var resp = await fetch('/api/invite/{{token}}', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload),
                signal: controller.signal
            });
            clearTimeout(timer);
            var text = await resp.text();
            var data = null;
            try { data = JSON.parse(text); } catch (e) {}
            if (resp.ok) {
                location.href = '/confirm/{{token}}';
                return;
            }
            showMsg('err', (data && data.error) || 'Ошибка при отправке. Попробуйте ещё раз.');
        } catch (e) {
            showMsg('err', 'Ошибка сети. Проверьте соединение и попробуйте ещё раз.');
        } finally {
            clearTimeout(timer);
            btn.disabled = false;
            btn.textContent = btnText;
        }
    });
})();
""";
    }

    public record SubmitResult(bool Success, string? Error = null, DateTime? SavedAt = null);

    public record CompanionSubmit(string? Name, Dictionary<string, string>? Answers);
    public record InviteSubmitRequest(Dictionary<string, string>? Answers, Dictionary<string, string>? Attendance, CompanionSubmit? Companion);

    public async Task<SubmitResult> SubmitAsync(Guest guest, InviteSubmitRequest req)
    {
        var now = DateTime.UtcNow;
        var guestEvents = await _db.GuestEvents
            .Where(ge => ge.GuestId == guest.Id)
            .Include(ge => ge.Event)
            .ToListAsync();

        var invitedEventIds = guestEvents.Select(ge => ge.EventId).ToHashSet();
        var companionAllowed = guestEvents.Any(ge => ge.CompanionsAllowed > 0);

        var questions = await _db.Questions
            .Where(q => q.IsActive && (q.EventId == null || invitedEventIds.Contains(q.EventId.Value)))
            .ToListAsync();

        var errors = new List<string>();

        if (req.Attendance != null)
        {
            foreach (var (key, value) in req.Attendance)
            {
                if (!int.TryParse(key, out var eventId)) continue;
                if (!invitedEventIds.Contains(eventId)) continue;
                var ge = guestEvents.First(x => x.EventId == eventId);
                ge.Attendance = value switch
                {
                    "yes" => Attendance.Yes,
                    "no" => Attendance.No,
                    _ => ge.Attendance
                };
            }
        }

        var notAttendingEventIds = guestEvents
            .Where(ge => ge.Attendance == Attendance.No)
            .Select(ge => ge.EventId)
            .ToHashSet();

        bool IsVisible(Question q) =>
            q.EventId == null || !notAttendingEventIds.Contains(q.EventId.Value);

        var visibleQuestions = questions.Where(q => !q.IsPerCompanion && IsVisible(q)).ToList();
        var visibleCompanionQuestions = questions.Where(q => q.IsPerCompanion && IsVisible(q)).ToList();
        var skippedQuestionIds = questions.Where(q => !IsVisible(q)).Select(q => q.Id).ToList();

        if (skippedQuestionIds.Count > 0)
        {
            var staleAnswers = _db.Answers
                .Where(a => a.GuestId == guest.Id && skippedQuestionIds.Contains(a.QuestionId))
                .ToList();
            _db.Answers.RemoveRange(staleAnswers);
        }

        var answers = req.Answers ?? new Dictionary<string, string>();
        foreach (var q in visibleQuestions)
        {
            answers.TryGetValue(q.Id.ToString(), out var raw);
            var value = raw?.Trim() ?? "";
            var (ok, err) = Validate(q, value);
            if (!ok) { errors.Add($"{q.Text}: {err}"); continue; }
            if (q.IsRequired && string.IsNullOrEmpty(value))
                errors.Add($"Поле «{q.Text}» обязательно");
        }

        var companionAnswers = req.Companion?.Answers ?? new Dictionary<string, string>();
        var hasCompanionPayload = req.Companion != null &&
            (!string.IsNullOrWhiteSpace(req.Companion.Name) ||
             companionAnswers.Any(kv => !string.IsNullOrWhiteSpace(kv.Value)));

        if (companionAllowed && hasCompanionPayload)
        {
            if (string.IsNullOrWhiteSpace(req.Companion!.Name))
                errors.Add("Укажите имя спутника (пары)");

            foreach (var q in visibleCompanionQuestions)
            {
                companionAnswers.TryGetValue(q.Id.ToString(), out var raw);
                var value = raw?.Trim() ?? "";
                var (ok, err) = Validate(q, value);
                if (!ok) { errors.Add($"{q.Text}: {err}"); continue; }
                if (q.IsRequired && string.IsNullOrEmpty(value))
                    errors.Add($"Поле «{q.Text}» (для спутника) обязательно");
            }
        }
        else if (!companionAllowed && hasCompanionPayload)
        {
            errors.Add("Для вас +1 не предусмотрен");
        }

        if (errors.Count > 0)
            return new SubmitResult(false, string.Join(" ", errors));

        var guestCompanion = guest.Companions.FirstOrDefault();

        if (companionAllowed && hasCompanionPayload)
        {
            if (guestCompanion == null)
            {
                guestCompanion = new Companion { GuestId = guest.Id, Name = req.Companion!.Name! };
                _db.Companions.Add(guestCompanion);
                await _db.SaveChangesAsync();
            }
            else
            {
                guestCompanion.Name = req.Companion!.Name!;
            }
        }
        else if (companionAllowed && guestCompanion != null)
        {
            var oldAnswers = _db.Answers.Where(a => a.CompanionId == guestCompanion.Id).ToList();
            _db.Answers.RemoveRange(oldAnswers);
            _db.Companions.Remove(guestCompanion);
            guestCompanion = null;
        }

        foreach (var q in visibleQuestions)
        {
            answers.TryGetValue(q.Id.ToString(), out var raw);
            var value = raw?.Trim() ?? "";
            UpsertAnswer(guest.Id, null, q.Id, value, now);
        }

        if (guestCompanion != null)
        {
            foreach (var q in visibleCompanionQuestions)
            {
                companionAnswers.TryGetValue(q.Id.ToString(), out var raw);
                var value = raw?.Trim() ?? "";
                UpsertAnswer(guest.Id, guestCompanion.Id, q.Id, value, now);
            }
        }

        var snapshot = new
        {
            answers = visibleQuestions.ToDictionary(
                q => q.Id.ToString(),
                q => answers.TryGetValue(q.Id.ToString(), out var r) ? (r?.Trim() ?? "") : ""),
            companion = guestCompanion != null ? new
            {
                name = guestCompanion.Name,
                answers = visibleCompanionQuestions.ToDictionary(
                    q => q.Id.ToString(),
                    q => companionAnswers.TryGetValue(q.Id.ToString(), out var r) ? (r?.Trim() ?? "") : "")
            } : null
        };

        _db.Submissions.Add(new Submission
        {
            GuestId = guest.Id,
            SubmittedAt = now,
            CompanionName = guestCompanion?.Name,
            AnswersJson = JsonSerializer.Serialize(snapshot)
        });

        await _db.SaveChangesAsync();
        return new SubmitResult(true, null, now);
    }

    private static (bool ok, string? error) Validate(Question q, string value)
    {
        switch (q.Type)
        {
            case QuestionType.Text:
                return (true, null);
            case QuestionType.YesNo:
                return value == "да" || value == "нет" || value == ""
                    ? (true, null)
                    : (false, "недопустимый ответ");
            case QuestionType.SingleChoice:
            {
                var opts = ParseOptions(q.Options);
                return value == "" || opts.Contains(value) ? (true, null) : (false, "выберите один из предложенных вариантов");
            }
            case QuestionType.MultiChoice:
            {
                var opts = ParseOptions(q.Options);
                if (string.IsNullOrWhiteSpace(value)) return (true, null);
                try
                {
                    var sel = JsonSerializer.Deserialize<List<string>>(value);
                    if (sel == null) return (false, "некорректный ответ");
                    return sel.All(s => opts.Contains(s)) ? (true, null) : (false, "недопустимые варианты");
                }
                catch { return (false, "некорректный ответ"); }
            }
            default:
                return (false, "неизвестный тип вопроса");
        }
    }

    private void UpsertAnswer(int guestId, int? companionId, int questionId, string value, DateTime now)
    {
        var existing = _db.Answers
            .FirstOrDefault(a => a.GuestId == guestId && a.CompanionId == companionId && a.QuestionId == questionId);
        if (existing != null)
        {
            existing.Value = value;
            existing.SubmittedAt = now;
        }
        else
        {
            _db.Answers.Add(new Answer
            {
                GuestId = guestId,
                CompanionId = companionId,
                QuestionId = questionId,
                Value = value,
                SubmittedAt = now
            });
        }
    }
}