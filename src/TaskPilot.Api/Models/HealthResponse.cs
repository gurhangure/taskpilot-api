namespace TaskPilot.Api.Models;

/// <summary>
/// Carries the API status returned by the health endpoint.
/// </summary>
/// <param name="Status">The status value indicating whether the API is responding.</param>
public sealed record HealthResponse(string Status);
