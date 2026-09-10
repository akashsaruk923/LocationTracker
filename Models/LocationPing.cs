namespace LocationTracker.Api.Models;

/// <summary>
/// One recorded position, captured only after the visitor grants the browser
/// geolocation permission on the client page.
/// </summary>
public class LocationPing
{
    public long Id { get; set; }

    /// <summary>Random id kept in the browser's localStorage so repeat visits from the same device can be grouped.</summary>
    public string ClientId { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Accuracy of the lat/long in metres, as reported by the browser.</summary>
    public double? AccuracyMeters { get; set; }
    public double? AltitudeMeters { get; set; }
    public double? AltitudeAccuracyMeters { get; set; }
    public double? Heading { get; set; }
    public double? SpeedMetersPerSecond { get; set; }

    /// <summary>When the browser fixed the position (from the Geolocation timestamp).</summary>
    public DateTimeOffset? DeviceTimestampUtc { get; set; }

    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
