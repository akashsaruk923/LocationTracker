using LocationTracker.Api.Data;
using LocationTracker.Api.Dtos;
using LocationTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS) inject the port to bind on via $PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Connection string precedence: env var ConnectionStrings__Default > appsettings.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("No 'Default' connection string configured.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

// The public tunnel origin differs from the app origin, so allow the page to call the API.
builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

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

app.MapGet("/health", () => Results.Ok(new { status = "ok", timeUtc = DateTimeOffset.UtcNow }));

app.MapPost("/api/locations", async (LocationPingRequest req, HttpContext ctx, AppDbContext db) =>
{
    var validation = new List<string>();
    if (string.IsNullOrWhiteSpace(req.ClientId)) validation.Add("clientId is required");
    if (req.Latitude is < -90 or > 90) validation.Add("latitude out of range");
    if (req.Longitude is < -180 or > 180) validation.Add("longitude out of range");
    if (validation.Count > 0) return Results.ValidationProblem(
        new Dictionary<string, string[]> { ["payload"] = validation.ToArray() });

    var ping = new LocationPing
    {
        ClientId = req.ClientId.Trim(),
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        AccuracyMeters = req.AccuracyMeters,
        AltitudeMeters = req.AltitudeMeters,
        AltitudeAccuracyMeters = req.AltitudeAccuracyMeters,
        Heading = req.Heading,
        SpeedMetersPerSecond = req.SpeedMetersPerSecond,
        DeviceTimestampUtc = req.DeviceTimestampMs is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null,
        UserAgent = ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
            ? ua[..Math.Min(ua.Length, 512)]
            : null,
        IpAddress = ClientIp(ctx),
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    db.LocationPings.Add(ping);
    await db.SaveChangesAsync();

    return Results.Created($"/api/locations/{ping.Id}", new { ping.Id, ping.CreatedAtUtc });
});

app.Run();
