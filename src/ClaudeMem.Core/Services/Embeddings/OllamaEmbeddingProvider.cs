using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeMem.Core.Services.Embeddings;

/// <summary>
/// Embedding provider using Ollama's local API.
/// Default model: nomic-embed-text (768 dimensions, ~300MB)
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;

    public string Name => "ollama";
    public int Dimension => _model switch
    {
        "nomic-embed-text" => 768,
        "mxbai-embed-large" => 1024,
        "all-minilm" => 384,
        _ => 768 // default
    };

    public OllamaEmbeddingProvider(string? baseUrl = null, string? model = null)
    {
        _baseUrl = baseUrl ?? Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
        _model = model ?? Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL") ?? "nomic-embed-text";
        _http = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var request = new OllamaEmbedRequest { Model = _model, Prompt = text };
        var response = await _http.PostAsJsonAsync("/api/embeddings", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);
        return result?.Embedding ?? Array.Empty<float>();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            var embedding = await GenerateEmbeddingAsync(text, ct);
            results.Add(embedding);
        }
        return results;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pull the embedding model if not already available.
    /// </summary>
    public async Task EnsureModelAsync(CancellationToken ct = default)
    {
        var request = new { name = _model };
        await _http.PostAsJsonAsync("/api/pull", request, ct);
    }

    private class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";
    }

    private class OllamaEmbedResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
