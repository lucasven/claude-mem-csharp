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

// Register FTS5 search (always available)
builder.Services.AddSingleton<FullTextSearchService>();

// Configure vector search if enabled
var vectorEnabled = Environment.GetEnvironmentVariable("CLAUDE_MEM_VECTOR_ENABLED") == "true";
var project = Environment.GetEnvironmentVariable("CLAUDE_MEM_PROJECT") ?? "default";

if (vectorEnabled)
{
    // Configure embedding provider
    var embeddingProvider = Environment.GetEnvironmentVariable("CLAUDE_MEM_EMBEDDING_PROVIDER") ?? "openai";
    var embeddingApiKey = Environment.GetEnvironmentVariable("CLAUDE_MEM_EMBEDDING_API_KEY") 
        ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    var embeddingModel = Environment.GetEnvironmentVariable("CLAUDE_MEM_EMBEDDING_MODEL") ?? "text-embedding-3-small";
    var embeddingBaseUrl = Environment.GetEnvironmentVariable("CLAUDE_MEM_EMBEDDING_BASE_URL");

    if (!string.IsNullOrEmpty(embeddingApiKey))
    {
        builder.Services.AddSingleton<IEmbeddingProvider>(sp => embeddingProvider switch
        {
            "openai" => new OpenAIEmbeddingProvider(embeddingApiKey, embeddingModel, embeddingBaseUrl),
            _ => new OpenAIEmbeddingProvider(embeddingApiKey, embeddingModel, embeddingBaseUrl)
        });

        // Configure vector store
        var vectorStore = Environment.GetEnvironmentVariable("CLAUDE_MEM_VECTOR_STORE") ?? "sqlite";
        builder.Services.AddSingleton<IVectorStore>(sp => vectorStore switch
        {
            "qdrant" => new QdrantVectorStore(),
            "sqlite" => new SqliteVectorStore(),
            _ => new SqliteVectorStore()
        });
    }
}

// Register hybrid search service
builder.Services.AddSingleton(sp =>
{
    var fts = sp.GetRequiredService<FullTextSearchService>();
    var observations = sp.GetRequiredService<IObservationRepository>();
    var embeddings = sp.GetService<IEmbeddingProvider>();
    var vectorStore = sp.GetService<IVectorStore>();

    return new HybridSearchService(
        fts,
        observations,
        project,
        embeddings,
        vectorStore
    );
});

var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaudeMemDatabase>();
    db.Migrate();
    Console.WriteLine("[Worker] Database migrations applied");
}

// Initialize hybrid search
var hybridSearch = app.Services.GetService<HybridSearchService>();
if (hybridSearch != null)
{
    _ = Task.Run(async () =>
    {
        try
        {
            await hybridSearch.InitializeAsync();
            Console.WriteLine($"[Worker] Hybrid search ready (mode: {hybridSearch.SearchMode})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker] Hybrid search init warning: {ex.Message}");
        }
    });
}

// Map endpoints
app.MapHealthEndpoints();
app.MapObservationEndpoints();
app.MapSessionEndpoints();
app.MapSearchEndpoints();
app.MapTimelineEndpoints();

Console.WriteLine($"[Worker] Starting on port {port}...");
app.Run();
