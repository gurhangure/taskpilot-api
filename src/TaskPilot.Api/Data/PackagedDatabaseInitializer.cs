using Microsoft.EntityFrameworkCore;

namespace TaskPilot.Api.Data;

/// <summary>Applies packaged database migrations during host startup before HTTP requests are accepted.</summary>
internal sealed class PackagedDatabaseInitializer : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PackagedDatabaseInitializer> _logger;

    /// <summary>Receives startup configuration, scoped persistence access, and diagnostic logging.</summary>
    public PackagedDatabaseInitializer(IConfiguration configuration, IServiceScopeFactory scopeFactory,
        ILogger<PackagedDatabaseInitializer> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Creates the packaged data directory and applies pending migrations, or does nothing in normal mode.</summary>
    /// <param name="cancellationToken">Cancels initialization when host startup is cancelled.</param>
    /// <returns>A task completing only after initialization succeeds.</returns>
    /// <exception cref="InvalidOperationException">Database initialization failed and the host must not start.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!PackagedDatabase.IsEnabled(_configuration))
            {
                return;
            }

            var path = PackagedDatabase.GetPath(_configuration);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TaskPilotDbContext>();
            await context.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Packaged database migrations completed successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            const string message = "TaskPilot database initialization failed. Check TaskPilot:DatabasePath, directory write permissions, available disk space, and SQLite migration compatibility. The API will not start; the database has not been reset.";
            _logger.LogCritical("{Message} Failure type: {FailureType}", message, exception.GetType().Name);
            throw new InvalidOperationException(message, exception);
        }
    }

    /// <summary>Completes shutdown because initialization holds no long-lived resources.</summary>
    /// <param name="cancellationToken">The host shutdown token.</param>
    /// <returns>An already completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
