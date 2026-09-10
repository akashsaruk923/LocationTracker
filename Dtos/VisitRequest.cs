namespace LocationTracker.Api.Dtos;

/// <summary>Sent by the page on load, before any permission prompt.</summary>
public class VisitRequest
{
    public string? ClientId { get; set; }
}
