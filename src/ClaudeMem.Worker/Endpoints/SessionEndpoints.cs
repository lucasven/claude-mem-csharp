using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Worker.Models;

namespace ClaudeMem.Worker.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sessions/init", (
            SessionInitRequest request,
            ISessionRepository sessions) =>
        {
            var existing = sessions.GetByContentSessionId(request.ContentSessionId);
            if (existing != null)
            {
                return Results.Ok(new
                {
                    sessionDbId = existing.Id,
                    promptNumber = 1,
                    skipped = false
                });
            }

            var session = new Session
            {
                ContentSessionId = request.ContentSessionId,
                Project = request.Project,
                UserPrompt = request.Prompt,
                StartedAt = DateTime.UtcNow
            };

            var id = sessions.Create(session);
            return Results.Ok(new
            {
                sessionDbId = id,
                promptNumber = 1,
                skipped = false
            });
        });

        app.MapPost("/api/sessions/observations", (
            ObservationRequest request,
            ISessionRepository sessions) =>
        {
            // Queue observation for processing
            // For now, just acknowledge receipt
            return Results.Ok(new
            {
                status = "queued"
            });
        });

        app.MapPost("/api/sessions/summarize", (
            SummarizeRequest request,
            ISessionRepository sessions) =>
        {
            // Queue summary generation
            return Results.Ok(new
            {
                status = "queued"
            });
        });

        app.MapGet("/api/processing-status", () =>
        {
            return Results.Ok(new
            {
                isProcessing = false,
                queueDepth = 0
            });
        });
    }
}
