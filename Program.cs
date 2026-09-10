using LocationTracker.Api.Data;
using LocationTracker.Api.Dtos;
using LocationTracker.Api.Models;
using LocationTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS) inject the port to bind on via $PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Connection string precedence: DATABASE_URL (Render/host convention) >
// ConnectionStrings:Default (appsettings or ConnectionStrings__Default env var).
var rawConnection = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("No database connection configured (set DATABASE_URL or ConnectionStrings:Default).");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(NormalizePostgres(rawConnection)));

// The public tunnel origin differs from the app origin, so allow the page to call the API.
builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddHttpClient<IpGeoLookup>(c => c.Timeout = TimeSpan.FromSeconds(4));

var app = builder.Build();

// Apply pending migrations on startup so a fresh database just works.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Real client IP when running behind the Cloudflare tunnel / Render proxy.
static string? ClientIp(HttpContext ctx) =>
    ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
    ?? ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
    ?? ctx.Connection.RemoteIpAddress?.ToString();

static string? UserAgent(HttpContext ctx) =>
    ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
        ? ua[..Math.Min(ua.Length, 512)]
        : null;

static string? MapsUrl(double? lat, double? lng) =>
    lat is { } a && lng is { } b
        ? $"https://www.google.com/maps?q={a.ToString(System.Globalization.CultureInfo.InvariantCulture)},{b.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        : null;

app.MapGet("/health", () => Results.Ok(new { status = "ok", timeUtc = DateTimeOffset.UtcNow }));

// Called by the page the instant it loads. Saves an approximate, city-level
// location derived from the visitor's IP - no permission needed, so something
// is always recorded even if the visitor never allows precise location.
app.MapPost("/api/visit", async (VisitRequest req, HttpContext ctx, AppDbContext db, IpGeoLookup geo) =>
{
    var ip = ClientIp(ctx);
    var g = await geo.LookupAsync(ip, ctx.RequestAborted);

    var ping = new LocationPing
    {
        Source = "ip",
        ClientId = string.IsNullOrWhiteSpace(req.ClientId) ? "unknown" : req.ClientId.Trim(),
        Latitude = g.Latitude,
        Longitude = g.Longitude,
        GoogleMapsUrl = MapsUrl(g.Latitude, g.Longitude),
        City = g.City,
        Region = g.Region,
        Country = g.Country,
        UserAgent = UserAgent(ctx),
        IpAddress = ip,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    db.LocationPings.Add(ping);
    await db.SaveChangesAsync();

    return Results.Created($"/api/locations/{ping.Id}",
        new { ping.Id, ping.Source, ping.Latitude, ping.Longitude, ping.City, ping.Country, ping.GoogleMapsUrl });
});

// Called after the visitor allows the browser location prompt. Precise position.
app.MapPost("/api/locations", async (LocationPingRequest req, HttpContext ctx, AppDbContext db, IpGeoLookup geo) =>
{
    var validation = new List<string>();
    if (string.IsNullOrWhiteSpace(req.ClientId)) validation.Add("clientId is required");
    if (req.Latitude is < -90 or > 90) validation.Add("latitude out of range");
    if (req.Longitude is < -180 or > 180) validation.Add("longitude out of range");
    if (validation.Count > 0) return Results.ValidationProblem(
        new Dictionary<string, string[]> { ["payload"] = validation.ToArray() });

    var ip = ClientIp(ctx);
    var g = await geo.LookupAsync(ip, ctx.RequestAborted);

    var ping = new LocationPing
    {
        Source = "gps",
        ClientId = req.ClientId.Trim(),
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        GoogleMapsUrl = MapsUrl(req.Latitude, req.Longitude),
        AccuracyMeters = req.AccuracyMeters,
        AltitudeMeters = req.AltitudeMeters,
        AltitudeAccuracyMeters = req.AltitudeAccuracyMeters,
        Heading = req.Heading,
        SpeedMetersPerSecond = req.SpeedMetersPerSecond,
        DeviceTimestampUtc = req.DeviceTimestampMs is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null,
        City = g.City,
        Region = g.Region,
        Country = g.Country,
        UserAgent = UserAgent(ctx),
        IpAddress = ip,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    db.LocationPings.Add(ping);
    await db.SaveChangesAsync();

    return Results.Created($"/api/locations/{ping.Id}", new { ping.Id, ping.CreatedAtUtc, ping.GoogleMapsUrl });
});

app.Run();

/// <summary>
/// Accepts either a libpq URL (postgres://user:pass@host:port/db, as Render's
/// DATABASE_URL provides) or an already-formed Npgsql key/value string.
/// </summary>
static string NormalizePostgres(string value)
{
    if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return value;

    var uri = new Uri(value);
    var userInfo = uri.UserInfo.Split(':', 2);
    var b = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
        Database = uri.AbsolutePath.TrimStart('/'),
        SslMode = Npgsql.SslMode.Require,
    };

    foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var kv = pair.Split('=', 2);
        if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<Npgsql.SslMode>(kv[1], true, out var parsed))
            b.SslMode = parsed;
    }

    return b.ConnectionString;
}
