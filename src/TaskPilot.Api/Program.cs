using Microsoft.EntityFrameworkCore;
using TaskPilot.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<TaskPilotDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TaskPilot")
        ?? throw new InvalidOperationException("The TaskPilot connection string is required.")));

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

/// <summary>
/// Exposes the top-level entry point so integration tests can host the application with WebApplicationFactory.
/// </summary>
public partial class Program
{
}
