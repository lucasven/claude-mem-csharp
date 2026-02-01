using ClaudeMem.Core.Data;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services;
using ClaudeMem.Worker.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Configure port from settings or default
var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777";
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

// Register database and repositories
builder.Services.AddSingleton<ClaudeMemDatabase>();
builder.Services.AddSingleton<IObservationRepository, ObservationRepository>();
builder.Services.AddSingleton<ISessionRepository, SessionRepository>();
builder.Services.AddSingleton<ISummaryRepository, SummaryRepository>();

// Register Chroma services for semantic search
var chromaEnabled = Environment.GetEnvironmentVariable("CLAUDE_MEM_CHROMA_ENABLED") != "false";
if (chromaEnabled)
{
    var pythonVersion = Environment.GetEnvironmentVariable("CLAUDE_MEM_PYTHON_VERSION") ?? "3.12";
    builder.Services.AddSingleton(sp => new ChromaService(pythonVersion: pythonVersion));
    builder.Services.AddSingleton(sp =>
    {
        var chroma = sp.GetRequiredService<ChromaService>();
        var observations = sp.GetRequiredService<IObservationRepository>();
        var project = Environment.GetEnvironmentVariable("CLAUDE_MEM_PROJECT") ?? "default";
        return new ChromaSyncService(chroma, observations, project);
    });
}

var app = builder.Build();

// Initialize Chroma if enabled
if (chromaEnabled)
{
    var chroma = app.Services.GetService<ChromaService>();
    if (chroma != null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await chroma.StartAsync();
                Console.WriteLine("[Worker] ChromaDB connected for semantic search");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker] ChromaDB failed to start: {ex.Message}");
                Console.WriteLine("[Worker] Semantic search will be unavailable");
            }
        });
    }
}

// Map endpoints
app.MapHealthEndpoints();
app.MapObservationEndpoints();
app.MapSessionEndpoints();
app.MapSearchEndpoints();

app.Run();
