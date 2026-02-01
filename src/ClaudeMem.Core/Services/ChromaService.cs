using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeMem.Core.Services;

/// <summary>
/// Service for interacting with ChromaDB via chroma-mcp (MCP over stdio).
/// Uses a small language model (all-MiniLM-L6-v2) for local embeddings.
/// </summary>
public class ChromaService : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private readonly string _dataDir;
    private readonly string _pythonVersion;
    private int _requestId = 0;
    private bool _initialized = false;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ChromaService(string? dataDir = null, string pythonVersion = "3.12")
    {
        _dataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-mem",
            "vector-db"
        );
        _pythonVersion = pythonVersion;
        Directory.CreateDirectory(_dataDir);
    }

    /// <summary>
    /// Start the chroma-mcp process and initialize MCP connection.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = "uvx",
                Arguments = $"--python {_pythonVersion} chroma-mcp --client-type persistent --data-dir \"{_dataDir}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.Start();

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            // Send MCP initialize request
            var initRequest = new McpRequest
            {
                JsonRpc = "2.0",
                Id = ++_requestId,
                Method = "initialize",
                Params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "claude-mem-csharp", version = "1.0.0" }
                }
            };

            var response = await SendRequestAsync<McpInitializeResult>(initRequest, ct);
            if (response?.ServerInfo != null)
            {
                Console.WriteLine($"[ChromaService] Connected to {response.ServerInfo.Name} v{response.ServerInfo.Version}");
            }

            // Send initialized notification
            var notification = new McpNotification
            {
                JsonRpc = "2.0",
                Method = "notifications/initialized"
            };
            await SendNotificationAsync(notification, ct);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Add documents to a collection with automatic embedding.
    /// </summary>
    public async Task<bool> AddDocumentsAsync(
        string collectionName,
        IEnumerable<ChromaDocument> documents,
        CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);

        // Ensure collection exists
        await CreateCollectionIfNotExistsAsync(collectionName, ct);

        var docs = documents.ToList();
        if (docs.Count == 0) return true;

        var request = new McpRequest
        {
            JsonRpc = "2.0",
            Id = ++_requestId,
            Method = "tools/call",
            Params = new
            {
                name = "chroma_add_documents",
                arguments = new
                {
                    collection_name = collectionName,
                    documents = docs.Select(d => d.Content).ToArray(),
                    ids = docs.Select(d => d.Id).ToArray(),
                    metadatas = docs.Select(d => d.Metadata).ToArray()
                }
            }
        };

        var response = await SendRequestAsync<McpToolResult>(request, ct);
        return response != null;
    }

    /// <summary>
    /// Query collection for similar documents.
    /// </summary>
    public async Task<List<ChromaQueryResult>> QueryAsync(
        string collectionName,
        string queryText,
        int nResults = 10,
        Dictionary<string, object>? whereFilter = null,
        CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);

        var args = new Dictionary<string, object>
        {
            ["collection_name"] = collectionName,
            ["query_texts"] = new[] { queryText },
            ["n_results"] = nResults
        };

        if (whereFilter != null)
        {
            args["where"] = whereFilter;
        }

        var request = new McpRequest
        {
            JsonRpc = "2.0",
            Id = ++_requestId,
            Method = "tools/call",
            Params = new
            {
                name = "chroma_query_documents",
                arguments = args
            }
        };

        var response = await SendRequestAsync<McpToolResult>(request, ct);
        if (response?.Content == null) return new List<ChromaQueryResult>();

        // Parse the query response
        var results = new List<ChromaQueryResult>();
        try
        {
            var content = response.Content.FirstOrDefault();
            if (content?.Text != null)
            {
                var queryResponse = JsonSerializer.Deserialize<ChromaQueryResponse>(content.Text);
                if (queryResponse?.Ids != null && queryResponse.Ids.Count > 0)
                {
                    var ids = queryResponse.Ids[0];
                    var documents = queryResponse.Documents?[0] ?? new List<string>();
                    var distances = queryResponse.Distances?[0] ?? new List<double>();
                    var metadatas = queryResponse.Metadatas?[0] ?? new List<Dictionary<string, JsonElement>>();

                    for (int i = 0; i < ids.Count; i++)
                    {
                        results.Add(new ChromaQueryResult
                        {
                            Id = ids[i],
                            Document = i < documents.Count ? documents[i] : "",
                            Distance = i < distances.Count ? distances[i] : 0,
                            Metadata = i < metadatas.Count ? metadatas[i] : new()
                        });
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Failed to parse, return empty
        }

        return results;
    }

    /// <summary>
    /// Delete documents by ID.
    /// </summary>
    public async Task<bool> DeleteDocumentsAsync(
        string collectionName,
        IEnumerable<string> ids,
        CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);

        var request = new McpRequest
        {
            JsonRpc = "2.0",
            Id = ++_requestId,
            Method = "tools/call",
            Params = new
            {
                name = "chroma_delete_documents",
                arguments = new
                {
                    collection_name = collectionName,
                    ids = ids.ToArray()
                }
            }
        };

        var response = await SendRequestAsync<McpToolResult>(request, ct);
        return response != null;
    }

    /// <summary>
    /// Get collection info.
    /// </summary>
    public async Task<ChromaCollectionInfo?> GetCollectionInfoAsync(
        string collectionName,
        CancellationToken ct = default)
    {
        await EnsureStartedAsync(ct);

        var request = new McpRequest
        {
            JsonRpc = "2.0",
            Id = ++_requestId,
            Method = "tools/call",
            Params = new
            {
                name = "chroma_get_collection_info",
                arguments = new { collection_name = collectionName }
            }
        };

        var response = await SendRequestAsync<McpToolResult>(request, ct);
        if (response?.Content?.FirstOrDefault()?.Text == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ChromaCollectionInfo>(response.Content[0].Text!);
        }
        catch
        {
            return null;
        }
    }

    private async Task CreateCollectionIfNotExistsAsync(string collectionName, CancellationToken ct)
    {
        var info = await GetCollectionInfoAsync(collectionName, ct);
        if (info != null) return;

        var request = new McpRequest
        {
            JsonRpc = "2.0",
            Id = ++_requestId,
            Method = "tools/call",
            Params = new
            {
                name = "chroma_create_collection",
                arguments = new
                {
                    collection_name = collectionName,
                    embedding_function_name = "default"
                }
            }
        };

        await SendRequestAsync<McpToolResult>(request, ct);
    }

    private async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (!_initialized)
        {
            await StartAsync(ct);
        }
    }

    private async Task<T?> SendRequestAsync<T>(McpRequest request, CancellationToken ct) where T : class
    {
        if (_stdin == null || _stdout == null)
            throw new InvalidOperationException("ChromaService not started");

        var json = JsonSerializer.Serialize(request, JsonOptions);
        await _stdin.WriteLineAsync(json);
        await _stdin.FlushAsync();

        // Read response
        var responseLine = await _stdout.ReadLineAsync(ct);
        if (string.IsNullOrEmpty(responseLine)) return null;

        var response = JsonSerializer.Deserialize<McpResponse<T>>(responseLine, JsonOptions);
        return response?.Result;
    }

    private async Task SendNotificationAsync(McpNotification notification, CancellationToken ct)
    {
        if (_stdin == null)
            throw new InvalidOperationException("ChromaService not started");

        var json = JsonSerializer.Serialize(notification, JsonOptions);
        await _stdin.WriteLineAsync(json);
        await _stdin.FlushAsync();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Dispose()
    {
        _stdin?.Dispose();
        _stdout?.Dispose();
        if (_process != null && !_process.HasExited)
        {
            _process.Kill();
            _process.Dispose();
        }
        _lock.Dispose();
    }
}

#region MCP Protocol Types

internal class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

internal class McpNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

internal class McpResponse<T>
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

internal class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

internal class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "";

    [JsonPropertyName("serverInfo")]
    public McpServerInfo? ServerInfo { get; set; }
}

internal class McpServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

internal class McpToolResult
{
    [JsonPropertyName("content")]
    public List<McpContent>? Content { get; set; }

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

internal class McpContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

#endregion

#region Chroma Types

public class ChromaDocument
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class ChromaQueryResult
{
    public string Id { get; set; } = "";
    public string Document { get; set; } = "";
    public double Distance { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = new();
}

public class ChromaCollectionInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

internal class ChromaQueryResponse
{
    [JsonPropertyName("ids")]
    public List<List<string>>? Ids { get; set; }

    [JsonPropertyName("documents")]
    public List<List<string>>? Documents { get; set; }

    [JsonPropertyName("distances")]
    public List<List<double>>? Distances { get; set; }

    [JsonPropertyName("metadatas")]
    public List<List<Dictionary<string, JsonElement>>>? Metadatas { get; set; }
}

#endregion
