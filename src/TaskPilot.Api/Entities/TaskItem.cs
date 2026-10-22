namespace TaskPilot.Api.Entities;

/// <summary>
/// Stores a task's content, completion state, and server-assigned identity and creation time.
/// </summary>
public sealed class TaskItem
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public bool IsCompleted { get; set; }

    /// <summary>
    /// Gets or sets the creation time, which callers must supply in UTC.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}
