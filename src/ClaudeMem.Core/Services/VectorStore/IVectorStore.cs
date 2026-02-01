namespace ClaudeMem.Core.Services.VectorStore;

/// <summary>
/// Interface for vector storage backends (Qdrant, LanceDB, SQLite, etc.)
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Store name for configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initialize the store (create collections, etc.)
    /// </summary>
    Task InitializeAsync(string collectionName, int dimension, CancellationToken ct = default);

    /// <summary>
    /// Upsert vectors with metadata.
    /// </summary>
    Task UpsertAsync(string collectionName, IEnumerable<VectorRecord> records, CancellationToken ct = default);

    /// <summary>
    /// Search for similar vectors.
    /// </summary>
    Task<List<VectorSearchResult>> SearchAsync(
        string collectionName,
        float[] queryVector,
        int limit = 10,
        Dictionary<string, object>? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete vectors by ID.
    /// </summary>
    Task DeleteAsync(string collectionName, IEnumerable<string> ids, CancellationToken ct = default);

    /// <summary>
    /// Get collection info (count, etc.)
    /// </summary>
    Task<VectorCollectionInfo?> GetCollectionInfoAsync(string collectionName, CancellationToken ct = default);

    /// <summary>
    /// Check if the store is available.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}

public record VectorRecord(
    string Id,
    float[] Vector,
    Dictionary<string, object> Metadata
);

public record VectorSearchResult(
    string Id,
    float Score,
    Dictionary<string, object> Metadata
);

public record VectorCollectionInfo(
    string Name,
    long Count,
    int Dimension
);
