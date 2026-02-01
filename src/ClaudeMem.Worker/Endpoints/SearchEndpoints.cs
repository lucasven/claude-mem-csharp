using ClaudeMem.Core.Services;

namespace ClaudeMem.Worker.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", async (
            string query,
            string? project,
            string? type,
            int? limit,
            ChromaSyncService? chromaSync) =>
        {
            if (chromaSync == null)
            {
                return Results.BadRequest(new
                {
                    error = "Semantic search not configured",
                    hint = "ChromaSync service is not running"
                });
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new
                {
                    error = "query parameter is required"
                });
            }

            try
            {
                var results = await chromaSync.SearchAsync(
                    query,
                    limit ?? 10,
                    type
                );

                return Results.Ok(new
                {
                    query,
                    count = results.Count,
                    results = results.Select(r => new
                    {
                        observationId = r.ObservationId,
                        score = Math.Round(r.Score, 4),
                        type = r.Type,
                        title = r.Title,
                        preview = r.Content.Length > 200
                            ? r.Content[..200] + "..."
                            : r.Content
                    })
                });
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

        app.MapGet("/api/search/status", async (ChromaService? chroma) =>
        {
            if (chroma == null)
            {
                return Results.Ok(new
                {
                    enabled = false,
                    status = "not_configured",
                    message = "ChromaDB integration not configured"
                });
            }

            try
            {
                // Try to get any collection to verify connection
                await chroma.StartAsync();
                return Results.Ok(new
                {
                    enabled = true,
                    status = "connected",
                    message = "ChromaDB is running"
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    enabled = true,
                    status = "error",
                    message = ex.Message
                });
            }
        });
    }
}
