using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeMem.Core.Services.Embeddings;

/// <summary>
/// Embedding provider using OpenAI's Embeddings API.
/// Compatible with OpenAI, Azure OpenAI, and OpenAI-compatible APIs (OpenRouter, etc.)
/// </summary>
public class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    public string Name => "openai";
    
    public int Dimension => _model switch
    {
        "text-embedding-3-small" => 1536,
        "text-embedding-3-large" => 3072,
        "text-embedding-ada-002" => 1536,
        _ => 1536 // default
    };

    public OpenAIEmbeddingProvider(string apiKey, string? model = null, string? baseUrl = null)
    {
        _apiKey = apiKey;
        _model = model ?? "text-embedding-3-small";
        
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl ?? "https://api.openai.com/v1/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var request = new EmbeddingRequest
        {
            Model = _model,
            Input = text
        };

        var response = await _http.PostAsJsonAsync("embeddings", request, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"OpenAI embeddings API error: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
        
        if (result?.Data == null || result.Data.Count == 0)
            throw new Exception("No embedding returned from API");

        return result.Data[0].Embedding;
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var textList = texts.ToList();
        
        // OpenAI supports batch embeddings
        var request = new BatchEmbeddingRequest
        {
            Model = _model,
            Input = textList
        };

        var response = await _http.PostAsJsonAsync("embeddings", request, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"OpenAI embeddings API error: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
        
        if (result?.Data == null)
            throw new Exception("No embeddings returned from API");

        // Sort by index to maintain order
        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            // Try a minimal embedding to check availability
            var request = new EmbeddingRequest
            {
                Model = _model,
                Input = "test"
            };

            var response = await _http.PostAsJsonAsync("embeddings", request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input")]
        public string Input { get; set; } = "";
    }

    private class BatchEmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = new();
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public UsageData? Usage { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();

        [JsonPropertyName("index")]
        public int Index { get; set; }
    }

    private class UsageData
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
