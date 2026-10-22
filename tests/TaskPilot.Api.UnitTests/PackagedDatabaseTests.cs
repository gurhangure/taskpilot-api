using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TaskPilot.Api.Data;
using Xunit;

namespace TaskPilot.Api.UnitTests;

/// <summary>Checks packaged storage configuration without accessing a database or creating directories.</summary>
public sealed class PackagedDatabaseTests
{
    /// <summary>Uses the OS-provided application-data directory rather than the executable or working directory.</summary>
    [Fact]
    public void DefaultPath_UsesUserApplicationData()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "TaskPilot", "taskpilot.db"), PackagedDatabase.GetPath(configuration));
        Assert.False(PackagedDatabase.IsEnabled(configuration));
    }

    /// <summary>Rejects relative and empty overrides so packaged storage cannot silently depend on the working directory.</summary>
    /// <param name="path">An invalid configured database path.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("tasks.db")]
    public void InvalidOverride_IsRejected(string path)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TaskPilot:DatabasePath"] = path
        }).Build();
        Assert.Throws<InvalidOperationException>(() => PackagedDatabase.GetPath(configuration));
    }

    /// <summary>Preserves normal connection strings and uses the explicit packaged path only when enabled.</summary>
    [Fact]
    public void ConnectionString_SeparatesPackagedAndDevelopmentStorage()
    {
        var path = Path.Combine(Path.GetTempPath(), "taskpilot-unit", "tasks.db");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TaskPilot"] = "Data Source=development.db",
            ["TaskPilot:DatabasePath"] = path
        }).Build();
        Assert.Equal("Data Source=development.db", PackagedDatabase.GetConnectionString(configuration));
        configuration["TaskPilot:Packaged"] = "true";
        Assert.Equal(path, new SqliteConnectionStringBuilder(PackagedDatabase.GetConnectionString(configuration)).DataSource);
    }
}
