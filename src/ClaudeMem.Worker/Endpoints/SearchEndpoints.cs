using ClaudeMem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeMem.Worker.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        /// <summary>
        /// Hybrid search combining FTS5 keyword search with vector semantic search.
        /// Part of the 3-layer workflow: search → timeline → get_observations
        /// </summary>
        app.MapGet("/api/search", async (
            [FromQuery] string query,
            [FromQuery] string? type,
            [FromQuery] int? limit,
            [FromQuery] string? project,
            [FromQuery] long? dateStart,
            [FromQuery] long? dateEnd,
            [FromServices] HybridSearchService? hybridSearch,
            [FromServices] FullTextSearchService? ftsSearch) =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "query parameter is required" });
            }

            try
            {
                // Prefer hybrid search (FTS + vector), fall back to FTS only
                if (hybridSearch != null)
                {
                    var results = await hybridSearch.SearchAsync(
                        query,
                        limit ?? 10,
                        type
                    );

                    return Results.Ok(new
                    {
                        query,
                        mode = hybridSearch.SearchMode,
                        count = results.Count,
                        results = results.Select(r => new
                        {
                            observationId = r.ObservationId,
                            score = Math.Round(r.HybridScore, 4),
                            ftsScore = Math.Round(r.FtsScore, 4),
                            vectorScore = Math.Round(r.VectorScore, 4),
                            type = r.Type,
                            title = r.Title,
                            preview = r.Snippet.Length > 200 ? r.Snippet[..200] + "..." : r.Snippet
                        }),
                        hint = "Use /api/timeline?anchor=<id> to get chronological context, then /api/observations/batch to fetch full details"
                    });
                }
                else if (ftsSearch != null)
                {
                    var results = ftsSearch.SearchObservations(
                        query,
                        limit ?? 10,
                        type,
                        project,
                        dateStart,
                        dateEnd
                    );

                    return Results.Ok(new
                    {
                        query,
                        mode = "fts5",
                        count = results.Count,
                        results = results.Select(r => new
                        {
                            observationId = r.Id,
                            score = Math.Round(r.NormalizedScore, 4),
                            type = r.Type,
                            title = r.Title,
                            preview = r.Snippet.Length > 200 ? r.Snippet[..200] + "..." : r.Snippet
                        }),
                        hint = "Use /api/timeline?anchor=<id> to get chronological context"
                    });
                }
                else
                {
                    return Results.BadRequest(new
                    {
                        error = "Search not configured",
                        hint = "Ensure database migrations have been applied"
                    });
                }
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Search failed",
                    statusCode: 500
                );
            }
        });

        /// <summary>
        /// Get search service status.
        /// </summary>
        app.MapGet("/api/search/status", async (
            [FromServices] HybridSearchService? hybridSearch,
            [FromServices] FullTextSearchService? ftsSearch) =>
        {
            if (hybridSearch != null)
            {
                try
                {
                    var status = await hybridSearch.GetStatusAsync();
                    return Results.Ok(new
                    {
                        status = "ready",
                        mode = status.Mode,
                        fts = new
                        {
                            available = status.FtsAvailable,
                            engine = "sqlite_fts5"
                        },
                        vector = new
                        {
                            available = status.VectorAvailable,
                            embeddingProvider = status.EmbeddingProvider,
                            embeddingAvailable = status.EmbeddingAvailable,
                            vectorStore = status.VectorStore,
                            vectorStoreAvailable = status.VectorStoreAvailable,
                            documentCount = status.DocumentCount,
                            dimension = status.Dimension
                        }
                    });
                }
                catch (Exception ex)
                {
                    return Results.Ok(new
                    {
                        status = "error",
                        message = ex.Message
                    });
                }
            }
            else if (ftsSearch != null)
            {
                return Results.Ok(new
                {
                    status = "ready",
                    mode = "fts5",
                    fts = new
                    {
                        available = true,
                        engine = "sqlite_fts5"
                    },
                    vector = new
                    {
                        available = false,
                        message = "Vector search not configured"
                    }
                });
            }
            else
            {
                return Results.Ok(new
                {
                    status = "not_configured",
                    message = "Search services not available"
                });
            }
        });

        /// <summary>
        /// Rebuild FTS5 index from existing observations.
        /// </summary>
        app.MapPost("/api/search/rebuild-fts", (
            [FromServices] FullTextSearchService? ftsSearch,
            [FromServices] ClaudeMem.Core.Data.ClaudeMemDatabase db) =>
        {
            if (ftsSearch == null)
            {
                return Results.BadRequest(new { error = "FTS search not configured" });
            }

            try
            {
                var conn = db.GetConnection();  // Shared singleton - dont dispose
                using var cmd = conn.CreateCommand();
                
                // Rebuild FTS index
                cmd.CommandText = """
                    DELETE FROM observations_fts;
                    INSERT INTO observations_fts(rowid, title, subtitle, narrative, text, facts, concepts)
                    SELECT id, title, subtitle, narrative, text, facts, concepts FROM observations;
                    """;
                cmd.ExecuteNonQuery();

                return Results.Ok(new
                {
                    status = "rebuilt",
                    message = "FTS5 index has been rebuilt from observations table"
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Rebuild failed",
                    statusCode: 500
                );
            }
        });
    }
}
