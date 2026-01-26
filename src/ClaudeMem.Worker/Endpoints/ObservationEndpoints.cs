using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Worker.Endpoints;

public static class ObservationEndpoints
{
    public static void MapObservationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/observations", (
            IObservationRepository repo,
            int? offset,
            int? limit,
            string? project) =>
        {
            var actualLimit = Math.Min(limit ?? 20, 100);
            var observations = repo.GetRecent(
                limit: actualLimit + 1, // Fetch one extra to check hasMore
                offset: offset ?? 0,
                project: project);

            var hasMore = observations.Count > actualLimit;
            if (hasMore) observations = observations.Take(actualLimit).ToList();

            return Results.Ok(new
            {
                items = observations,
                hasMore,
                offset = offset ?? 0,
                limit = actualLimit
            });
        });

        app.MapGet("/api/observation/{id:long}", (long id, IObservationRepository repo) =>
        {
            var observation = repo.GetById(id);
            return observation is not null
                ? Results.Ok(observation)
                : Results.NotFound();
        });

        app.MapPost("/api/observations/batch", (
            BatchRequest request,
            IObservationRepository repo) =>
        {
            var observations = repo.GetByIds(request.Ids);
            return Results.Ok(observations);
        });
    }

    public record BatchRequest(long[] Ids);
}
