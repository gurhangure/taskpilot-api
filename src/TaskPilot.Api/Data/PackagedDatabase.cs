using Microsoft.Data.Sqlite;

namespace TaskPilot.Api.Data;

/// <summary>Resolves packaged storage separately from the existing development connection string.</summary>
internal static class PackagedDatabase
{
    /// <summary>Selects explicit packaged mode without inferring it from environment or working directory.</summary>
    internal static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>("TaskPilot:Packaged");

    /// <summary>Resolves an absolute persistent file path under the current user's application data by default.</summary>
    /// <param name="configuration">Settings containing an optional absolute TaskPilot:DatabasePath.</param>
    /// <returns>The full SQLite database path.</returns>
    /// <exception cref="InvalidOperationException">The override is not absolute or no application-data directory is available.</exception>
    internal static string GetPath(IConfiguration configuration)
    {
        var configuredPath = configuration["TaskPilot:DatabasePath"];
        if (configuredPath is not null)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathFullyQualified(configuredPath))
            {
                throw new InvalidOperationException("TaskPilot:DatabasePath must be an absolute SQLite file path in packaged mode.");
            }

            return Path.GetFullPath(configuredPath);
        }

        var directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException("No user application-data directory is available. Set TaskPilot:DatabasePath to an absolute writable file path.");
        }

        return Path.Combine(directory, "TaskPilot", "taskpilot.db");
    }

    /// <summary>Uses packaged file storage only when enabled; otherwise preserves the development connection string.</summary>
    internal static string GetConnectionString(IConfiguration configuration) => IsEnabled(configuration)
        ? new SqliteConnectionStringBuilder { DataSource = GetPath(configuration) }.ToString()
        : configuration.GetConnectionString("TaskPilot")
            ?? throw new InvalidOperationException("The TaskPilot connection string is required.");
}
