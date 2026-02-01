namespace ClaudeMem.Core.Services.Embeddings;

/// <summary>
/// Interface for embedding providers (Ollama, OpenAI, etc.)
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Provider name for configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Generate embeddings for a single text.
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Generate embeddings for multiple texts (batch).
    /// </summary>
    Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Check if the provider is available/healthy.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Get the embedding dimension for this provider/model.
    /// </summary>
    int Dimension { get; }
}
