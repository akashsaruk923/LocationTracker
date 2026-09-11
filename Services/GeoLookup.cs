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
    public bool HasVillage => !string.IsNullOrWhiteSpace(Village);
}

/// <summary>
/// Free, key-less geo lookups. Best-effort - any failure returns <see cref="GeoResult.Empty"/>.
/// <list type="bullet">
/// <item><see cref="LookupByIpAsync"/> - approximate city from an IP (ipwho.is). Coarse: often the ISP's city.</item>
/// <item><see cref="ReverseGeocodeAsync"/> - village/suburb-level address for precise GPS coordinates.
/// Tries OpenStreetMap/Nominatim, then Photon (komoot), then BigDataCloud - cloud-hosted apps sometimes get
/// blocked/rate-limited by Nominatim's public server, so the chain keeps trying until one answers.</item>
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

    /// <summary>Exact coordinates -> place name. Tries providers in order until one has a village.</summary>
    public async Task<GeoResult> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct = default)
    {
        var best = GeoResult.Empty;

        var nominatim = await NominatimAsync(lat, lng, ct);
        if (nominatim.HasVillage) return nominatim;
        if (nominatim.HasPlace) best = nominatim;

        var photon = await PhotonAsync(lat, lng, ct);
        if (photon.HasVillage) return photon;
        if (photon.HasPlace && !best.HasPlace) best = photon;

        var bdc = await BigDataCloudAsync(lat, lng, ct);
        if (bdc.HasVillage) return bdc;
        if (bdc.HasPlace && !best.HasPlace) best = bdc;

        return best;
    }

    private async Task<GeoResult> NominatimAsync(double lat, double lng, CancellationToken ct)
    {
        var url = "https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=18&addressdetails=1"
            + $"&lat={lat.ToString(CultureInfo.InvariantCulture)}"
            + $"&lon={lng.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Nominatim's usage policy requires a descriptive User-Agent and/or Referer.
            req.Headers.UserAgent.ParseAdd("LocationTracker/1.0 (+https://github.com/akashsaruk923/LocationTracker)");
            req.Headers.Referrer = new Uri("https://github.com/akashsaruk923/LocationTracker");
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Nominatim returned {Status} for {Lat},{Lng}", (int)res.StatusCode, lat, lng);
                return GeoResult.Empty;
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (!root.TryGetProperty("address", out var a) || a.ValueKind != JsonValueKind.Object)
                return GeoResult.Empty;

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

    /// <summary>Photon (Komoot) - also OpenStreetMap data, a separate free public server.</summary>
    private async Task<GeoResult> PhotonAsync(double lat, double lng, CancellationToken ct)
    {
        var url = "https://photon.komoot.io/reverse"
            + $"?lat={lat.ToString(CultureInfo.InvariantCulture)}"
            + $"&lon={lng.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            using var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Photon returned {Status} for {Lat},{Lng}", (int)res.StatusCode, lat, lng);
                return GeoResult.Empty;
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("features", out var features) ||
                features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
                return GeoResult.Empty;

            var p = features[0].GetProperty("properties");
            var village = GetString(p, "locality") ?? GetString(p, "district") ?? GetString(p, "name");
            var city = GetString(p, "city") ?? GetString(p, "county");

            var addressParts = new[] { GetString(p, "name"), village, city, GetString(p, "state"), GetString(p, "country") }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();

            return new GeoResult(
                Latitude: lat, Longitude: lng,
                Village: village,
                City: city,
                District: GetString(p, "county"),
                Region: GetString(p, "state"),
                Country: GetString(p, "country"),
                Postcode: GetString(p, "postcode"),
                Address: string.Join(", ", addressParts));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Photon reverse geocode failed for {Lat},{Lng}", lat, lng);
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
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("BigDataCloud returned {Status} for {Lat},{Lng}", (int)res.StatusCode, lat, lng);
                return GeoResult.Empty;
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var locality = GetString(root, "locality");
            var city = GetString(root, "city") ?? locality;

            // "locality" is often the same as "city" (no village-level detail). Look for a
            // more specific name in the administrative hierarchy as a last resort.
            var village = locality;
            if (string.IsNullOrWhiteSpace(village) || string.Equals(village, city, StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("localityInfo", out var li) &&
                    li.TryGetProperty("administrative", out var admin) && admin.ValueKind == JsonValueKind.Array)
                {
                    // Highest "order" = most specific administrative unit.
                    string? candidate = null;
                    var bestOrder = -1;
                    foreach (var item in admin.EnumerateArray())
                    {
                        var name = GetString(item, "name");
                        if (name is null || string.Equals(name, city, StringComparison.OrdinalIgnoreCase)) continue;
                        var order = item.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number
                            ? o.GetInt32() : -1;
                        if (order > bestOrder) { bestOrder = order; candidate = name; }
                    }
                    village = candidate ?? village;
                }
            }

            return new GeoResult(
                Latitude: lat, Longitude: lng,
                Village: village,
                City: city,
                District: null,
                Region: GetString(root, "principalSubdivision"),
                Country: GetString(root, "countryName"),
                Postcode: GetString(root, "postcode"),
                Address: string.Join(", ", new[] { village, city, GetString(root, "principalSubdivision"), GetString(root, "countryName") }
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()));
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
