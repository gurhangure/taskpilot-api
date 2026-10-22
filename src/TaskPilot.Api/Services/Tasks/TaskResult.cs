namespace TaskPilot.Api.Services.Tasks;

/// <summary>Provides an immutable snapshot of a saved task independent of persistence tracking.</summary>
/// <param name="Id">The server-generated task identity.</param>
/// <param name="Title">The task title.</param>
/// <param name="Description">The optional task description.</param>
/// <param name="IsCompleted">Whether the task is complete.</param>
/// <param name="CreatedAtUtc">The original creation time in UTC.</param>
public sealed record TaskResult(Guid Id, string Title, string? Description, bool IsCompleted, DateTime CreatedAtUtc);
