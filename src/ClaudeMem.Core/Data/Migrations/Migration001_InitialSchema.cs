using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Data.Migrations;

public class Migration001_InitialSchema : IMigration
{
    public int Version => 1;
    public string Name => "InitialSchema";

    public void Up(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            -- SDK Sessions table
            CREATE TABLE IF NOT EXISTS sdk_sessions (
                id INTEGER PRIMARY KEY,
                content_session_id TEXT UNIQUE NOT NULL,
                memory_session_id TEXT UNIQUE,
                project TEXT NOT NULL,
                user_prompt TEXT,
                started_at TEXT NOT NULL,
                started_at_epoch INTEGER NOT NULL,
                completed_at TEXT,
                completed_at_epoch INTEGER,
                status TEXT CHECK(status IN ('active', 'completed', 'failed')) DEFAULT 'active'
            );

            -- Observations table
            CREATE TABLE IF NOT EXISTS observations (
                id INTEGER PRIMARY KEY,
                memory_session_id TEXT NOT NULL,
                project TEXT NOT NULL,
                type TEXT NOT NULL CHECK(type IN ('decision', 'bugfix', 'feature', 'refactor', 'discovery')),
                title TEXT,
                subtitle TEXT,
                narrative TEXT,
                text TEXT NOT NULL,
                facts TEXT,
                concepts TEXT,
                files_read TEXT,
                files_modified TEXT,
                prompt_number INTEGER,
                discovery_tokens INTEGER DEFAULT 0,
                created_at TEXT NOT NULL,
                created_at_epoch INTEGER NOT NULL,
                FOREIGN KEY (memory_session_id) REFERENCES sdk_sessions(memory_session_id) ON DELETE CASCADE
            );

            -- Session summaries table
            CREATE TABLE IF NOT EXISTS session_summaries (
                id INTEGER PRIMARY KEY,
                memory_session_id TEXT UNIQUE NOT NULL,
                project TEXT NOT NULL,
                request TEXT,
                investigated TEXT,
                learned TEXT,
                completed TEXT,
                next_steps TEXT,
                files_read TEXT,
                files_edited TEXT,
                notes TEXT,
                prompt_number INTEGER,
                discovery_tokens INTEGER DEFAULT 0,
                created_at TEXT NOT NULL,
                created_at_epoch INTEGER NOT NULL,
                FOREIGN KEY (memory_session_id) REFERENCES sdk_sessions(memory_session_id) ON DELETE CASCADE
            );

            -- User prompts table
            CREATE TABLE IF NOT EXISTS user_prompts (
                id INTEGER PRIMARY KEY,
                content_session_id TEXT NOT NULL,
                project TEXT NOT NULL,
                prompt_number INTEGER NOT NULL,
                prompt_text TEXT NOT NULL,
                memory_session_id TEXT,
                created_at TEXT NOT NULL,
                created_at_epoch INTEGER NOT NULL,
                UNIQUE(content_session_id, prompt_number)
            );

            -- Schema versions table
            CREATE TABLE IF NOT EXISTS schema_versions (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );

            -- Indexes
            CREATE INDEX IF NOT EXISTS idx_observations_memory_session ON observations(memory_session_id);
            CREATE INDEX IF NOT EXISTS idx_observations_project ON observations(project);
            CREATE INDEX IF NOT EXISTS idx_observations_type ON observations(type);
            CREATE INDEX IF NOT EXISTS idx_observations_created_at ON observations(created_at_epoch);
            CREATE INDEX IF NOT EXISTS idx_sdk_sessions_project ON sdk_sessions(project);
            CREATE INDEX IF NOT EXISTS idx_user_prompts_session ON user_prompts(content_session_id);
            """;
        cmd.ExecuteNonQuery();
    }
}
