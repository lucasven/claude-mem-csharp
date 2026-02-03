using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Repositories;

public class UserPromptRepository : IUserPromptRepository
{
    private readonly ClaudeMemDatabase _db;

    public UserPromptRepository(ClaudeMemDatabase db)
    {
        _db = db;
    }

    public List<UserPrompt> GetRecent(int limit, int offset, string? project)
    {
        var prompts = new List<UserPrompt>();
        using var cmd = _db.Connection.CreateCommand();

        var sql = "SELECT * FROM user_prompts";
        if (!string.IsNullOrEmpty(project))
        {
            sql += " WHERE project = @project";
            cmd.Parameters.AddWithValue("@project", project);
        }
        sql += " ORDER BY created_at_epoch DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            prompts.Add(MapPrompt(reader));
        }

        return prompts;
    }

    public UserPrompt? GetById(long id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM user_prompts WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapPrompt(reader) : null;
    }

    public long GetCount(string? project = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        var sql = "SELECT COUNT(*) FROM user_prompts";
        if (!string.IsNullOrEmpty(project))
        {
            sql += " WHERE project = @project";
            cmd.Parameters.AddWithValue("@project", project);
        }
        cmd.CommandText = sql;

        return (long)(cmd.ExecuteScalar() ?? 0);
    }

    public long Store(UserPrompt prompt)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_prompts (
                content_session_id, project, prompt_number, prompt_text, 
                memory_session_id, created_at, created_at_epoch
            ) VALUES (
                @contentSessionId, @project, @promptNumber, @promptText,
                @memorySessionId, @createdAt, @createdAtEpoch
            )
            """;

        var now = DateTime.UtcNow;
        cmd.Parameters.AddWithValue("@contentSessionId", prompt.ContentSessionId);
        cmd.Parameters.AddWithValue("@project", prompt.Project);
        cmd.Parameters.AddWithValue("@promptNumber", prompt.PromptNumber);
        cmd.Parameters.AddWithValue("@promptText", prompt.PromptText);
        cmd.Parameters.AddWithValue("@memorySessionId", (object?)prompt.MemorySessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", now.ToString("o"));
        cmd.Parameters.AddWithValue("@createdAtEpoch", new DateTimeOffset(now).ToUnixTimeMilliseconds());

        cmd.ExecuteNonQuery();

        using var idCmd = _db.Connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return (long)(idCmd.ExecuteScalar() ?? 0);
    }

    public int GetNextPromptNumber(string contentSessionId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(prompt_number), 0) + 1 FROM user_prompts WHERE content_session_id = @sessionId";
        cmd.Parameters.AddWithValue("@sessionId", contentSessionId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static UserPrompt MapPrompt(SqliteDataReader reader)
    {
        return new UserPrompt
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            ContentSessionId = reader.GetString(reader.GetOrdinal("content_session_id")),
            Project = reader.GetString(reader.GetOrdinal("project")),
            PromptNumber = reader.GetInt32(reader.GetOrdinal("prompt_number")),
            PromptText = reader.GetString(reader.GetOrdinal("prompt_text")),
            MemorySessionId = reader.IsDBNull(reader.GetOrdinal("memory_session_id"))
                ? null : reader.GetString(reader.GetOrdinal("memory_session_id")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
        };
    }
}
