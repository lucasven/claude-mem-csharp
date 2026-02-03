using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Core.Services;
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
                MemorySessionId = request.ContentSessionId,
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
        /// Queue an observation. Optionally enriches with LLM if enrich=true.
        /// </summary>
        app.MapPost("/api/sessions/observations", async (
            ObservationRequest request,
            ISessionRepository sessions,
            IObservationRepository observations,
            IClaudeService? claudeService,
            HybridSearchService? hybridSearch,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ContentSessionId))
            {
                return Results.BadRequest(new { error = "contentSessionId is required" });
            }

            // Get or create session
            var session = sessions.GetByContentSessionId(request.ContentSessionId);
            if (session == null)
            {
                session = new Session
                {
                    ContentSessionId = request.ContentSessionId,
                    MemorySessionId = request.ContentSessionId,
                    Project = request.Cwd ?? "default",
                    StartedAt = DateTime.UtcNow
                };
                sessions.Create(session);
                session = sessions.GetByContentSessionId(request.ContentSessionId);
            }

            Observation observation;
            var enriched = false;

            // Try LLM enrichment if service available and enrich flag is true
            if (claudeService != null && request.Enrich == true)
            {
                try
                {
                    var enrichedObs = await claudeService.ExtractObservationAsync(
                        session!.MemorySessionId ?? session.ContentSessionId,
                        session.Project,
                        request.ToolName ?? "unknown",
                        request.ToolInput,
                        request.ToolResponse,
                        ct);

                    if (enrichedObs != null)
                    {
                        observation = enrichedObs;
                        enriched = true;
                    }
                    else
                    {
                        // LLM decided not to create observation
                        return Results.Ok(new { status = "skipped", reason = "LLM deemed not worth storing" });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Observation] LLM enrichment failed: {ex.Message}");
                    // Fall back to basic observation
                    observation = CreateBasicObservation(request, session!);
                }
            }
            else
            {
                // Create basic observation without LLM
                observation = CreateBasicObservation(request, session!);
            }

            // Store observation
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
                enriched,
                ftsIndexed = true,
                vectorIndexing = hybridSearch?.VectorSearchAvailable == true
            });
        });

        /// <summary>
        /// Generate session summary using LLM.
        /// </summary>
        app.MapPost("/api/sessions/summarize", async (
            SummarizeRequest request,
            ISessionRepository sessions,
            IObservationRepository observations,
            ISummaryRepository summaries,
            IClaudeService? claudeService,
            CancellationToken ct) =>
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

            Summary summary;

            // Try LLM summary generation
            if (claudeService != null)
            {
                try
                {
                    // Get observations for this session
                    var sessionObs = observations.GetBySessionId(session.MemorySessionId ?? session.ContentSessionId);
                    
                    var extraction = await claudeService.GenerateSummaryAsync(
                        session.MemorySessionId ?? session.ContentSessionId,
                        sessionObs,
                        request.LastAssistantMessage,
                        ct);

                    if (extraction != null)
                    {
                        summary = new Summary
                        {
                            MemorySessionId = session.MemorySessionId ?? session.ContentSessionId,
                            Project = session.Project,
                            Request = extraction.Request,
                            Investigated = extraction.Investigated,
                            Learned = extraction.Learned,
                            Completed = extraction.Completed,
                            NextSteps = extraction.NextSteps,
                            FilesRead = string.Join(", ", extraction.FilesRead ?? []),
                            FilesEdited = string.Join(", ", extraction.FilesEdited ?? []),
                            Notes = extraction.Notes,
                            CreatedAt = DateTime.UtcNow
                        };
                    }
                    else
                    {
                        summary = CreateBasicSummary(session, request.LastAssistantMessage);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Summary] LLM generation failed: {ex.Message}");
                    summary = CreateBasicSummary(session, request.LastAssistantMessage);
                }
            }
            else
            {
                summary = CreateBasicSummary(session, request.LastAssistantMessage);
            }

            var summaryId = summaries.Store(summary);

            return Results.Ok(new
            {
                status = "stored",
                summaryId,
                hasLlmContent = !string.IsNullOrEmpty(summary.Request) || !string.IsNullOrEmpty(summary.Learned)
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

            sessions.MarkComplete(session.Id, request.Reason ?? "exit");

            return Results.Ok(new
            {
                status = "completed",
                sessionId = session.Id,
                reason = request.Reason ?? "exit"
            });
        });
    }

    private static Observation CreateBasicObservation(ObservationRequest request, Session session)
    {
        var obsType = ObservationType.Discovery;
        if (!string.IsNullOrEmpty(request.ObservationType))
        {
            Enum.TryParse<ObservationType>(request.ObservationType, ignoreCase: true, out obsType);
        }

        return new Observation
        {
            MemorySessionId = session.MemorySessionId ?? session.ContentSessionId,
            Project = session.Project,
            Type = obsType,
            Title = request.Title ?? request.ToolName,
            Text = request.ToolResponse ?? "",
            Narrative = request.Narrative,
            Facts = request.Facts ?? [],
            Concepts = request.Concepts ?? [],
            FilesRead = request.FilesRead ?? [],
            FilesModified = request.FilesModified ?? [],
            DiscoveryTokens = request.DiscoveryTokens ?? 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static Summary CreateBasicSummary(Session session, string? lastMessage)
    {
        return new Summary
        {
            MemorySessionId = session.MemorySessionId ?? session.ContentSessionId,
            Project = session.Project,
            Completed = lastMessage,
            CreatedAt = DateTime.UtcNow
        };
    }
}

public class SessionInitRequest
{
    public string? ContentSessionId { get; set; }
    public string? Project { get; set; }
    public string? Prompt { get; set; }
}

public class ObservationRequest
{
    public string? ContentSessionId { get; set; }
    public string? ToolName { get; set; }
    public object? ToolInput { get; set; }
    public string? ToolResponse { get; set; }
    public string? Cwd { get; set; }
    public string? Title { get; set; }
    public string? Narrative { get; set; }
    public string? ObservationType { get; set; }
    public List<string>? Facts { get; set; }
    public List<string>? Concepts { get; set; }
    public List<string>? FilesRead { get; set; }
    public List<string>? FilesModified { get; set; }
    public int? DiscoveryTokens { get; set; }
    public bool? Enrich { get; set; } // Set to true to use LLM enrichment
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
