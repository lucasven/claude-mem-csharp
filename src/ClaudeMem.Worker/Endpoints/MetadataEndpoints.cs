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
                    port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37778"
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
    }
}
