using ClaudeMem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeMem.Worker.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", async (
            [FromQuery] string query,
            [FromQuery] string? type,
            [FromQuery] int? limit,
            [FromServices] SemanticSearchService? searchService) =>
        {
            if (searchService == null)
            {
                return Results.BadRequest(new
                {
                    error = "Semantic search not configured",
                    hint = "Set CLAUDE_MEM_SEARCH_ENABLED=true and configure embedding provider"
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
                var results = await searchService.SearchAsync(
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

        app.MapGet("/api/search/status", async ([FromServices] SemanticSearchService? searchService) =>
        {
            if (searchService == null)
            {
                return Results.Ok(new
                {
                    enabled = false,
                    status = "not_configured",
                    message = "Semantic search not configured",
                    hint = "Set CLAUDE_MEM_SEARCH_ENABLED=true"
                });
            }

            try
            {
                var status = await searchService.GetStatusAsync();
                return Results.Ok(new
                {
                    enabled = status.Enabled,
                    status = status.EmbeddingAvailable && status.VectorStoreAvailable ? "ready" : "degraded",
                    embeddingProvider = status.EmbeddingProvider,
                    embeddingAvailable = status.EmbeddingAvailable,
                    vectorStore = status.VectorStore,
                    vectorStoreAvailable = status.VectorStoreAvailable,
                    collection = status.CollectionName,
                    documentCount = status.DocumentCount,
                    dimension = status.Dimension
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
