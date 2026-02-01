using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Core.Services;

/// <summary>
/// Syncs observations to ChromaDB for semantic search.
/// </summary>
public class ChromaSyncService : IDisposable
{
    private readonly ChromaService _chroma;
    private readonly IObservationRepository _observations;
    private readonly string _project;
    private readonly string _collectionName;

    public ChromaSyncService(
        ChromaService chroma,
        IObservationRepository observations,
        string project)
    {
        _chroma = chroma;
        _observations = observations;
        _project = project;
        _collectionName = $"cm__{SanitizeCollectionName(project)}";
    }

    /// <summary>
    /// Sync a single observation to Chroma.
    /// </summary>
    public async Task SyncObservationAsync(Observation observation, CancellationToken ct = default)
    {
        var doc = ObservationToDocument(observation);
        await _chroma.AddDocumentsAsync(_collectionName, new[] { doc }, ct);
    }

    /// <summary>
    /// Sync multiple observations to Chroma.
    /// </summary>
    public async Task SyncObservationsAsync(IEnumerable<Observation> observations, CancellationToken ct = default)
    {
        var docs = observations.Select(ObservationToDocument).ToList();
        if (docs.Count == 0) return;

        // Batch in groups of 100
        const int batchSize = 100;
        for (int i = 0; i < docs.Count; i += batchSize)
        {
            var batch = docs.Skip(i).Take(batchSize);
            await _chroma.AddDocumentsAsync(_collectionName, batch, ct);
        }
    }

    /// <summary>
    /// Sync all unsynced observations from the database.
    /// </summary>
    public async Task SyncAllUnsyncedAsync(CancellationToken ct = default)
    {
        // Get collection info to find what's already synced
        var info = await _chroma.GetCollectionInfoAsync(_collectionName, ct);
        var existingCount = info?.Count ?? 0;

        // Get total count in database
        var totalCount = _observations.GetCount(_project);

        if (totalCount <= existingCount)
        {
            // Likely all synced already
            return;
        }

        // Get recent observations that might not be synced
        var toSync = _observations.GetRecent(totalCount - existingCount, existingCount, _project);
        if (toSync.Count > 0)
        {
            await SyncObservationsAsync(toSync, ct);
            Console.WriteLine($"[ChromaSync] Synced {toSync.Count} observations to {_collectionName}");
        }
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
        Dictionary<string, object>? whereFilter = null;
        if (!string.IsNullOrEmpty(type))
        {
            whereFilter = new Dictionary<string, object>
            {
                ["type"] = type
            };
        }

        var results = await _chroma.QueryAsync(_collectionName, query, limit, whereFilter, ct);

        return results.Select(r => new SemanticSearchResult
        {
            ObservationId = long.TryParse(r.Id.Replace("obs_", ""), out var id) ? id : 0,
            Content = r.Document,
            Distance = r.Distance,
            Score = 1.0 - r.Distance, // Convert distance to similarity score
            Type = r.Metadata.TryGetValue("type", out var t) ? t.GetString() ?? "" : "",
            Title = r.Metadata.TryGetValue("title", out var ti) ? ti.GetString() ?? "" : ""
        }).ToList();
    }

    private ChromaDocument ObservationToDocument(Observation obs)
    {
        // Build searchable text content
        var contentParts = new List<string>();

        if (!string.IsNullOrEmpty(obs.Title))
            contentParts.Add($"Title: {obs.Title}");

        if (!string.IsNullOrEmpty(obs.Subtitle))
            contentParts.Add($"Subtitle: {obs.Subtitle}");

        if (!string.IsNullOrEmpty(obs.Narrative))
            contentParts.Add(obs.Narrative);

        if (obs.Facts.Count > 0)
            contentParts.Add($"Facts: {string.Join("; ", obs.Facts)}");

        if (obs.Concepts.Count > 0)
            contentParts.Add($"Concepts: {string.Join(", ", obs.Concepts)}");

        return new ChromaDocument
        {
            Id = $"obs_{obs.Id}",
            Content = string.Join("\n\n", contentParts),
            Metadata = new Dictionary<string, object>
            {
                ["observation_id"] = obs.Id,
                ["session_id"] = obs.MemorySessionId,
                ["type"] = obs.Type.ToString(),
                ["title"] = obs.Title ?? "",
                ["project"] = _project,
                ["created_at_epoch"] = obs.CreatedAtEpoch
            }
        };
    }

    private static string SanitizeCollectionName(string name)
    {
        // Chroma collection names: 3-63 chars, alphanumeric + underscores
        var sanitized = new string(name
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace("-", "_")
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray());

        if (sanitized.Length < 3)
            sanitized = sanitized.PadRight(3, '_');

        if (sanitized.Length > 60)
            sanitized = sanitized[..60];

        return sanitized.ToLowerInvariant();
    }

    public void Dispose()
    {
        _chroma.Dispose();
    }
}

public class SemanticSearchResult
{
    public long ObservationId { get; set; }
    public string Content { get; set; } = "";
    public double Distance { get; set; }
    public double Score { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
}
