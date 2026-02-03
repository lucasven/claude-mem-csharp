using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Services.VectorStore;

/// <summary>
/// Simple vector store using SQLite with brute-force cosine similarity.
/// Good for small datasets (&lt;100k vectors). No external dependencies.
/// </summary>
public class SqliteVectorStore : IVectorStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dbPath;

    public string Name => "sqlite";

    public SqliteVectorStore(string? dbPath = null)
    {
        var dataDir = Environment.GetEnvironmentVariable("CLAUDE_MEM_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-mem");
        
        _dbPath = dbPath ?? Path.Combine(dataDir, "vectors.db");

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        // Enable WAL mode
        ExecuteNonQuery("PRAGMA journal_mode = WAL");
        ExecuteNonQuery("PRAGMA synchronous = NORMAL");
    }

    public Task InitializeAsync(string collectionName, int dimension, CancellationToken ct = default)
    {
        var tableName = SanitizeTableName(collectionName);

        ExecuteNonQuery($@"
            CREATE TABLE IF NOT EXISTS {tableName} (
                id TEXT PRIMARY KEY,
                vector BLOB NOT NULL,
                metadata TEXT,
                created_at INTEGER DEFAULT (strftime('%s', 'now'))
            )");

        ExecuteNonQuery($@"
            CREATE TABLE IF NOT EXISTS {tableName}_meta (
                key TEXT PRIMARY KEY,
                value TEXT
            )");

        ExecuteNonQuery($@"
            INSERT OR REPLACE INTO {tableName}_meta (key, value) 
            VALUES ('dimension', @dim)",
            ("@dim", dimension.ToString()));

        return Task.CompletedTask;
    }

    public Task UpsertAsync(string collectionName, IEnumerable<VectorRecord> records, CancellationToken ct = default)
    {
        var tableName = SanitizeTableName(collectionName);

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var record in records)
            {
                var vectorBytes = VectorToBytes(record.Vector);
                var metadataJson = JsonSerializer.Serialize(record.Metadata);

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $@"
                    INSERT OR REPLACE INTO {tableName} (id, vector, metadata)
                    VALUES (@id, @vector, @metadata)";
                cmd.Parameters.AddWithValue("@id", record.Id);
                cmd.Parameters.AddWithValue("@vector", vectorBytes);
                cmd.Parameters.AddWithValue("@metadata", metadataJson);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<List<VectorSearchResult>> SearchAsync(
        string collectionName,
        float[] queryVector,
        int limit = 10,
        Dictionary<string, object>? filter = null,
        CancellationToken ct = default)
    {
        var tableName = SanitizeTableName(collectionName);
        var results = new List<(string Id, float Score, Dictionary<string, object> Metadata)>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT id, vector, metadata FROM {tableName}";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var vectorBytes = (byte[])reader.GetValue(1);
            var metadataJson = reader.IsDBNull(2) ? "{}" : reader.GetString(2);

            var storedVector = BytesToVector(vectorBytes);
            var score = CosineSimilarity(queryVector, storedVector);
            var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson) ?? new();

            // Apply filter if provided
            if (filter != null)
            {
                var matches = filter.All(kv =>
                    metadata.TryGetValue(kv.Key, out var val) &&
                    val?.ToString() == kv.Value?.ToString());
                if (!matches) continue;
            }

            results.Add((id, score, metadata));
        }

        // Sort by score descending and take top N
        var topResults = results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .Select(r => new VectorSearchResult(r.Id, r.Score, r.Metadata))
            .ToList();

        return Task.FromResult(topResults);
    }

    public Task DeleteAsync(string collectionName, IEnumerable<string> ids, CancellationToken ct = default)
    {
        var tableName = SanitizeTableName(collectionName);
        var idList = ids.ToList();

        if (idList.Count == 0) return Task.CompletedTask;

        var placeholders = string.Join(",", idList.Select((_, i) => $"@id{i}"));

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {tableName} WHERE id IN ({placeholders})";

        for (int i = 0; i < idList.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@id{i}", idList[i]);
        }

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<VectorCollectionInfo?> GetCollectionInfoAsync(string collectionName, CancellationToken ct = default)
    {
        var tableName = SanitizeTableName(collectionName);

        try
        {
            using var countCmd = _connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {tableName}";
            var count = Convert.ToInt64(countCmd.ExecuteScalar());

            using var dimCmd = _connection.CreateCommand();
            dimCmd.CommandText = $"SELECT value FROM {tableName}_meta WHERE key = 'dimension'";
            var dimStr = dimCmd.ExecuteScalar()?.ToString() ?? "0";
            var dimension = int.Parse(dimStr);

            return Task.FromResult<VectorCollectionInfo?>(new VectorCollectionInfo(collectionName, count, dimension));
        }
        catch
        {
            return Task.FromResult<VectorCollectionInfo?>(null);
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_connection.State == System.Data.ConnectionState.Open);
    }

    #region Vector Math

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }

    private static byte[] VectorToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    #endregion

    #region Helpers

    private static string SanitizeTableName(string name)
    {
        return "vec_" + new string(name
            .Replace("-", "_")
            .Replace("/", "_")
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray()).ToLowerInvariant();
    }

    private void ExecuteNonQuery(string sql, params (string name, object value)[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        cmd.ExecuteNonQuery();
    }

    #endregion

    public void Dispose()
    {
        _connection.Dispose();
    }
}
