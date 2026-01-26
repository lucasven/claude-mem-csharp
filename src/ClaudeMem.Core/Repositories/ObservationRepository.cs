using System.Text.Json;
using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Repositories;

public class ObservationRepository : IObservationRepository
{
    private readonly ClaudeMemDatabase _db;

    public ObservationRepository(ClaudeMemDatabase db)
    {
        _db = db;
    }

    public long Store(Observation observation)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO observations (
                memory_session_id, project, type, title, subtitle, narrative, text,
                facts, concepts, files_read, files_modified, prompt_number,
                discovery_tokens, created_at, created_at_epoch
            ) VALUES (
                @memorySessionId, @project, @type, @title, @subtitle, @narrative, @text,
                @facts, @concepts, @filesRead, @filesModified, @promptNumber,
                @discoveryTokens, @createdAt, @createdAtEpoch
            );
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("@memorySessionId", observation.MemorySessionId);
        cmd.Parameters.AddWithValue("@project", observation.Project);
        cmd.Parameters.AddWithValue("@type", observation.Type.ToString().ToLower());
        cmd.Parameters.AddWithValue("@title", (object?)observation.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subtitle", (object?)observation.Subtitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@narrative", (object?)observation.Narrative ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@text", observation.Text);
        cmd.Parameters.AddWithValue("@facts", JsonSerializer.Serialize(observation.Facts));
        cmd.Parameters.AddWithValue("@concepts", JsonSerializer.Serialize(observation.Concepts));
        cmd.Parameters.AddWithValue("@filesRead", JsonSerializer.Serialize(observation.FilesRead));
        cmd.Parameters.AddWithValue("@filesModified", JsonSerializer.Serialize(observation.FilesModified));
        cmd.Parameters.AddWithValue("@promptNumber", (object?)observation.PromptNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@discoveryTokens", observation.DiscoveryTokens);
        cmd.Parameters.AddWithValue("@createdAt", observation.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@createdAtEpoch", observation.CreatedAtEpoch);

        return (long)cmd.ExecuteScalar()!;
    }

    public Observation? GetById(long id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM observations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapObservation(reader) : null;
    }

    public List<Observation> GetRecent(int limit = 20, int offset = 0, string? project = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        var sql = "SELECT * FROM observations";
        if (project != null)
        {
            sql += " WHERE project = @project";
            cmd.Parameters.AddWithValue("@project", project);
        }
        sql += " ORDER BY created_at_epoch DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        cmd.CommandText = sql;

        var results = new List<Observation>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapObservation(reader));
        }
        return results;
    }

    public List<Observation> GetByIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return [];

        using var cmd = _db.Connection.CreateCommand();
        var placeholders = string.Join(",", idList.Select((_, i) => $"@id{i}"));
        cmd.CommandText = $"SELECT * FROM observations WHERE id IN ({placeholders}) ORDER BY created_at_epoch DESC";

        for (int i = 0; i < idList.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@id{i}", idList[i]);
        }

        var results = new List<Observation>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapObservation(reader));
        }
        return results;
    }

    public int GetCount(string? project = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = project != null
            ? "SELECT COUNT(*) FROM observations WHERE project = @project"
            : "SELECT COUNT(*) FROM observations";
        if (project != null)
            cmd.Parameters.AddWithValue("@project", project);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static Observation MapObservation(SqliteDataReader reader)
    {
        return new Observation
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            MemorySessionId = reader.GetString(reader.GetOrdinal("memory_session_id")),
            Project = reader.GetString(reader.GetOrdinal("project")),
            Type = Enum.Parse<ObservationType>(reader.GetString(reader.GetOrdinal("type")), ignoreCase: true),
            Title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title")),
            Subtitle = reader.IsDBNull(reader.GetOrdinal("subtitle")) ? null : reader.GetString(reader.GetOrdinal("subtitle")),
            Narrative = reader.IsDBNull(reader.GetOrdinal("narrative")) ? null : reader.GetString(reader.GetOrdinal("narrative")),
            Text = reader.GetString(reader.GetOrdinal("text")),
            Facts = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("facts"))) ?? [],
            Concepts = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("concepts"))) ?? [],
            FilesRead = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("files_read"))) ?? [],
            FilesModified = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("files_modified"))) ?? [],
            PromptNumber = reader.IsDBNull(reader.GetOrdinal("prompt_number")) ? null : reader.GetInt32(reader.GetOrdinal("prompt_number")),
            DiscoveryTokens = reader.GetInt32(reader.GetOrdinal("discovery_tokens")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
        };
    }
}
