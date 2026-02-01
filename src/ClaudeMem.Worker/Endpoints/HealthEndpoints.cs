using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services;

namespace ClaudeMem.Worker.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.MapGet("/api/stats", (ClaudeMem.Core.Data.ClaudeMemDatabase db,
            IObservationRepository observations,
            ISessionRepository sessions) =>
        {
            return Results.Ok(new
            {
                worker = new
                {
                    version = "1.0.0",
                    uptime = Environment.TickCount64 / 1000,
                    port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777"
                },
                observations = observations.GetCount(),
                sessions = sessions.GetCount()
            });
        });

        // Context injection endpoint for SessionStart hook
        app.MapGet("/api/context/inject", async (
            string? project,
            HybridSearchService search,
            IObservationRepository observations,
            ISummaryRepository summaries,
            CancellationToken ct) =>
        {
            var contextParts = new List<string>();
            
            // Get recent summaries for project
            var recentSummaries = summaries.GetRecent(limit: 3, project: project);
            if (recentSummaries.Any())
            {
                contextParts.Add("## Recent Session Summaries\n");
                foreach (var summary in recentSummaries)
                {
                    contextParts.Add($"### Session {summary.MemorySessionId}");
                    if (!string.IsNullOrEmpty(summary.Completed))
                        contextParts.Add($"**Completed:** {summary.Completed}");
                    contextParts.Add("");
                }
            }

            // Get recent observations for project
            var recentObs = observations.GetRecent(limit: 10, project: project);
            if (recentObs.Any())
            {
                contextParts.Add("## Recent Observations\n");
                foreach (var obs in recentObs.Take(5))
                {
                    contextParts.Add($"- [{obs.Type}] {obs.Title ?? obs.Narrative?.Substring(0, Math.Min(50, obs.Narrative?.Length ?? 0))} (#{obs.Id})");
                }
            }

            if (!contextParts.Any())
            {
                return Results.Ok("");
            }

            var context = string.Join("\n", contextParts);
            return Results.Ok(context);
        });
    }
}
