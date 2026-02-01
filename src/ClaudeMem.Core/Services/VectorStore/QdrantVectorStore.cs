using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeMem.Core.Services.VectorStore;

/// <summary>
/// Vector store implementation using Qdrant HTTP API.
/// Qdrant can run as Docker container or standalone binary.
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string Name => "qdrant";

    public QdrantVectorStore(string? baseUrl = null)
    {
        _baseUrl = baseUrl ?? Environment.GetEnvironmentVariable("QDRANT_URL") ?? "http://localhost:6333";
        _http = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task InitializeAsync(string collectionName, int dimension, CancellationToken ct = default)
    {
        // Check if collection exists
        var checkResponse = await _http.GetAsync($"/collections/{collectionName}", ct);
        if (checkResponse.IsSuccessStatusCode) return;

        // Create collection
        var createRequest = new
        {
            vectors = new
            {
                size = dimension,
                distance = "Cosine"
            }
        };

        var response = await _http.PutAsJsonAsync($"/collections/{collectionName}", createRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpsertAsync(string collectionName, IEnumerable<VectorRecord> records, CancellationToken ct = default)
    {
        var points = records.Select(r => new
        {
            id = r.Id,
            vector = r.Vector,
            payload = r.Metadata
        }).ToList();

        if (points.Count == 0) return;

        var request = new { points };
        var response = await _http.PutAsJsonAsync($"/collections/{collectionName}/points", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<VectorSearchResult>> SearchAsync(
        string collectionName,
        float[] queryVector,
        int limit = 10,
        Dictionary<string, object>? filter = null,
        CancellationToken ct = default)
    {
        var request = new Dictionary<string, object>
        {
            ["vector"] = queryVector,
            ["limit"] = limit,
            ["with_payload"] = true
        };

        if (filter != null)
        {
            request["filter"] = new { must = filter.Select(kv => new { key = kv.Key, match = new { value = kv.Value } }) };
        }

        var response = await _http.PostAsJsonAsync($"/collections/{collectionName}/points/search", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(ct);

        return result?.Result?.Select(r => new VectorSearchResult(
            r.Id?.ToString() ?? "",
            r.Score,
            r.Payload?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new()
        )).ToList() ?? new();
    }

    public async Task DeleteAsync(string collectionName, IEnumerable<string> ids, CancellationToken ct = default)
    {
        var request = new { points = ids.ToList() };
        var response = await _http.PostAsJsonAsync($"/collections/{collectionName}/points/delete", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<VectorCollectionInfo?> GetCollectionInfoAsync(string collectionName, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/collections/{collectionName}", ct);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<QdrantCollectionResponse>(ct);
            if (result?.Result == null) return null;

            return new VectorCollectionInfo(
                collectionName,
                result.Result.PointsCount ?? 0,
                result.Result.Config?.Params?.Size ?? 0
            );
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    #region Qdrant API Response Types

    private class QdrantSearchResponse
    {
        [JsonPropertyName("result")]
        public List<QdrantSearchResult>? Result { get; set; }
    }

    private class QdrantSearchResult
    {
        [JsonPropertyName("id")]
        public object? Id { get; set; }

        [JsonPropertyName("score")]
        public float Score { get; set; }

        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; set; }
    }

    private class QdrantCollectionResponse
    {
        [JsonPropertyName("result")]
        public QdrantCollectionResult? Result { get; set; }
    }

    private class QdrantCollectionResult
    {
        [JsonPropertyName("points_count")]
        public long? PointsCount { get; set; }

        [JsonPropertyName("config")]
        public QdrantConfig? Config { get; set; }
    }

    private class QdrantConfig
    {
        [JsonPropertyName("params")]
        public QdrantParams? Params { get; set; }
    }

    private class QdrantParams
    {
        [JsonPropertyName("size")]
        public int? Size { get; set; }
    }

    #endregion
}
