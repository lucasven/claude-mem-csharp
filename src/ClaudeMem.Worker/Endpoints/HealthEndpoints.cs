namespace ClaudeMem.Worker.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.MapGet("/api/stats", (ClaudeMem.Core.Data.ClaudeMemDatabase db,
            ClaudeMem.Core.Repositories.IObservationRepository observations,
            ClaudeMem.Core.Repositories.ISessionRepository sessions) =>
        {
            return Results.Ok(new
            {
                worker = new
                {
                    version = "1.0.0",
                    uptime = Environment.TickCount64 / 1000,
                    port = 37777
                },
                database = new
                {
                    observations = observations.GetCount(),
                }
            });
        });
    }
}
