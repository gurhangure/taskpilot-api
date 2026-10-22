namespace TaskPilot.Api.Services.Tasks;

/// <summary>Supplies caller-editable content for a new, incomplete task.</summary>
/// <param name="Title">A nonblank title of at most 200 characters.</param>
/// <param name="Description">Optional description of at most 2000 characters.</param>
public sealed record CreateTaskInput(string Title, string? Description = null);
