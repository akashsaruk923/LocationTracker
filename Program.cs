using LocationTracker.Api.Data;
using LocationTracker.Api.Dtos;
using LocationTracker.Api.Models;
using LocationTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Optional: honour a $PORT if a host injects one.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// SQL Server. From ConnectionStrings:Default (appsettings) or the
// ConnectionStrings__Default environment variable.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("No 'Default' connection string configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// The public tunnel origin differs from the app origin, so allow the page to call the API.
builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddHttpClient<GeoLookup>(c => c.Timeout = TimeSpan.FromSeconds(5));

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

// India Standard Time is a fixed UTC+5:30 (no DST), so a fixed offset is exact
// and avoids depending on the OS timezone database inside the container.
var istOffset = TimeSpan.FromHours(5.5);
DateTime IstNow() => DateTimeOffset.UtcNow.ToOffset(istOffset).DateTime;
DateTime? ToIst(DateTimeOffset? utc) => utc?.ToOffset(istOffset).DateTime;

app.MapGet("/health", () => Results.Ok(new { status = "ok", timeUtc = DateTimeOffset.UtcNow }));

// Called by the page the instant it loads. Saves an approximate, city-level
// location derived from the visitor's IP - no permission needed, so something
// is always recorded even if the visitor never allows precise location.
app.MapPost("/api/visit", async (VisitRequest req, HttpContext ctx, AppDbContext db, GeoLookup geo) =>
{
    var ip = ClientIp(ctx);
    var g = await geo.LookupByIpAsync(ip, ctx.RequestAborted);

    var ping = new LocationPing
    {
        Source = "ip",
        ClientId = string.IsNullOrWhiteSpace(req.ClientId) ? "unknown" : req.ClientId.Trim(),
        Latitude = g.Latitude,
        Longitude = g.Longitude,
        GoogleMapsUrl = MapsUrl(g.Latitude, g.Longitude),
        Village = g.Village,
        City = g.City,
        District = g.District,
        Region = g.Region,
        Country = g.Country,
        Postcode = g.Postcode,
        Address = g.Address,
        UserAgent = UserAgent(ctx),
        IpAddress = ip,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CreatedAtIst = IstNow(),
    };

    db.LocationPings.Add(ping);
    await db.SaveChangesAsync();

    return Results.Created($"/api/locations/{ping.Id}",
        new { ping.Id, ping.Source, ping.Latitude, ping.Longitude, ping.Village, ping.City, ping.Country, ping.GoogleMapsUrl, ping.CreatedAtIst });
});

// Called after the visitor allows the browser location prompt. Precise position.
app.MapPost("/api/locations", async (LocationPingRequest req, HttpContext ctx, AppDbContext db, GeoLookup geo) =>
{
    var validation = new List<string>();
    if (string.IsNullOrWhiteSpace(req.ClientId)) validation.Add("clientId is required");
    if (req.Latitude is < -90 or > 90) validation.Add("latitude out of range");
    if (req.Longitude is < -180 or > 180) validation.Add("longitude out of range");
    if (validation.Count > 0) return Results.ValidationProblem(
        new Dictionary<string, string[]> { ["payload"] = validation.ToArray() });

    var ip = ClientIp(ctx);
    // City/state from the ACTUAL coordinates - not the IP - so the "gps" row is correct.
    var g = await geo.ReverseGeocodeAsync(req.Latitude, req.Longitude, ctx.RequestAborted);
    if (!g.HasPlace)
        g = await geo.LookupByIpAsync(ip, ctx.RequestAborted);

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
        DeviceTimestampIst = req.DeviceTimestampMs is { } ms2
            ? ToIst(DateTimeOffset.FromUnixTimeMilliseconds(ms2))
            : null,
        Village = g.Village,
        City = g.City,
        District = g.District,
        Region = g.Region,
        Country = g.Country,
        Postcode = g.Postcode,
        Address = g.Address,
        UserAgent = UserAgent(ctx),
        IpAddress = ip,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        CreatedAtIst = IstNow(),
    };

    db.LocationPings.Add(ping);
    await db.SaveChangesAsync();

    return Results.Created($"/api/locations/{ping.Id}",
        new { ping.Id, ping.Village, ping.City, ping.District, ping.Region, ping.Address, ping.CreatedAtIst, ping.GoogleMapsUrl });
});

// Quick read-back to verify what's stored. Protected by the ADMIN_KEY env var.
app.MapGet("/api/recent", async (HttpContext ctx, AppDbContext db, IConfiguration cfg) =>
{
    var expected = cfg["ADMIN_KEY"];
    if (string.IsNullOrEmpty(expected) || ctx.Request.Query["key"] != expected)
        return Results.Unauthorized();

    var rows = await db.LocationPings
        .OrderByDescending(p => p.Id)
        .Take(50)
        .Select(p => new
        {
            p.Id, p.Source, p.ClientId,
            p.Latitude, p.Longitude, p.GoogleMapsUrl,
            p.Village, p.City, p.District, p.Region, p.Country, p.Postcode, p.Address,
            p.AccuracyMeters, p.IpAddress,
            p.CreatedAtIst, p.DeviceTimestampIst,
        })
        .ToListAsync();

    return Results.Ok(rows);
});

app.Run();
