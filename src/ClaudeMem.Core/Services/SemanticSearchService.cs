using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services.Embeddings;
using ClaudeMem.Core.Services.VectorStore;

namespace ClaudeMem.Core.Services;

/// <summary>
/// Unified semantic search service using pluggable embedding providers and vector stores.
/// </summary>
public class SemanticSearchService : IDisposable
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly IObservationRepository _observations;
    private readonly string _project;
    private readonly string _collectionName;
    private bool _initialized = false;

    public string EmbeddingProvider => _embeddings.Name;
    public string VectorStore => _vectorStore.Name;

    public SemanticSearchService(
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IObservationRepository observations,
        string project)
    {
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _observations = observations;
        _project = project;
        _collectionName = SanitizeCollectionName(project);
    }

    /// <summary>
    /// Initialize the service (create collection, etc.)
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _vectorStore.InitializeAsync(_collectionName, _embeddings.Dimension, ct);
        _initialized = true;
    }

    /// <summary>
    /// Index a single observation.
    /// </summary>
    public async Task IndexObservationAsync(Observation observation, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var content = ObservationToText(observation);
        var embedding = await _embeddings.GenerateEmbeddingAsync(content, ct);

        var record = new VectorRecord(
            $"obs_{observation.Id}",
            embedding,
            new Dictionary<string, object>
            {
                ["observation_id"] = observation.Id,
                ["session_id"] = observation.MemorySessionId,
                ["type"] = observation.Type.ToString(),
                ["title"] = observation.Title ?? "",
                ["project"] = _project,
                ["created_at_epoch"] = observation.CreatedAtEpoch,
                ["content_preview"] = content.Length > 200 ? content[..200] : content
            }
        );

        await _vectorStore.UpsertAsync(_collectionName, new[] { record }, ct);
    }

    /// <summary>
    /// Index multiple observations in batch.
    /// </summary>
    public async Task IndexObservationsAsync(IEnumerable<Observation> observations, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var obsList = observations.ToList();
        if (obsList.Count == 0) return;

        var texts = obsList.Select(ObservationToText).ToList();
        var embeddings = await _embeddings.GenerateEmbeddingsAsync(texts, ct);

        var records = obsList.Zip(embeddings, (obs, emb) => new VectorRecord(
            $"obs_{obs.Id}",
            emb,
            new Dictionary<string, object>
            {
                ["observation_id"] = obs.Id,
                ["session_id"] = obs.MemorySessionId,
                ["type"] = obs.Type.ToString(),
                ["title"] = obs.Title ?? "",
                ["project"] = _project,
                ["created_at_epoch"] = obs.CreatedAtEpoch,
                ["content_preview"] = texts[obsList.IndexOf(obs)] is var t && t.Length > 200 ? t[..200] : t
            }
        )).ToList();

        // Batch upsert in chunks of 100
        const int batchSize = 100;
        for (int i = 0; i < records.Count; i += batchSize)
        {
            var batch = records.Skip(i).Take(batchSize);
            await _vectorStore.UpsertAsync(_collectionName, batch, ct);
        }

        Console.WriteLine($"[SemanticSearch] Indexed {records.Count} observations using {_embeddings.Name}/{_vectorStore.Name}");
    }

    /// <summary>
    /// Search for similar observations.
    /// </summary>
    public async Task<List<SemanticSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        string? type = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var queryEmbedding = await _embeddings.GenerateEmbeddingAsync(query, ct);

        Dictionary<string, object>? filter = null;
        if (!string.IsNullOrEmpty(type))
        {
            filter = new Dictionary<string, object> { ["type"] = type };
        }

        var results = await _vectorStore.SearchAsync(_collectionName, queryEmbedding, limit, filter, ct);

        return results.Select(r => new SemanticSearchResult
        {
            ObservationId = r.Metadata.TryGetValue("observation_id", out var idObj) ? Convert.ToInt64(idObj) : 0,
            Score = r.Score,
            Type = r.Metadata.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "",
            Title = r.Metadata.TryGetValue("title", out var ti) ? ti?.ToString() ?? "" : "",
            Content = r.Metadata.TryGetValue("content_preview", out var c) ? c?.ToString() ?? "" : ""
        }).ToList();
    }

    /// <summary>
    /// Get service status.
    /// </summary>
    public async Task<SemanticSearchStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var embeddingsAvailable = await _embeddings.IsAvailableAsync(ct);
        var vectorStoreAvailable = await _vectorStore.IsAvailableAsync(ct);

        VectorCollectionInfo? collectionInfo = null;
        if (vectorStoreAvailable)
        {
            collectionInfo = await _vectorStore.GetCollectionInfoAsync(_collectionName, ct);
        }

        return new SemanticSearchStatus
        {
            Enabled = true,
            EmbeddingProvider = _embeddings.Name,
            EmbeddingAvailable = embeddingsAvailable,
            VectorStore = _vectorStore.Name,
            VectorStoreAvailable = vectorStoreAvailable,
            CollectionName = _collectionName,
            DocumentCount = collectionInfo?.Count ?? 0,
            Dimension = _embeddings.Dimension
        };
    }

    private static string ObservationToText(Observation obs)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(obs.Title))
            parts.Add($"Title: {obs.Title}");

        if (!string.IsNullOrEmpty(obs.Subtitle))
            parts.Add($"Subtitle: {obs.Subtitle}");

        if (!string.IsNullOrEmpty(obs.Narrative))
            parts.Add(obs.Narrative);

        if (obs.Facts.Count > 0)
            parts.Add($"Facts: {string.Join("; ", obs.Facts)}");

        if (obs.Concepts.Count > 0)
            parts.Add($"Concepts: {string.Join(", ", obs.Concepts)}");

        return string.Join("\n\n", parts);
    }

    private static string SanitizeCollectionName(string name)
    {
        var sanitized = new string(name
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace("-", "_")
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray());

        if (sanitized.Length < 3) sanitized = sanitized.PadRight(3, '_');
        if (sanitized.Length > 60) sanitized = sanitized[..60];

        return $"cm_{sanitized.ToLowerInvariant()}";
    }

    public void Dispose()
    {
        if (_vectorStore is IDisposable disposable)
            disposable.Dispose();
    }
}

public class SemanticSearchResult
{
    public long ObservationId { get; set; }
    public float Score { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

public class SemanticSearchStatus
{
    public bool Enabled { get; set; }
    public string EmbeddingProvider { get; set; } = "";
    public bool EmbeddingAvailable { get; set; }
    public string VectorStore { get; set; } = "";
    public bool VectorStoreAvailable { get; set; }
    public string CollectionName { get; set; } = "";
    public long DocumentCount { get; set; }
    public int Dimension { get; set; }
}
