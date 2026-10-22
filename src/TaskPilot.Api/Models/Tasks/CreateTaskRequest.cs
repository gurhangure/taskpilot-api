using System.ComponentModel.DataAnnotations;

namespace TaskPilot.Api.Models.Tasks;

/// <summary>Validates caller-editable content before creating a task.</summary>
public sealed class CreateTaskRequest
{
    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }
}
