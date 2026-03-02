using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Core.Services;

/// <summary>
/// Full-text search using SQLite FTS5.
/// Provides fast keyword-based search as part of hybrid search strategy.
/// </summary>
public class FullTextSearchService
{
    private readonly ClaudeMemDatabase _db;

    public FullTextSearchService(ClaudeMemDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Search observations using FTS5 full-text search.
    /// Returns results ranked by BM25 relevance.
    /// </summary>
    public List<FtsSearchResult> SearchObservations(
        string query,
        int limit = 20,
        string? type = null,
        string? project = null,
        long? dateStart = null,
        long? dateEnd = null)
    {
        var results = new List<FtsSearchResult>();
        var escapedQuery = EscapeFts5Query(query);

        if (string.IsNullOrWhiteSpace(escapedQuery))
            return results;

        var conn = _db.GetConnection();  // Shared singleton - dont dispose
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT 
                o.id,
                o.title,
                o.type,
                o.project,
                o.created_at_epoch,
                bm25(observations_fts) as rank,
                snippet(observations_fts, 2, '<mark>', '</mark>', '...', 64) as snippet
            FROM observations_fts
            JOIN observations o ON observations_fts.rowid = o.id
            WHERE observations_fts MATCH @query
            """;

        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(type))
            conditions.Add("o.type = @type");
        if (!string.IsNullOrEmpty(project))
            conditions.Add("o.project = @project");
        if (dateStart.HasValue)
            conditions.Add("o.created_at_epoch >= @dateStart");
        if (dateEnd.HasValue)
            conditions.Add("o.created_at_epoch <= @dateEnd");

        if (conditions.Count > 0)
            sql += " AND " + string.Join(" AND ", conditions);

        sql += " ORDER BY rank LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", escapedQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        if (!string.IsNullOrEmpty(type))
            cmd.Parameters.AddWithValue("@type", type);
        if (!string.IsNullOrEmpty(project))
            cmd.Parameters.AddWithValue("@project", project);
        if (dateStart.HasValue)
            cmd.Parameters.AddWithValue("@dateStart", dateStart.Value);
        if (dateEnd.HasValue)
            cmd.Parameters.AddWithValue("@dateEnd", dateEnd.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FtsSearchResult
            {
                Id = reader.GetInt64(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Project = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CreatedAtEpoch = reader.GetInt64(4),
                Rank = reader.GetDouble(5),
                Snippet = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });
        }

        return results;
    }

    /// <summary>
    /// Search summaries using FTS5.
    /// </summary>
    public List<FtsSearchResult> SearchSummaries(string query, int limit = 20, string? project = null)
    {
        var results = new List<FtsSearchResult>();
        var escapedQuery = EscapeFts5Query(query);

        if (string.IsNullOrWhiteSpace(escapedQuery))
            return results;

        var conn = _db.GetConnection();  // Shared singleton - dont dispose
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT 
                s.id,
                s.request as title,
                'summary' as type,
                s.project,
                s.created_at_epoch,
                bm25(summaries_fts) as rank,
                snippet(summaries_fts, 2, '<mark>', '</mark>', '...', 64) as snippet
            FROM summaries_fts
            JOIN session_summaries s ON summaries_fts.rowid = s.id
            WHERE summaries_fts MATCH @query
            """;

        if (!string.IsNullOrEmpty(project))
            sql += " AND s.project = @project";

        sql += " ORDER BY rank LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", escapedQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        if (!string.IsNullOrEmpty(project))
            cmd.Parameters.AddWithValue("@project", project);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FtsSearchResult
            {
                Id = reader.GetInt64(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Type = reader.GetString(2),
                Project = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CreatedAtEpoch = reader.GetInt64(4),
                Rank = reader.GetDouble(5),
                Snippet = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });
        }

        return results;
    }

    /// <summary>
    /// Get timeline context around a specific observation.
    /// </summary>
    public TimelineResult GetTimeline(
        long anchorId,
        int depthBefore = 3,
        int depthAfter = 3,
        string? project = null)
    {
        var conn = _db.GetConnection();  // Shared singleton - dont dispose
        
        // Get anchor observation
        var anchor = GetObservationById(conn, anchorId);
        if (anchor == null)
            return new TimelineResult { Found = false };

        var result = new TimelineResult
        {
            Found = true,
            Anchor = anchor,
            Before = new List<TimelineItem>(),
            After = new List<TimelineItem>()
        };

        // Get observations before
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, title, type, project, created_at_epoch
                FROM observations
                WHERE created_at_epoch < @anchorEpoch
                """ + (project != null ? " AND project = @project" : "") + """
                ORDER BY created_at_epoch DESC
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@anchorEpoch", anchor.CreatedAtEpoch);
            cmd.Parameters.AddWithValue("@limit", depthBefore);
            if (project != null)
                cmd.Parameters.AddWithValue("@project", project);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Before.Add(new TimelineItem
                {
                    Id = reader.GetInt64(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Project = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAtEpoch = reader.GetInt64(4)
                });
            }
        }

        // Get observations after
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, title, type, project, created_at_epoch
                FROM observations
                WHERE created_at_epoch > @anchorEpoch
                """ + (project != null ? " AND project = @project" : "") + """
                ORDER BY created_at_epoch ASC
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@anchorEpoch", anchor.CreatedAtEpoch);
            cmd.Parameters.AddWithValue("@limit", depthAfter);
            if (project != null)
                cmd.Parameters.AddWithValue("@project", project);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.After.Add(new TimelineItem
                {
                    Id = reader.GetInt64(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Project = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAtEpoch = reader.GetInt64(4)
                });
            }
        }

        // Reverse 'before' to chronological order
        result.Before.Reverse();

        return result;
    }

    /// <summary>
    /// Find anchor observation by searching FTS5 and returning the top match.
    /// </summary>
    public long? FindAnchorByQuery(string query, string? project = null)
    {
        var results = SearchObservations(query, limit: 1, project: project);
        return results.Count > 0 ? results[0].Id : null;
    }

    private TimelineItem? GetObservationById(SqliteConnection conn, long id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, type, project, created_at_epoch FROM observations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new TimelineItem
            {
                Id = reader.GetInt64(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Type = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Project = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CreatedAtEpoch = reader.GetInt64(4)
            };
        }
        return null;
    }

    /// <summary>
    /// Escape special FTS5 query characters to prevent injection.
    /// </summary>
    private static string EscapeFts5Query(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";

        // Escape double quotes
        var escaped = query.Replace("\"", "\"\"");
        
        // Wrap in quotes to treat as phrase if it contains special chars
        if (escaped.Any(c => "+-*(){}[]^~:".Contains(c)))
        {
            escaped = $"\"{escaped}\"";
        }

        return escaped;
    }
}

public class FtsSearchResult
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string Project { get; set; } = "";
    public long CreatedAtEpoch { get; set; }
    public double Rank { get; set; }
    public string Snippet { get; set; } = "";
    
    /// <summary>
    /// Convert BM25 rank (lower = better) to 0-1 score (higher = better)
    /// </summary>
    public float NormalizedScore => 1f / (1f + Math.Max(0f, (float)-Rank));
}

public class TimelineResult
{
    public bool Found { get; set; }
    public TimelineItem? Anchor { get; set; }
    public List<TimelineItem> Before { get; set; } = new();
    public List<TimelineItem> After { get; set; } = new();
}

public class TimelineItem
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string Project { get; set; } = "";
    public long CreatedAtEpoch { get; set; }
}
