using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Repositories;

public class SummaryRepository : ISummaryRepository
{
    private readonly ClaudeMemDatabase _db;

    public SummaryRepository(ClaudeMemDatabase db)
    {
        _db = db;
    }

    public long Store(Summary summary)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO session_summaries (
                memory_session_id, project, request, investigated, learned,
                completed, next_steps, files_read, files_edited, notes,
                prompt_number, discovery_tokens, created_at, created_at_epoch
            ) VALUES (
                @memorySessionId, @project, @request, @investigated, @learned,
                @completed, @nextSteps, @filesRead, @filesEdited, @notes,
                @promptNumber, @discoveryTokens, @createdAt, @createdAtEpoch
            );
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("@memorySessionId", summary.MemorySessionId);
        cmd.Parameters.AddWithValue("@project", summary.Project);
        cmd.Parameters.AddWithValue("@request", (object?)summary.Request ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@investigated", (object?)summary.Investigated ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@learned", (object?)summary.Learned ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@completed", (object?)summary.Completed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nextSteps", (object?)summary.NextSteps ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@filesRead", (object?)summary.FilesRead ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@filesEdited", (object?)summary.FilesEdited ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)summary.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@promptNumber", (object?)summary.PromptNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@discoveryTokens", summary.DiscoveryTokens);
        cmd.Parameters.AddWithValue("@createdAt", summary.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@createdAtEpoch", summary.CreatedAtEpoch);

        return (long)cmd.ExecuteScalar()!;
    }

    public Summary? GetById(long id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM session_summaries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapSummary(reader) : null;
    }

    public Summary? GetByMemorySessionId(string memorySessionId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM session_summaries WHERE memory_session_id = @id";
        cmd.Parameters.AddWithValue("@id", memorySessionId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapSummary(reader) : null;
    }

    public List<Summary> GetRecent(int limit = 20, int offset = 0, string? project = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        var sql = "SELECT * FROM session_summaries";
        if (project != null)
        {
            sql += " WHERE project = @project";
            cmd.Parameters.AddWithValue("@project", project);
        }
        sql += " ORDER BY created_at_epoch DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        cmd.CommandText = sql;

        var results = new List<Summary>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapSummary(reader));
        }
        return results;
    }

    private static Summary MapSummary(SqliteDataReader reader)
    {
        return new Summary
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            MemorySessionId = reader.GetString(reader.GetOrdinal("memory_session_id")),
            Project = reader.GetString(reader.GetOrdinal("project")),
            Request = GetNullableString(reader, "request"),
            Investigated = GetNullableString(reader, "investigated"),
            Learned = GetNullableString(reader, "learned"),
            Completed = GetNullableString(reader, "completed"),
            NextSteps = GetNullableString(reader, "next_steps"),
            FilesRead = GetNullableString(reader, "files_read"),
            FilesEdited = GetNullableString(reader, "files_edited"),
            Notes = GetNullableString(reader, "notes"),
            PromptNumber = reader.IsDBNull(reader.GetOrdinal("prompt_number"))
                ? null : reader.GetInt32(reader.GetOrdinal("prompt_number")),
            DiscoveryTokens = reader.GetInt32(reader.GetOrdinal("discovery_tokens")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
        };
    }

    public long GetCount()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM session_summaries";
        return (long)(cmd.ExecuteScalar() ?? 0);
    }

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
