using System.ComponentModel.DataAnnotations;

namespace TaskPilot.Api.Models.Tasks;

/// <summary>Validates replacement task content; omitted completion state defaults to false.</summary>
public sealed class UpdateTaskRequest
{
    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    public bool IsCompleted { get; init; }
}
