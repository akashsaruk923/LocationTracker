using System.Text.Json;

namespace LocationTracker.Api.Services;

public record IpGeoResult(double? Latitude, double? Longitude, string? City, string? Region, string? Country);

/// <summary>
/// Approximate, city-level location from an IP address using the free, key-less
/// ipwho.is service. Best-effort: any failure returns an empty result.
/// </summary>
public class IpGeoLookup(HttpClient http, ILogger<IpGeoLookup> logger)
{
    public async Task<IpGeoResult> LookupAsync(string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IsPublicIp(ip))
            return new IpGeoResult(null, null, null, null, null);

        try
        {
            using var res = await http.GetAsync($"https://ipwho.is/{Uri.EscapeDataString(ip)}", ct);
            if (!res.IsSuccessStatusCode) return Empty();

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var ok) && !ok.GetBoolean())
                return Empty();

            return new IpGeoResult(
                Latitude: GetDouble(root, "latitude"),
                Longitude: GetDouble(root, "longitude"),
                City: GetString(root, "city"),
                Region: GetString(root, "region"),
                Country: GetString(root, "country"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IP geolocation lookup failed for {Ip}", ip);
            return Empty();
        }
    }

    private static IpGeoResult Empty() => new(null, null, null, null, null);

    private static double? GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? d : null;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

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
