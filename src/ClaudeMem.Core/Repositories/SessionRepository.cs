using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly ClaudeMemDatabase _db;

    public SessionRepository(ClaudeMemDatabase db)
    {
        _db = db;
    }

    public long Create(Session session)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sdk_sessions (
                content_session_id, memory_session_id, project, user_prompt,
                started_at, started_at_epoch, status
            ) VALUES (
                @contentSessionId, @memorySessionId, @project, @userPrompt,
                @startedAt, @startedAtEpoch, @status
            );
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("@contentSessionId", session.ContentSessionId);
        cmd.Parameters.AddWithValue("@memorySessionId", (object?)session.MemorySessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@project", session.Project);
        cmd.Parameters.AddWithValue("@userPrompt", (object?)session.UserPrompt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startedAt", session.StartedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@startedAtEpoch", session.StartedAtEpoch);
        cmd.Parameters.AddWithValue("@status", session.Status.ToString().ToLower());

        return (long)cmd.ExecuteScalar()!;
    }

    public Session? GetById(long id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM sdk_sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapSession(reader) : null;
    }

    public Session? GetByContentSessionId(string contentSessionId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM sdk_sessions WHERE content_session_id = @id";
        cmd.Parameters.AddWithValue("@id", contentSessionId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapSession(reader) : null;
    }

    public Session? GetByMemorySessionId(string memorySessionId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM sdk_sessions WHERE memory_session_id = @id";
        cmd.Parameters.AddWithValue("@id", memorySessionId);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapSession(reader) : null;
    }

    public void UpdateMemorySessionId(string contentSessionId, string memorySessionId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE sdk_sessions
            SET memory_session_id = @memorySessionId
            WHERE content_session_id = @contentSessionId
            """;
        cmd.Parameters.AddWithValue("@memorySessionId", memorySessionId);
        cmd.Parameters.AddWithValue("@contentSessionId", contentSessionId);
        cmd.ExecuteNonQuery();
    }

    public void Complete(long id)
    {
        using var cmd = _db.Connection.CreateCommand();
        var now = DateTime.UtcNow;
        cmd.CommandText = """
            UPDATE sdk_sessions
            SET status = 'completed', completed_at = @completedAt, completed_at_epoch = @epoch
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@completedAt", now.ToString("o"));
        cmd.Parameters.AddWithValue("@epoch", new DateTimeOffset(now).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static Session MapSession(SqliteDataReader reader)
    {
        return new Session
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            ContentSessionId = reader.GetString(reader.GetOrdinal("content_session_id")),
            MemorySessionId = reader.IsDBNull(reader.GetOrdinal("memory_session_id"))
                ? null : reader.GetString(reader.GetOrdinal("memory_session_id")),
            Project = reader.GetString(reader.GetOrdinal("project")),
            UserPrompt = reader.IsDBNull(reader.GetOrdinal("user_prompt"))
                ? null : reader.GetString(reader.GetOrdinal("user_prompt")),
            StartedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at"))
                ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("completed_at"))),
            Status = Enum.Parse<SessionStatus>(reader.GetString(reader.GetOrdinal("status")), ignoreCase: true)
        };
    }
}
