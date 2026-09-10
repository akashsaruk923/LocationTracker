using System.ComponentModel.DataAnnotations;

namespace LocationTracker.Api.Dtos;

/// <summary>Payload sent by the browser page after the user allows location access.</summary>
public class LocationPingRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string ClientId { get; set; } = string.Empty;

    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    public double? AccuracyMeters { get; set; }
    public double? AltitudeMeters { get; set; }
    public double? AltitudeAccuracyMeters { get; set; }
    public double? Heading { get; set; }
    public double? SpeedMetersPerSecond { get; set; }

    /// <summary>Epoch milliseconds from the browser Geolocation position timestamp.</summary>
    public long? DeviceTimestampMs { get; set; }
}
