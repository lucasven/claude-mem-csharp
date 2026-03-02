using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Data.Migrations;

/// <summary>
/// Adds FTS5 full-text search virtual tables for hybrid search
/// (combining keyword search with vector similarity).
/// </summary>
public class Migration002_FTS5Search : IMigration
{
    public int Version => 2;
    public string Name => "FTS5Search";

    public void Up(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            -- FTS5 virtual table for observations
            CREATE VIRTUAL TABLE IF NOT EXISTS observations_fts USING fts5(
                title,
                subtitle,
                narrative,
                text,
                facts,
                concepts,
                content='observations',
                content_rowid='id'
            );

            -- FTS5 virtual table for session summaries
            CREATE VIRTUAL TABLE IF NOT EXISTS summaries_fts USING fts5(
                request,
                investigated,
                learned,
                completed,
                next_steps,
                notes,
                content='session_summaries',
                content_rowid='id'
            );

            -- FTS5 virtual table for user prompts
            CREATE VIRTUAL TABLE IF NOT EXISTS prompts_fts USING fts5(
                prompt_text,
                content='user_prompts',
                content_rowid='id'
            );

            -- Triggers to keep FTS5 in sync with observations
            CREATE TRIGGER IF NOT EXISTS observations_ai AFTER INSERT ON observations BEGIN
                INSERT INTO observations_fts(rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES (new.id, new.title, new.subtitle, new.narrative, new.text, new.facts, new.concepts);
            END;

            CREATE TRIGGER IF NOT EXISTS observations_ad AFTER DELETE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES('delete', old.id, old.title, old.subtitle, old.narrative, old.text, old.facts, old.concepts);
            END;

            CREATE TRIGGER IF NOT EXISTS observations_au AFTER UPDATE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES('delete', old.id, old.title, old.subtitle, old.narrative, old.text, old.facts, old.concepts);
                INSERT INTO observations_fts(rowid, title, subtitle, narrative, text, facts, concepts)
                VALUES (new.id, new.title, new.subtitle, new.narrative, new.text, new.facts, new.concepts);
            END;

            -- Triggers for session_summaries
            CREATE TRIGGER IF NOT EXISTS summaries_ai AFTER INSERT ON session_summaries BEGIN
                INSERT INTO summaries_fts(rowid, request, investigated, learned, completed, next_steps, notes)
                VALUES (new.id, new.request, new.investigated, new.learned, new.completed, new.next_steps, new.notes);
            END;

            CREATE TRIGGER IF NOT EXISTS summaries_ad AFTER DELETE ON session_summaries BEGIN
                INSERT INTO summaries_fts(summaries_fts, rowid, request, investigated, learned, completed, next_steps, notes)
                VALUES('delete', old.id, old.request, old.investigated, old.learned, old.completed, old.next_steps, old.notes);
            END;

            CREATE TRIGGER IF NOT EXISTS summaries_au AFTER UPDATE ON session_summaries BEGIN
                INSERT INTO summaries_fts(summaries_fts, rowid, request, investigated, learned, completed, next_steps, notes)
                VALUES('delete', old.id, old.request, old.investigated, old.learned, old.completed, old.next_steps, old.notes);
                INSERT INTO summaries_fts(rowid, request, investigated, learned, completed, next_steps, notes)
                VALUES (new.id, new.request, new.investigated, new.learned, new.completed, new.next_steps, new.notes);
            END;

            -- Triggers for user_prompts
            CREATE TRIGGER IF NOT EXISTS prompts_ai AFTER INSERT ON user_prompts BEGIN
                INSERT INTO prompts_fts(rowid, prompt_text)
                VALUES (new.id, new.prompt_text);
            END;

            CREATE TRIGGER IF NOT EXISTS prompts_ad AFTER DELETE ON user_prompts BEGIN
                INSERT INTO prompts_fts(prompts_fts, rowid, prompt_text)
                VALUES('delete', old.id, old.prompt_text);
            END;

            CREATE TRIGGER IF NOT EXISTS prompts_au AFTER UPDATE ON user_prompts BEGIN
                INSERT INTO prompts_fts(prompts_fts, rowid, prompt_text)
                VALUES('delete', old.id, old.prompt_text);
                INSERT INTO prompts_fts(rowid, prompt_text)
                VALUES (new.id, new.prompt_text);
            END;

            -- Rebuild FTS from existing data
            INSERT OR IGNORE INTO observations_fts(rowid, title, subtitle, narrative, text, facts, concepts)
            SELECT id, title, subtitle, narrative, text, facts, concepts FROM observations;

            INSERT OR IGNORE INTO summaries_fts(rowid, request, investigated, learned, completed, next_steps, notes)
            SELECT id, request, investigated, learned, completed, next_steps, notes FROM session_summaries;

            INSERT OR IGNORE INTO prompts_fts(rowid, prompt_text)
            SELECT id, prompt_text FROM user_prompts;
            """;
        cmd.ExecuteNonQuery();
    }
}
