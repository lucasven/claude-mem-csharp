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
            var observations = repo.GetRecent(
                limit: limit ?? 20,
                offset: offset ?? 0,
                project: project);
            var total = repo.GetCount(project);

            return Results.Ok(new { total, observations });
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
