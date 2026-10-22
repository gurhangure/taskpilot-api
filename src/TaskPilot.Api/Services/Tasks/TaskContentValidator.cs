namespace TaskPilot.Api.Services.Tasks;

/// <summary>Enforces task content rules without persistence, transport, or other external dependencies.</summary>
internal static class TaskContentValidator
{
    /// <summary>Rejects blank titles and oversized content without normalizing caller-supplied text.</summary>
    /// <param name="title">A nonblank title of at most 200 UTF-16 code units.</param>
    /// <param name="description">Optional description of at most 2000 UTF-16 code units.</param>
    /// <exception cref="ArgumentException">The title is null or blank, or either field exceeds its limit. Title errors take precedence.</exception>
    internal static void Validate(string? title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new ArgumentException("A nonblank title of at most 200 characters is required.", nameof(title));
        }

        if (description?.Length > 2000)
        {
            throw new ArgumentException("The description must contain at most 2000 characters.", nameof(description));
        }
    }
}
