using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Wedding.Api.Api;
using Wedding.Api.Data;
using Wedding.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port) && int.TryParse(port, out var portNum))
    builder.WebHost.UseUrls($"http://0.0.0.0:{portNum}");

var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "wedding.db";
if (!Path.IsPathRooted(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, dbPath);
var dbDir = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDir))
    Directory.CreateDirectory(dbDir);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "wedding_admin";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<ExcelService>();
builder.Services.AddSingleton<TemplateService>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

// apply migrations / create db on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Seed/bootstrap: if SEED_DB points to a bundled pre-populated database and the
// live database has no guests yet (e.g. a fresh persistent volume), replace it
// with the bundled copy. Lets production start with real data without any HTTP.
var seedDb = Environment.GetEnvironmentVariable("SEED_DB");
var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
if (!string.IsNullOrEmpty(seedDb))
{
    if (!Path.IsPathRooted(seedDb))
        seedDb = Path.Combine(builder.Environment.ContentRootPath, seedDb);
    startupLog.LogInformation("Seed check: seedDb={Seed} exists={Exists} dbPath={DbPath}",
        seedDb, File.Exists(seedDb), dbPath);

    if (File.Exists(seedDb))
    {
        bool seed;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seed = !db.Guests.Any();
        }

        if (seed)
        {
            // Drop pooled connections so the SQLite file can be replaced cleanly.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
            File.Copy(seedDb, dbPath, overwrite: true);

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();

            using var verify = app.Services.CreateScope();
            var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            startupLog.LogInformation("Seeded database from bundle: guests={Guests}", vdb.Guests.Count());
        }
        else
        {
            startupLog.LogInformation("Database already has guests; seed skipped");
        }
    }
}

// Behind the Caddy reverse proxy the app only sees plain HTTP on the internal
// hop, so trust X-Forwarded-Proto/For to know the request arrived over HTTPS
// (this makes the auth cookie carry the Secure flag) and to log the real IP.
// Safe because the app port is bound to 127.0.0.1 and only Caddy can reach it.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseDefaultFiles();
app.UseAuthentication();
app.UseAuthorization();

// Hide admin pages: only the login screen and its shared assets are public.
// Everything else under /admin/ redirects to login for unauthenticated visitors.
app.Use(async (ctx, next) =>
{
    var p = ctx.Request.Path.Value ?? "";
    var isAdminPage = p.Equals("/admin", StringComparison.OrdinalIgnoreCase)
                      || p.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase);
    if (isAdminPage && ctx.User.Identity?.IsAuthenticated != true)
    {
        var file = p.TrimEnd('/').Split('/').Last().ToLowerInvariant();
        var isPublicFile = file is "login.html" or "app.css" or "app.js";
        if (!isPublicFile)
        {
            ctx.Response.Redirect("/admin/login.html");
            return;
        }
    }
    await next();
});

app.UseStaticFiles();

app.MapInvite();
app.MapAuth();
app.MapUploads();
app.MapAdminGuests();
app.MapAdminEvents();
app.MapAdminQuestions();
app.MapAdminAnswers();
app.MapAdminTemplate();
app.MapAdminTools();

app.MapGet("/", () => Results.Redirect("/admin/"));

app.Run();