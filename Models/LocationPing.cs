namespace LocationTracker.Api.Models;

/// <summary>
/// One recorded position.
/// <para><c>Source = "ip"</c>: written automatically the moment the page loads,
/// from a lookup of the visitor's IP address (city-level, approximate).</para>
/// <para><c>Source = "gps"</c>: the precise device position, written only after
/// the visitor allows the browser location prompt.</para>
/// </summary>
public class LocationPing
{
    public long Id { get; set; }

    /// <summary>"ip" or "gps".</summary>
    public string Source { get; set; } = "gps";

    /// <summary>Random id kept in the browser's localStorage so repeat visits from the same device can be grouped.</summary>
    public string ClientId { get; set; } = string.Empty;

    // Nullable: an IP lookup can fail, and a denied GPS prompt leaves these empty.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Ready-to-open Google Maps link for the coordinates, when known.</summary>
    public string? GoogleMapsUrl { get; set; }

    /// <summary>Accuracy of the lat/long in metres (GPS only, as reported by the browser).</summary>
    public double? AccuracyMeters { get; set; }
    public double? AltitudeMeters { get; set; }
    public double? AltitudeAccuracyMeters { get; set; }
    public double? Heading { get; set; }
    public double? SpeedMetersPerSecond { get; set; }

    /// <summary>When the browser fixed the position (from the Geolocation timestamp), UTC.</summary>
    public DateTimeOffset? DeviceTimestampUtc { get; set; }

    /// <summary>Same moment as <see cref="DeviceTimestampUtc"/>, as India Standard Time (UTC+5:30) wall-clock.</summary>
    public DateTime? DeviceTimestampIst { get; set; }

    // Filled from the IP lookup (present on "ip" rows, sometimes on "gps" rows too).
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }

    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>Server insert time, UTC (the true instant).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Server insert time as India Standard Time (UTC+5:30) wall-clock - the column to read for local time.</summary>
    public DateTime CreatedAtIst { get; set; }
}
