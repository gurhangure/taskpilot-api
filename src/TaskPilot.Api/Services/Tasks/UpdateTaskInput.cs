namespace TaskPilot.Api.Services.Tasks;

/// <summary>Replaces the editable content and completion state of an existing task.</summary>
/// <param name="Title">A nonblank title of at most 200 characters.</param>
/// <param name="Description">Optional description of at most 2000 characters; null clears the existing value.</param>
/// <param name="IsCompleted">The replacement completion state.</param>
public sealed record UpdateTaskInput(string Title, string? Description, bool IsCompleted);
