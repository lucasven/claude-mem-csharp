using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services.Embeddings;
using ClaudeMem.Core.Services.VectorStore;

namespace ClaudeMem.Core.Services;

/// <summary>
/// Hybrid search combining FTS5 keyword search with vector semantic search.
/// Uses weighted scoring to merge results from both retrieval methods.
/// </summary>
public class HybridSearchService : IDisposable
{
    private readonly FullTextSearchService _fts;
    private readonly IEmbeddingProvider? _embeddings;
    private readonly IVectorStore? _vectorStore;
    private readonly IObservationRepository _observations;
    private readonly string _project;
    private readonly string _collectionName;
    
    private readonly float _vectorWeight;
    private readonly float _textWeight;
    private readonly int _candidateMultiplier;
    
    private bool _initialized = false;

    public bool VectorSearchAvailable => _embeddings != null && _vectorStore != null;
    public string SearchMode => VectorSearchAvailable ? "hybrid" : "fts5";

    public HybridSearchService(
        FullTextSearchService fts,
        IObservationRepository observations,
        string project,
        IEmbeddingProvider? embeddings = null,
        IVectorStore? vectorStore = null,
        float vectorWeight = 0.7f,
        float textWeight = 0.3f,
        int candidateMultiplier = 4)
    {
        _fts = fts;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _observations = observations;
        _project = project;
        _collectionName = SanitizeCollectionName(project);
        
        // Normalize weights
        var totalWeight = vectorWeight + textWeight;
        _vectorWeight = vectorWeight / totalWeight;
        _textWeight = textWeight / totalWeight;
        _candidateMultiplier = candidateMultiplier;
    }

    /// <summary>
    /// Initialize vector store if available.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized || _vectorStore == null || _embeddings == null)
            return;

        await _vectorStore.InitializeAsync(_collectionName, _embeddings.Dimension, ct);
        _initialized = true;
    }

    /// <summary>
    /// Perform hybrid search combining FTS5 and vector results.
    /// </summary>
    public async Task<List<HybridSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        string? type = null,
        CancellationToken ct = default)
    {
        var candidateLimit = limit * _candidateMultiplier;
        var candidates = new Dictionary<long, HybridSearchResult>();

        // 1. FTS5 keyword search (always available)
        var ftsResults = _fts.SearchObservations(query, candidateLimit, type, _project);
        foreach (var fts in ftsResults)
        {
            candidates[fts.Id] = new HybridSearchResult
            {
                ObservationId = fts.Id,
                Title = fts.Title,
                Type = fts.Type,
                CreatedAtEpoch = fts.CreatedAtEpoch,
                FtsScore = fts.NormalizedScore,
                Snippet = fts.Snippet
            };
        }

        // 2. Vector semantic search (if available)
        if (VectorSearchAvailable && _initialized)
        {
            try
            {
                var queryEmbedding = await _embeddings!.GenerateEmbeddingAsync(query, ct);
                
                Dictionary<string, object>? filter = null;
                if (!string.IsNullOrEmpty(type))
                    filter = new Dictionary<string, object> { ["type"] = type };

                var vectorResults = await _vectorStore!.SearchAsync(
                    _collectionName, 
                    queryEmbedding, 
                    candidateLimit, 
                    filter, 
                    ct);

                foreach (var vec in vectorResults)
                {
                    var obsId = vec.Metadata.TryGetValue("observation_id", out var idObj) 
                        ? Convert.ToInt64(idObj) 
                        : 0;

                    if (obsId == 0) continue;

                    if (candidates.TryGetValue(obsId, out var existing))
                    {
                        existing.VectorScore = vec.Score;
                    }
                    else
                    {
                        candidates[obsId] = new HybridSearchResult
                        {
                            ObservationId = obsId,
                            Title = vec.Metadata.TryGetValue("title", out var t) ? t?.ToString() ?? "" : "",
                            Type = vec.Metadata.TryGetValue("type", out var ty) ? ty?.ToString() ?? "" : "",
                            CreatedAtEpoch = vec.Metadata.TryGetValue("created_at_epoch", out var e) ? Convert.ToInt64(e) : 0,
                            VectorScore = vec.Score,
                            Snippet = vec.Metadata.TryGetValue("content_preview", out var c) ? c?.ToString() ?? "" : ""
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HybridSearch] Vector search failed, using FTS only: {ex.Message}");
            }
        }

        // 3. Compute weighted scores and sort
        var results = candidates.Values
            .Select(r =>
            {
                r.HybridScore = (_vectorWeight * r.VectorScore) + (_textWeight * r.FtsScore);
                return r;
            })
            .OrderByDescending(r => r.HybridScore)
            .Take(limit)
            .ToList();

        return results;
    }

    /// <summary>
    /// Index a single observation in the vector store.
    /// </summary>
    public async Task IndexObservationAsync(Observation observation, CancellationToken ct = default)
    {
        if (!VectorSearchAvailable)
            return;

        await InitializeAsync(ct);

        var content = ObservationToText(observation);
        var embedding = await _embeddings!.GenerateEmbeddingAsync(content, ct);

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

        await _vectorStore!.UpsertAsync(_collectionName, new[] { record }, ct);
    }

    /// <summary>
    /// Batch index multiple observations.
    /// </summary>
    public async Task IndexObservationsAsync(IEnumerable<Observation> observations, CancellationToken ct = default)
    {
        if (!VectorSearchAvailable)
            return;

        await InitializeAsync(ct);

        var obsList = observations.ToList();
        if (obsList.Count == 0) return;

        var texts = obsList.Select(ObservationToText).ToList();
        var embeddings = await _embeddings!.GenerateEmbeddingsAsync(texts, ct);

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

        const int batchSize = 100;
        for (int i = 0; i < records.Count; i += batchSize)
        {
            var batch = records.Skip(i).Take(batchSize);
            await _vectorStore!.UpsertAsync(_collectionName, batch, ct);
        }

        Console.WriteLine($"[HybridSearch] Indexed {records.Count} observations");
    }

    /// <summary>
    /// Get timeline context around an observation.
    /// </summary>
    public TimelineResult GetTimeline(long anchorId, int depthBefore = 3, int depthAfter = 3)
    {
        return _fts.GetTimeline(anchorId, depthBefore, depthAfter, _project);
    }

    /// <summary>
    /// Get timeline by query (finds anchor first).
    /// </summary>
    public TimelineResult GetTimelineByQuery(string query, int depthBefore = 3, int depthAfter = 3)
    {
        var anchorId = _fts.FindAnchorByQuery(query, _project);
        if (!anchorId.HasValue)
            return new TimelineResult { Found = false };

        return GetTimeline(anchorId.Value, depthBefore, depthAfter);
    }

    /// <summary>
    /// Get service status.
    /// </summary>
    public async Task<HybridSearchStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var status = new HybridSearchStatus
        {
            Mode = SearchMode,
            FtsAvailable = true, // Always available with SQLite
            VectorAvailable = VectorSearchAvailable
        };

        if (VectorSearchAvailable)
        {
            status.EmbeddingProvider = _embeddings!.Name;
            status.VectorStore = _vectorStore!.Name;
            status.EmbeddingAvailable = await _embeddings.IsAvailableAsync(ct);
            status.VectorStoreAvailable = await _vectorStore.IsAvailableAsync(ct);

            if (status.VectorStoreAvailable)
            {
                var info = await _vectorStore.GetCollectionInfoAsync(_collectionName, ct);
                status.DocumentCount = info?.Count ?? 0;
                status.Dimension = _embeddings.Dimension;
            }
        }

        return status;
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

public class HybridSearchResult
{
    public long ObservationId { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public long CreatedAtEpoch { get; set; }
    public string Snippet { get; set; } = "";
    
    public float FtsScore { get; set; }
    public float VectorScore { get; set; }
    public float HybridScore { get; set; }
}

public class HybridSearchStatus
{
    public string Mode { get; set; } = "fts5";
    public bool FtsAvailable { get; set; }
    public bool VectorAvailable { get; set; }
    public string? EmbeddingProvider { get; set; }
    public bool EmbeddingAvailable { get; set; }
    public string? VectorStore { get; set; }
    public bool VectorStoreAvailable { get; set; }
    public long DocumentCount { get; set; }
    public int Dimension { get; set; }
}
