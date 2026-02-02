using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services;

namespace ClaudeMem.Worker.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        // Root endpoint with status dashboard
        app.MapGet("/", (ClaudeMem.Core.Data.ClaudeMemDatabase db,
            IObservationRepository observations,
            ISessionRepository sessions) =>
        {
            var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777";
            var obsCount = observations.GetCount();
            var sessionCount = sessions.GetCount();
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            
            var html = $"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>Claude-Mem C# Worker</title>
                <style>
                    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; 
                           max-width: 800px; margin: 50px auto; padding: 20px; background: #1a1a2e; color: #eee; }}
                    h1 {{ color: #00d9ff; }}
                    .status {{ background: #16213e; padding: 20px; border-radius: 8px; margin: 20px 0; }}
                    .stat {{ display: inline-block; margin: 10px 20px; text-align: center; }}
                    .stat-value {{ font-size: 2em; color: #00d9ff; }}
                    .stat-label {{ color: #888; }}
                    .endpoint {{ background: #0f3460; padding: 10px; margin: 5px 0; border-radius: 4px; font-family: monospace; }}
                    .healthy {{ color: #00ff88; }}
                    a {{ color: #00d9ff; }}
                </style>
            </head>
            <body>
                <h1>🧠 Claude-Mem C# Worker</h1>
                <div class="status">
                    <span class="healthy">● Healthy</span> | Port: {port} | Uptime: {uptime.Hours}h {uptime.Minutes}m
                </div>
                
                <div class="status">
                    <div class="stat">
                        <div class="stat-value">{obsCount}</div>
                        <div class="stat-label">Observations</div>
                    </div>
                    <div class="stat">
                        <div class="stat-value">{sessionCount}</div>
                        <div class="stat-label">Sessions</div>
                    </div>
                </div>
                
                <h2>API Endpoints</h2>
                <div class="endpoint">GET <a href="/health">/health</a> - Health check</div>
                <div class="endpoint">GET <a href="/api/stats">/api/stats</a> - Worker statistics</div>
                <div class="endpoint">GET /api/search?query=... - Hybrid search</div>
                <div class="endpoint">GET /api/observations - List observations</div>
                <div class="endpoint">GET /api/observation/{{id}} - Get observation by ID</div>
                <div class="endpoint">POST /api/sessions/init - Initialize session</div>
                <div class="endpoint">POST /api/sessions/observations - Store observation</div>
                
                <h2>Data Location</h2>
                <div class="endpoint">~/.claude-mem-csharp/claude-mem.db</div>
            </body>
            </html>
            """;
            
            return Results.Content(html, "text/html");
        });

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
