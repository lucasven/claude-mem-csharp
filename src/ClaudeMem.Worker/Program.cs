using ClaudeMem.Core.Data;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services;
using ClaudeMem.Core.Services.Embeddings;
using ClaudeMem.Core.Services.VectorStore;
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

// Register semantic search services
var searchEnabled = Environment.GetEnvironmentVariable("CLAUDE_MEM_SEARCH_ENABLED") != "false";
if (searchEnabled)
{
    // Configure embedding provider (ollama, openai)
    var embeddingProvider = Environment.GetEnvironmentVariable("CLAUDE_MEM_EMBEDDING_PROVIDER") ?? "ollama";
    builder.Services.AddSingleton<IEmbeddingProvider>(sp => embeddingProvider switch
    {
        "ollama" => new OllamaEmbeddingProvider(),
        _ => new OllamaEmbeddingProvider() // default to ollama
    });

    // Configure vector store (sqlite, qdrant)
    var vectorStore = Environment.GetEnvironmentVariable("CLAUDE_MEM_VECTOR_STORE") ?? "sqlite";
    builder.Services.AddSingleton<IVectorStore>(sp => vectorStore switch
    {
        "qdrant" => new QdrantVectorStore(),
        "sqlite" => new SqliteVectorStore(),
        _ => new SqliteVectorStore() // default to sqlite
    });

    // Register semantic search service
    builder.Services.AddSingleton(sp =>
    {
        var embeddings = sp.GetRequiredService<IEmbeddingProvider>();
        var store = sp.GetRequiredService<IVectorStore>();
        var observations = sp.GetRequiredService<IObservationRepository>();
        var project = Environment.GetEnvironmentVariable("CLAUDE_MEM_PROJECT") ?? "default";
        return new SemanticSearchService(embeddings, store, observations, project);
    });
}

var app = builder.Build();

// Initialize semantic search if enabled
if (searchEnabled)
{
    var searchService = app.Services.GetService<SemanticSearchService>();
    if (searchService != null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await searchService.InitializeAsync();
                Console.WriteLine($"[Worker] Semantic search ready ({searchService.EmbeddingProvider}/{searchService.VectorStore})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker] Semantic search init failed: {ex.Message}");
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
