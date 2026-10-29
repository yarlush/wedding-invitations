using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Wedding.Api.Services;

public class TemplateService
{
    public const string TemplateKey = "InvitationTemplate";
    public const string ConfirmationKey = "ConfirmationTemplate";

    public string Build(string? templateHtml, string guestName, string eventsHtml, string? companionHtml, string questionsHtml, string script)
    {
        var html = string.IsNullOrWhiteSpace(templateHtml) ? DefaultTemplate : templateHtml;
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        SetText(doc, "guest-name", guestName);
        SetHtml(doc, "events", eventsHtml);
        SetHtmlOrHide(doc, "companion-section", companionHtml);
        SetHtml(doc, "questions", questionsHtml);

        if (doc.Body != null)
        {
            var s = doc.CreateElement("script");
            s.TextContent = script;
            doc.Body.AppendChild(s);
        }

        return doc.DocumentElement?.OuterHtml ?? html;
    }

    public string BuildConfirmation(string? templateHtml, string guestName, string message, string summaryHtml, string backLinkHtml)
    {
        var html = string.IsNullOrWhiteSpace(templateHtml) ? DefaultConfirmationTemplate : templateHtml;
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        SetText(doc, "guest-name", guestName);
        SetText(doc, "message", message);
        SetHtml(doc, "summary", summaryHtml);
        SetHtml(doc, "back-link", backLinkHtml);

        return doc.DocumentElement?.OuterHtml ?? html;
    }

    private static void SetText(IDocument doc, string id, string text)
    {
        var el = doc.GetElementById(id);
        if (el != null) el.TextContent = text;
    }

    private static void SetHtml(IDocument doc, string id, string html)
    {
        var el = doc.GetElementById(id);
        if (el == null || string.IsNullOrEmpty(html)) return;
        el.InnerHtml = html;
    }

    private static void SetHtmlOrHide(IDocument doc, string id, string? html)
    {
        var el = doc.GetElementById(id);
        if (el == null) return;
        if (string.IsNullOrEmpty(html))
        {
            el.SetAttribute("style", "display:none");
            el.InnerHtml = "";
            return;
        }
        el.InnerHtml = html;
    }

    public const string DefaultTemplate = """
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Приглашение</title>
<style>
  body { font-family: Georgia, 'Times New Roman', serif; background: #faf7f2; color: #3a3a3a; margin: 0; }
  .page { max-width: 720px; margin: 0 auto; padding: 48px 24px; }
  .title { font-size: 34px; text-align: center; margin: 8px 0 4px; }
  .subtitle { text-align: center; color: #8a8a8a; margin-bottom: 32px; }
  .card { background: #fff; border: 1px solid #eee; border-radius: 12px; padding: 20px 24px; margin-bottom: 16px; box-shadow: 0 1px 4px rgba(0,0,0,.04); }
  .card h3 { margin: 0 0 8px; color: #b8860b; }
  .card p { margin: 4px 0; }
  .attendance { margin-top: 12px; }
  .attendance label { margin-right: 16px; font-weight: normal; }
  .question { margin: 18px 0; }
  .question p { margin: 0 0 6px; font-weight: bold; }
  .question.invalid > p { color: #c00; }
  .question.invalid input[type=text] { border-color: #c00; }
  .question.invalid label { border-color: #e8a9a4; background: #fdf1ef; }
  input[type=text] { width: 100%; padding: 10px; box-sizing: border-box; border: 1px solid #ddd; border-radius: 8px; font-size: 15px; }
  .req { color: #c00; }
  .btn { display: inline-block; margin-top: 24px; padding: 14px 40px; font-size: 17px; background: #b8860b; color: #fff; border: none; border-radius: 10px; cursor: pointer; }
  .btn:disabled { opacity: .6; }
  #message { margin-top: 16px; padding: 12px 16px; border-radius: 8px; display: none; }
  #message.ok { display: block; background: #e8f5e9; color: #2e7d32; }
  #message.err { display: block; background: #fdecea; color: #c62828; }
  .companion { background: #fdf6e3; border: 1px dashed #e0c98a; border-radius: 10px; padding: 16px 20px; margin: 16px 0; }
  .companion-choice label { margin-right: 14px; cursor: pointer; }
  #companion-name.invalid { border-color: #c00; }
</style>
</head>
<body>
  <div class="page">
    <h1 class="title" id="guest-name"></h1>
    <p class="subtitle">Мы будем рады видеть вас на нашем празднике!</p>
    <div id="events"></div>
    <div id="companion-section"></div>
    <div id="questions"></div>
    <button class="btn" id="submit-btn">Отправить ответ</button>
    <div id="message"></div>
  </div>
</body>
</html>
""";

    public const string DefaultConfirmationTemplate = """
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Спасибо!</title>
<style>
  body { font-family: Georgia, 'Times New Roman', serif; background: #faf7f2; color: #3a3a3a; margin: 0; }
  .page { max-width: 640px; margin: 0 auto; padding: 72px 24px; text-align: center; }
  .emblem {
    width: 84px; height: 84px; margin: 0 auto 26px;
    border: 2px solid #b8860b; border-radius: 50%;
    display: flex; align-items: center; justify-content: center;
    font-size: 40px; color: #b8860b; background: #fffdf8;
  }
  .title { font-size: 40px; margin: 0 0 6px; color: #2d2a26; }
  .guest-name { font-size: 30px; font-weight: bold; color: #b8860b; margin: 12px 0 20px; }
  #message { font-size: 17px; color: #666; max-width: 480px; margin: 0 auto 8px; line-height: 1.6; }
  #summary {
    background: #fff; border: 1px solid #eee; border-radius: 12px;
    padding: 20px 24px; margin: 26px auto; max-width: 520px; text-align: left;
    box-shadow: 0 1px 4px rgba(0,0,0,.04);
  }
  #summary h3 { margin: 0 0 12px; font-size: 17px; color: #b8860b; }
  #summary .row { display: flex; justify-content: space-between; gap: 12px; padding: 8px 0; border-bottom: 1px dashed #eee; font-size: 15px; }
  #summary .row:last-child { border-bottom: none; }
  #summary .row .val { font-weight: bold; text-align: right; }
  .btn {
    display: inline-block; margin-top: 20px; padding: 13px 36px; font-size: 15px;
    background: #b8860b; color: #fff; border: none; border-radius: 10px; text-decoration: none;
  }
  .btn.secondary { background: #fff; color: #b8860b; border: 1px solid #e0c98a; }
  .back { display: inline-block; margin-top: 10px; }
</style>
</head>
<body>
  <div class="page">
    <div class="emblem">&#10003;</div>
    <h1 class="title">Спасибо!</h1>
    <div id="guest-name" class="guest-name"></div>
    <div id="message"></div>
    <div id="summary"></div>
    <div id="back-link"></div>
  </div>
</body>
</html>
""";
}