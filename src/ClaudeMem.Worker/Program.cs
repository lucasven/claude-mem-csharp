using ClaudeMem.Core.Data;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Worker.Endpoints;

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

var app = builder.Build();

// Map endpoints
app.MapHealthEndpoints();
app.MapObservationEndpoints();
app.MapSessionEndpoints();
app.MapSummaryEndpoints();
app.MapPromptEndpoints();

app.Run();
