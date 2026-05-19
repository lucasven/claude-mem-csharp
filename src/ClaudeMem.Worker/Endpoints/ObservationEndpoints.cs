using System.Text.Json;
using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Worker.Endpoints;

public static class ObservationEndpoints
{
    // Serialize list fields as JSON strings — the viewer JS expects to JSON.parse() them
    private static string ToJsonString(List<string>? list) =>
        list is { Count: > 0 } ? JsonSerializer.Serialize(list) : "[]";

    private static object MapObservation(ClaudeMem.Core.Models.Observation o) => new
    {
        o.Id,
        o.MemorySessionId,
        o.Project,
        type = o.Type.ToString().ToLowerInvariant(),
        o.Title,
        o.Subtitle,
        o.Narrative,
        o.Text,
        facts = ToJsonString(o.Facts),
        concepts = ToJsonString(o.Concepts),
        files_read = ToJsonString(o.FilesRead),
        files_modified = ToJsonString(o.FilesModified),
        o.PromptNumber,
        o.DiscoveryTokens,
        o.CreatedAt,
        o.CreatedAtEpoch
    };

    public static void MapObservationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/observations", (
            IObservationRepository repo,
            int? offset,
            int? limit,
            string? project) =>
        {
            var actualLimit = limit ?? 20;
            var actualOffset = offset ?? 0;
            var items = repo.GetRecent(
                limit: actualLimit,
                offset: actualOffset,
                project: project);
            var total = repo.GetCount(project);
            var hasMore = actualOffset + items.Count < total;

            return Results.Ok(new { items = items.Select(MapObservation), hasMore, offset = actualOffset, limit = actualLimit });
        });

        app.MapGet("/api/observation/{id:long}", (long id, IObservationRepository repo) =>
        {
            var observation = repo.GetById(id);
            return observation is not null
                ? Results.Ok(MapObservation(observation))
                : Results.NotFound();
        });

        app.MapPost("/api/observations/batch", (
            BatchRequest request,
            IObservationRepository repo) =>
        {
            var observations = repo.GetByIds(request.Ids);
            return Results.Ok(observations.Select(MapObservation));
        });
    }

    public record BatchRequest(long[] Ids);
}
