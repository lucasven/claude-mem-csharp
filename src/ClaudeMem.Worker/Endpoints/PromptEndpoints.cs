using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Worker.Endpoints;

public static class PromptEndpoints
{
    public static void MapPromptEndpoints(this WebApplication app)
    {
        app.MapGet("/api/prompts", (
            IUserPromptRepository repo,
            int? offset,
            int? limit,
            string? project) =>
        {
            var actualLimit = Math.Min(limit ?? 20, 100);
            var prompts = repo.GetRecent(actualLimit + 1, offset ?? 0, project);

            var hasMore = prompts.Count > actualLimit;
            if (hasMore) prompts = prompts.Take(actualLimit).ToList();

            return Results.Ok(new
            {
                items = prompts,
                hasMore,
                offset = offset ?? 0,
                limit = actualLimit
            });
        });

        app.MapGet("/api/prompt/{id:long}", (long id, IUserPromptRepository repo) =>
        {
            var prompt = repo.GetById(id);
            return prompt is not null ? Results.Ok(prompt) : Results.NotFound();
        });
    }
}
