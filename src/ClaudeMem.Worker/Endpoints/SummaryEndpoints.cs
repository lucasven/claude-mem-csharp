using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Worker.Endpoints;

public static class SummaryEndpoints
{
    public static void MapSummaryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/summaries", (
            ISummaryRepository repo,
            int? offset,
            int? limit,
            string? project) =>
        {
            var actualLimit = Math.Min(limit ?? 20, 100);
            var summaries = repo.GetRecent(
                limit: actualLimit + 1, // Fetch one extra to check hasMore
                offset: offset ?? 0,
                project: project);

            var hasMore = summaries.Count > actualLimit;
            if (hasMore) summaries = summaries.Take(actualLimit).ToList();

            return Results.Ok(new
            {
                items = summaries,
                hasMore,
                offset = offset ?? 0,
                limit = actualLimit
            });
        });

        app.MapGet("/api/summary/{id:long}", (long id, ISummaryRepository repo) =>
        {
            var summary = repo.GetById(id);
            return summary is not null ? Results.Ok(summary) : Results.NotFound();
        });
    }
}
