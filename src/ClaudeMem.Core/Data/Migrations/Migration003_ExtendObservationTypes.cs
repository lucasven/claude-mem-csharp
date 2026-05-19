using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Data.Migrations;

/// <summary>
/// Extends the observation type CHECK constraint to include types sent by hook scripts:
/// modification, action, observation (in addition to existing decision, bugfix, feature, refactor, discovery).
/// SQLite doesn't support ALTER CHECK, so we recreate the table.
/// </summary>
public class Migration003_ExtendObservationTypes : IMigration
{
    public int Version => 3;
    public string Name => "ExtendObservationTypes";

    public void Up(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            -- Drop FTS triggers temporarily
            DROP TRIGGER IF EXISTS observations_ai;
            DROP TRIGGER IF EXISTS observations_ad;
            DROP TRIGGER IF EXISTS observations_au;

            -- Rename old table
            ALTER TABLE observations RENAME TO observations_old;

            -- Create new table with expanded CHECK constraint
            CREATE TABLE observations (
                id INTEGER PRIMARY KEY,
                memory_session_id TEXT NOT NULL,
                project TEXT NOT NULL,
                type TEXT NOT NULL CHECK(type IN ('decision', 'bugfix', 'feature', 'refactor', 'discovery', 'modification', 'action', 'observation')),
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

            -- Copy data
            INSERT INTO observations SELECT * FROM observations_old;

            -- Drop old table
            DROP TABLE observations_old;

            -- Recreate indexes
            CREATE INDEX IF NOT EXISTS idx_observations_memory_session ON observations(memory_session_id);
            CREATE INDEX IF NOT EXISTS idx_observations_project ON observations(project);
            CREATE INDEX IF NOT EXISTS idx_observations_type ON observations(type);
            CREATE INDEX IF NOT EXISTS idx_observations_created_at ON observations(created_at_epoch);

            -- Recreate FTS triggers
            CREATE TRIGGER observations_ai AFTER INSERT ON observations BEGIN
                INSERT INTO observations_fts(rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES (new.id, new.title, new.subtitle, new.narrative, new.text, new.facts, new.concepts);
            END;

            CREATE TRIGGER observations_ad AFTER DELETE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES('delete', old.id, old.title, old.subtitle, old.narrative, old.text, old.facts, old.concepts);
            END;

            CREATE TRIGGER observations_au AFTER UPDATE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES('delete', old.id, old.title, old.subtitle, old.narrative, old.text, old.facts, old.concepts);
                INSERT INTO observations_fts(rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES (new.id, new.title, new.subtitle, new.narrative, new.text, new.facts, new.concepts);
            END;
            """;
        cmd.ExecuteNonQuery();
    }
}
