using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Worker.Models;
using ClaudeMem.Worker.Services;

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
            IBackgroundQueue queue) =>
        {
            queue.QueueObservation(new ObservationWorkItem(
                request.ContentSessionId,
                request.ToolName,
                request.ToolInput,
                request.ToolResponse,
                request.Cwd
            ));

            return Results.Ok(new
            {
                status = "queued"
            });
        });

        app.MapPost("/api/sessions/summarize", (
            SummarizeRequest request,
            IBackgroundQueue queue) =>
        {
            queue.QueueSummary(new SummaryWorkItem(
                request.ContentSessionId,
                request.LastAssistantMessage
            ));

            return Results.Ok(new
            {
                status = "queued"
            });
        });

        app.MapGet("/api/processing-status", (IBackgroundQueue queue) =>
        {
            var queueDepth = queue.ObservationQueueDepth + queue.SummaryQueueDepth;
            return Results.Ok(new
            {
                isProcessing = queueDepth > 0,
                queueDepth
            });
        });
    }
}
