using ClaudeMem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeMem.Worker.Endpoints;

public static class TimelineEndpoints
{
    public static void MapTimelineEndpoints(this WebApplication app)
    {
        /// <summary>
        /// Get chronological context around an observation.
        /// Part of the 3-layer workflow: search → timeline → get_observations
        /// </summary>
        app.MapGet("/api/timeline", (
            [FromQuery] long? anchor,
            [FromQuery] string? query,
            [FromQuery] int? depthBefore,
            [FromQuery] int? depthAfter,
            [FromQuery] string? project,
            [FromServices] HybridSearchService? hybridSearch,
            [FromServices] FullTextSearchService? ftsSearch) =>
        {
            // Prefer hybrid search, fall back to FTS
            if (hybridSearch != null)
            {
                if (anchor.HasValue)
                {
                    var result = hybridSearch.GetTimeline(
                        anchor.Value,
                        depthBefore ?? 3,
                        depthAfter ?? 3);

                    return FormatTimelineResult(result);
                }
                else if (!string.IsNullOrEmpty(query))
                {
                    var result = hybridSearch.GetTimelineByQuery(
                        query,
                        depthBefore ?? 3,
                        depthAfter ?? 3);

                    return FormatTimelineResult(result);
                }
            }
            else if (ftsSearch != null)
            {
                if (anchor.HasValue)
                {
                    var result = ftsSearch.GetTimeline(
                        anchor.Value,
                        depthBefore ?? 3,
                        depthAfter ?? 3,
                        project);

                    return FormatTimelineResult(result);
                }
                else if (!string.IsNullOrEmpty(query))
                {
                    var anchorId = ftsSearch.FindAnchorByQuery(query, project);
                    if (!anchorId.HasValue)
                    {
                        return Results.Ok(new
                        {
                            found = false,
                            message = "No matching observation found for query"
                        });
                    }

                    var result = ftsSearch.GetTimeline(
                        anchorId.Value,
                        depthBefore ?? 3,
                        depthAfter ?? 3,
                        project);

                    return FormatTimelineResult(result);
                }
            }

            return Results.BadRequest(new
            {
                error = "Either 'anchor' (observation ID) or 'query' parameter is required",
                usage = new
                {
                    byId = "/api/timeline?anchor=123&depthBefore=3&depthAfter=3",
                    byQuery = "/api/timeline?query=authentication%20bug&depthBefore=3&depthAfter=3"
                }
            });
        });
    }

    private static IResult FormatTimelineResult(TimelineResult result)
    {
        if (!result.Found)
        {
            return Results.Ok(new
            {
                found = false,
                message = "Observation not found"
            });
        }

        return Results.Ok(new
        {
            found = true,
            anchor = new
            {
                id = result.Anchor!.Id,
                title = result.Anchor.Title,
                type = result.Anchor.Type,
                project = result.Anchor.Project,
                createdAtEpoch = result.Anchor.CreatedAtEpoch
            },
            before = result.Before.Select(o => new
            {
                id = o.Id,
                title = o.Title,
                type = o.Type,
                createdAtEpoch = o.CreatedAtEpoch
            }),
            after = result.After.Select(o => new
            {
                id = o.Id,
                title = o.Title,
                type = o.Type,
                createdAtEpoch = o.CreatedAtEpoch
            }),
            context = $"Showing {result.Before.Count} observations before and {result.After.Count} after anchor #{result.Anchor.Id}"
        });
    }
}
