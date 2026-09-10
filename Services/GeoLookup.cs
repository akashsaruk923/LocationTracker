using System.Globalization;
using System.Text.Json;

namespace LocationTracker.Api.Services;

public record GeoResult(double? Latitude, double? Longitude, string? City, string? Region, string? Country)
{
    public static readonly GeoResult Empty = new(null, null, null, null, null);
    public bool HasPlace => !string.IsNullOrWhiteSpace(City) || !string.IsNullOrWhiteSpace(Country);
}

/// <summary>
/// Free, key-less geo lookups. Best-effort - any failure returns <see cref="GeoResult.Empty"/>.
/// <list type="bullet">
/// <item><see cref="LookupByIpAsync"/> - approximate city from an IP (ipwho.is). Coarse: often the ISP's city.</item>
/// <item><see cref="ReverseGeocodeAsync"/> - the real city/state for precise GPS coordinates (BigDataCloud api-bdc.io).</item>
/// </list>
/// </summary>
public class GeoLookup(HttpClient http, ILogger<GeoLookup> logger)
{
    public async Task<GeoResult> LookupByIpAsync(string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IsPublicIp(ip))
            return GeoResult.Empty;

        try
        {
            using var res = await http.GetAsync($"https://ipwho.is/{Uri.EscapeDataString(ip)}", ct);
            if (!res.IsSuccessStatusCode) return GeoResult.Empty;

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                return GeoResult.Empty;

            return new GeoResult(
                Latitude: GetDouble(root, "latitude"),
                Longitude: GetDouble(root, "longitude"),
                City: GetString(root, "city"),
                Region: GetString(root, "region"),
                Country: GetString(root, "country"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IP geolocation lookup failed for {Ip}", ip);
            return GeoResult.Empty;
        }
    }

    /// <summary>Turns exact coordinates into a place name - this is what makes the "gps" row's city correct.</summary>
    public async Task<GeoResult> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct = default)
    {
        var url = "https://api-bdc.io/data/reverse-geocode-client"
            + $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}"
            + $"&longitude={lng.ToString(CultureInfo.InvariantCulture)}"
            + "&localityLanguage=en";
        try
        {
            using var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return GeoResult.Empty;

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var city = GetString(root, "city");
            if (string.IsNullOrWhiteSpace(city)) city = GetString(root, "locality");

            return new GeoResult(
                Latitude: lat,
                Longitude: lng,
                City: city,
                Region: GetString(root, "principalSubdivision"),
                Country: GetString(root, "countryName"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reverse geocode failed for {Lat},{Lng}", lat, lng);
            return GeoResult.Empty;
        }
    }

    private static double? GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? d : null;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s : null;

    private static bool IsPublicIp(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var addr)) return false;
        if (System.Net.IPAddress.IsLoopback(addr)) return false;
        var b = addr.GetAddressBytes();
        if (b.Length == 4)
        {
            if (b[0] == 10) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 169 && b[1] == 254) return false;
        }
        return true;
    }
}
