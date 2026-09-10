using System.Globalization;
using System.Text.Json;

namespace LocationTracker.Api.Services;

public record GeoResult(
    double? Latitude, double? Longitude,
    string? Village, string? City, string? District,
    string? Region, string? Country, string? Postcode, string? Address)
{
    public static readonly GeoResult Empty = new(null, null, null, null, null, null, null, null, null);
    public bool HasPlace =>
        !string.IsNullOrWhiteSpace(Village) || !string.IsNullOrWhiteSpace(City) ||
        !string.IsNullOrWhiteSpace(Country);
}

/// <summary>
/// Free, key-less geo lookups. Best-effort - any failure returns <see cref="GeoResult.Empty"/>.
/// <list type="bullet">
/// <item><see cref="LookupByIpAsync"/> - approximate city from an IP (ipwho.is). Coarse: often the ISP's city.</item>
/// <item><see cref="ReverseGeocodeAsync"/> - village/suburb-level address for precise GPS
/// coordinates (OpenStreetMap / Nominatim), with BigDataCloud as a fallback.</item>
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

            return GeoResult.Empty with
            {
                Latitude = GetDouble(root, "latitude"),
                Longitude = GetDouble(root, "longitude"),
                City = GetString(root, "city"),
                Region = GetString(root, "region"),
                Country = GetString(root, "country"),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IP geolocation lookup failed for {Ip}", ip);
            return GeoResult.Empty;
        }
    }

    /// <summary>Exact coordinates -> place name. Populates Village in rural areas.</summary>
    public async Task<GeoResult> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct = default)
    {
        var osm = await NominatimAsync(lat, lng, ct);
        if (osm.HasPlace) return osm;
        return await BigDataCloudAsync(lat, lng, ct);
    }

    private async Task<GeoResult> NominatimAsync(double lat, double lng, CancellationToken ct)
    {
        var url = "https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=18&addressdetails=1"
            + $"&lat={lat.ToString(CultureInfo.InvariantCulture)}"
            + $"&lon={lng.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Nominatim requires a descriptive User-Agent identifying the app.
            req.Headers.UserAgent.ParseAdd("LocationTracker/1.0 (+https://github.com/akashsaruk923/LocationTracker)");
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return GeoResult.Empty;

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (!root.TryGetProperty("address", out var a) || a.ValueKind != JsonValueKind.Object)
                return GeoResult.Empty;

            // Most-local settlement name, from village down through town/suburb.
            var village = GetString(a, "village") ?? GetString(a, "hamlet")
                ?? GetString(a, "town") ?? GetString(a, "suburb")
                ?? GetString(a, "neighbourhood") ?? GetString(a, "locality");
            var city = GetString(a, "city") ?? GetString(a, "town")
                ?? GetString(a, "municipality") ?? GetString(a, "county");
            var district = GetString(a, "state_district") ?? GetString(a, "county")
                ?? GetString(a, "district");

            return new GeoResult(
                Latitude: lat, Longitude: lng,
                Village: village,
                City: city,
                District: district,
                Region: GetString(a, "state") ?? GetString(a, "region"),
                Country: GetString(a, "country"),
                Postcode: GetString(a, "postcode"),
                Address: GetString(root, "display_name"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nominatim reverse geocode failed for {Lat},{Lng}", lat, lng);
            return GeoResult.Empty;
        }
    }

    private async Task<GeoResult> BigDataCloudAsync(double lat, double lng, CancellationToken ct)
    {
        var url = "https://api-bdc.io/data/reverse-geocode-client"
            + $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}"
            + $"&longitude={lng.ToString(CultureInfo.InvariantCulture)}&localityLanguage=en";
        try
        {
            using var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return GeoResult.Empty;

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var locality = GetString(root, "locality");
            var city = GetString(root, "city") ?? locality;

            return new GeoResult(
                Latitude: lat, Longitude: lng,
                Village: locality,
                City: city,
                District: null,
                Region: GetString(root, "principalSubdivision"),
                Country: GetString(root, "countryName"),
                Postcode: null,
                Address: null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BigDataCloud reverse geocode failed for {Lat},{Lng}", lat, lng);
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
