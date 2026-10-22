using Microsoft.EntityFrameworkCore;
using TaskPilot.Api.Data;
using TaskPilot.Api.Services.Tasks;

#if TASKPILOT_DISTRIBUTION
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
// Distributed binaries always use safe per-user storage and automatic migrations.
builder.Configuration["TaskPilot:Packaged"] = "true";
#else
var builder = WebApplication.CreateBuilder(args);
#endif

builder.Services.AddControllers();
builder.Services.AddHostedService<PackagedDatabaseInitializer>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddDbContext<TaskPilotDbContext>(options =>
    options.UseSqlite(PackagedDatabase.GetConnectionString(builder.Configuration)));

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
