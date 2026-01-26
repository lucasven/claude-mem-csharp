using ClaudeMem.Core.Data;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Worker.Endpoints;
using ClaudeMem.Worker.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Configure port from settings or default
var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37778";
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

// Register services
builder.Services.AddSingleton<ClaudeMemDatabase>();
builder.Services.AddSingleton<IObservationRepository, ObservationRepository>();
builder.Services.AddSingleton<ISessionRepository, SessionRepository>();
builder.Services.AddSingleton<ISummaryRepository, SummaryRepository>();
builder.Services.AddSingleton<IUserPromptRepository, UserPromptRepository>();
builder.Services.AddSingleton<SSEBroadcaster>();

var app = builder.Build();

// Serve static files from ui directory
var uiPath = Path.Combine(AppContext.BaseDirectory, "ui");
if (Directory.Exists(uiPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uiPath),
        RequestPath = ""
    });
}

// Map endpoints
app.MapHealthEndpoints();
app.MapObservationEndpoints();
app.MapSessionEndpoints();
app.MapSummaryEndpoints();
app.MapPromptEndpoints();
app.MapMetadataEndpoints();
app.MapViewerEndpoints();

app.Run();
