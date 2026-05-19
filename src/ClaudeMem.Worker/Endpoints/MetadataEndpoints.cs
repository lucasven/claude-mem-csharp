using ClaudeMem.Core.Data;
using ClaudeMem.Core.Repositories;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Worker.Endpoints;

public static class MetadataEndpoints
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public static void MapMetadataEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stats", (
            ClaudeMemDatabase db,
            IObservationRepository obsRepo,
            ISessionRepository sessionRepo,
            ISummaryRepository summaryRepo) =>
        {
            var uptime = (int)(DateTime.UtcNow - StartTime).TotalSeconds;
            var observationCount = obsRepo.GetCount();
            var sessionCount = sessionRepo.GetCount();
            var summaryCount = summaryRepo.GetCount();

            var dbPath = db.GetDatabasePath();
            var dbSize = dbPath != ":memory:" && File.Exists(dbPath)
                ? new FileInfo(dbPath).Length
                : 0;

            return Results.Ok(new
            {
                worker = new
                {
                    version = "1.0.0",
                    uptime,
                    activeSessions = 0,
                    sseClients = 0,
                    port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777"
                },
                database = new
                {
                    path = dbPath,
                    size = dbSize,
                    observations = observationCount,
                    sessions = sessionCount,
                    summaries = summaryCount
                }
            });
        });

        app.MapGet("/api/projects", (ClaudeMemDatabase db) =>
        {
            var projects = new List<string>();
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT project
                FROM observations
                WHERE project IS NOT NULL
                GROUP BY project
                ORDER BY MAX(created_at_epoch) DESC
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    projects.Add(reader.GetString(0));
            }

            return Results.Ok(new { projects });
        });

        app.MapGet("/api/processing-status", () =>
        {
            // TODO: Implement actual processing queue tracking
            return Results.Ok(new { isProcessing = false, queueDepth = 0 });
        });

        app.MapPost("/api/processing", () =>
        {
            return Results.Ok(new { status = "ok", isProcessing = false, queueDepth = 0, activeSessions = 0 });
        });

        // Settings endpoint - stub for viewer UI compatibility
        app.MapGet("/api/settings", () =>
        {
            return Results.Ok(new
            {
                contextInjection = true,
                maxObservations = 50,
                maxSummaries = 10,
                maxPrompts = 5,
                vectorSearch = Environment.GetEnvironmentVariable("CLAUDE_MEM_VECTOR_ENABLED") != "false"
            });
        });

        app.MapPost("/api/settings", () =>
        {
            return Results.Ok(new { status = "ok" });
        });

        // Logs endpoint - stub for viewer UI compatibility
        app.MapGet("/api/logs", () =>
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude-mem-csharp", "hooks.log");
            var lines = new List<string>();
            if (File.Exists(logPath))
            {
                lines = File.ReadLines(logPath).TakeLast(100).ToList();
            }
            return Results.Ok(new { logs = lines });
        });

        app.MapPost("/api/logs/clear", () =>
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude-mem-csharp", "hooks.log");
            if (File.Exists(logPath))
                File.WriteAllText(logPath, "");
            return Results.Ok(new { status = "cleared" });
        });

        // Context preview endpoint - stub for viewer UI compatibility
        app.MapGet("/api/context/preview", (string? project) =>
        {
            return Results.Ok(new
            {
                context = "",
                tokenCount = 0,
                project = project ?? "default"
            });
        });
    }
}
