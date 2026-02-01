using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services;
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
            if (string.IsNullOrWhiteSpace(request.ContentSessionId))
            {
                return Results.BadRequest(new
                {
                    error = "contentSessionId is required",
                    hint = "Expected fields: contentSessionId (string), project (string), prompt (string, optional)"
                });
            }

            if (string.IsNullOrWhiteSpace(request.Project))
            {
                return Results.BadRequest(new
                {
                    error = "project is required",
                    hint = "Expected fields: contentSessionId (string), project (string), prompt (string, optional)"
                });
            }

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

        /// <summary>
        /// Queue an observation. Auto-indexes for FTS5 (via trigger) and vector search (if enabled).
        /// </summary>
        app.MapPost("/api/sessions/observations", async (
            ObservationRequest request,
            ISessionRepository sessions,
            IObservationRepository observations,
            HybridSearchService? hybridSearch) =>
        {
            if (string.IsNullOrWhiteSpace(request.ContentSessionId))
            {
                return Results.BadRequest(new { error = "contentSessionId is required" });
            }

            // Get or create session
            var session = sessions.GetByContentSessionId(request.ContentSessionId);
            if (session == null)
            {
                // Auto-create session if needed
                session = new Session
                {
                    ContentSessionId = request.ContentSessionId,
                    Project = request.Cwd ?? "default",
                    StartedAt = DateTime.UtcNow
                };
                sessions.Create(session);
                session = sessions.GetByContentSessionId(request.ContentSessionId);
            }

            // Parse observation type
            var obsType = ObservationType.Discovery;
            if (!string.IsNullOrEmpty(request.ObservationType))
            {
                Enum.TryParse<ObservationType>(request.ObservationType, ignoreCase: true, out obsType);
            }

            // Create observation
            var observation = new Observation
            {
                MemorySessionId = session!.MemorySessionId ?? session.ContentSessionId,
                Project = session.Project,
                Type = obsType,
                Title = request.Title ?? request.ToolName,
                Text = request.ToolResponse ?? "",
                Narrative = request.Narrative,
                Facts = request.Facts ?? new List<string>(),
                Concepts = request.Concepts ?? new List<string>(),
                FilesRead = request.FilesRead ?? new List<string>(),
                FilesModified = request.FilesModified ?? new List<string>(),
                CreatedAt = DateTime.UtcNow
            };

            // Store observation (FTS5 auto-indexed via trigger)
            var obsId = observations.Store(observation);
            observation.Id = obsId;

            // Vector indexing (async, best-effort)
            if (hybridSearch?.VectorSearchAvailable == true)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await hybridSearch.IndexObservationAsync(observation);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AutoIndex] Vector indexing failed for obs #{obsId}: {ex.Message}");
                    }
                });
            }

            return Results.Ok(new
            {
                status = "stored",
                observationId = obsId,
                ftsIndexed = true,
                vectorIndexing = hybridSearch?.VectorSearchAvailable == true
            });
        });

        app.MapPost("/api/sessions/summarize", (
            SummarizeRequest request,
            ISessionRepository sessions,
            ISummaryRepository summaries) =>
        {
            if (string.IsNullOrWhiteSpace(request.ContentSessionId))
            {
                return Results.BadRequest(new { error = "contentSessionId is required" });
            }

            var session = sessions.GetByContentSessionId(request.ContentSessionId);
            if (session == null)
            {
                return Results.NotFound(new { error = "Session not found" });
            }

            // Create summary
            var summary = new Summary
            {
                MemorySessionId = session.MemorySessionId ?? session.ContentSessionId,
                Project = session.Project,
                Completed = request.LastAssistantMessage,
                CreatedAt = DateTime.UtcNow
            };

            var summaryId = summaries.Store(summary);

            return Results.Ok(new
            {
                status = "stored",
                summaryId
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

        /// <summary>
        /// Mark a session as complete (SessionEnd hook).
        /// </summary>
        app.MapPost("/api/sessions/complete", (
            SessionCompleteRequest request,
            ISessionRepository sessions) =>
        {
            if (string.IsNullOrWhiteSpace(request.ContentSessionId))
            {
                return Results.BadRequest(new { error = "contentSessionId is required" });
            }

            var session = sessions.GetByContentSessionId(request.ContentSessionId);
            if (session == null)
            {
                return Results.NotFound(new { error = "Session not found" });
            }

            // Mark session as completed
            sessions.MarkComplete(session.Id, request.Reason ?? "exit");

            return Results.Ok(new
            {
                status = "completed",
                sessionId = session.Id,
                reason = request.Reason ?? "exit"
            });
        });
    }
}

/// <summary>
/// Request model for creating observations.
/// </summary>
public class ObservationRequest
{
    public string? ContentSessionId { get; set; }
    public string? ToolName { get; set; }
    public object? ToolInput { get; set; }
    public string? ToolResponse { get; set; }
    public string? Cwd { get; set; }
    
    // Additional fields for structured observations
    public string? Title { get; set; }
    public string? Narrative { get; set; }
    public string? ObservationType { get; set; }
    public List<string>? Facts { get; set; }
    public List<string>? Concepts { get; set; }
    public List<string>? FilesRead { get; set; }
    public List<string>? FilesModified { get; set; }
}

public class SummarizeRequest
{
    public string? ContentSessionId { get; set; }
    public string? LastAssistantMessage { get; set; }
}

public class SessionCompleteRequest
{
    public string? ContentSessionId { get; set; }
    public string? Reason { get; set; }
}
