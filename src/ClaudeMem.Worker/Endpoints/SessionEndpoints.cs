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
            ISessionRepository sessions,
            IUserPromptRepository prompts) =>
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
            int promptNumber = 1;

            if (existing != null)
            {
                promptNumber = (int)(prompts.GetCount(request.Project) + 1);
            }
            else
            {
                var session = new Session
                {
                    ContentSessionId = request.ContentSessionId,
                    MemorySessionId = request.ContentSessionId,
                    Project = request.Project,
                    UserPrompt = request.Prompt,
                    StartedAt = DateTime.UtcNow
                };
                sessions.Create(session);
                existing = sessions.GetByContentSessionId(request.ContentSessionId);
            }

            // Store user prompt if provided
            if (!string.IsNullOrWhiteSpace(request.Prompt))
            {
                try
                {
                    prompts.Store(new UserPrompt
                    {
                        ContentSessionId = request.ContentSessionId,
                        Project = request.Project,
                        PromptNumber = promptNumber,
                        PromptText = request.Prompt,
                        MemorySessionId = existing?.MemorySessionId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SessionInit] Failed to store prompt: {ex.Message}");
                }
            }

            return Results.Ok(new
            {
                sessionDbId = existing!.Id,
                promptNumber,
                skipped = false
            });
        });

        /// <summary>
        /// Store an observation. Basic observation is stored immediately.
        /// LLM enrichment is opt-in (set enrich=true or CLAUDE_MEM_ENRICH=true).
        /// </summary>
        app.MapPost("/api/sessions/observations", (
            ObservationRequest request,
            ISessionRepository sessions,
            IObservationRepository observations,
            IClaudeService? claudeService,
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

            // Always store basic observation immediately (fast path)
            var observation = CreateBasicObservation(request, session!);
            var obsId = observations.Store(observation);
            observation.Id = obsId;

            // LLM enrichment: opt-in via request param or env var
            var enrichEnabled = request.Enrich == true
                || Environment.GetEnvironmentVariable("CLAUDE_MEM_ENRICH") == "true";

            if (enrichEnabled && claudeService != null)
            {
                // Fire-and-forget: enrich asynchronously without blocking response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var enrichedObs = await claudeService.ExtractObservationAsync(
                            session!.MemorySessionId ?? session.ContentSessionId,
                            session.Project,
                            request.ToolName ?? "unknown",
                            request.ToolInput,
                            request.ToolResponse,
                            CancellationToken.None);

                        if (enrichedObs != null)
                        {
                            // Could update the stored observation here in the future
                            Console.WriteLine($"[Observation] LLM enrichment completed for obs #{obsId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Observation] LLM enrichment failed for obs #{obsId}: {ex.Message}");
                    }
                });
            }

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

        /// <summary>
        /// Generate session summary using LLM, with observation-based fallback.
        /// </summary>
        app.MapPost("/api/sessions/summarize", async (
            SummarizeRequest request,
            ISessionRepository sessions,
            IObservationRepository observations,
            ISummaryRepository summaries,
            IUserPromptRepository prompts,
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

            var memSessionId = session.MemorySessionId ?? session.ContentSessionId;
            var sessionObs = observations.GetBySessionId(memSessionId);

            Summary summary;
            var usedLlm = false;

            // Try LLM summary generation
            if (claudeService != null)
            {
                try
                {
                    var extraction = await claudeService.GenerateSummaryAsync(
                        memSessionId,
                        sessionObs,
                        request.LastAssistantMessage,
                        ct);

                    if (extraction != null)
                    {
                        summary = new Summary
                        {
                            MemorySessionId = memSessionId,
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
                        usedLlm = true;
                    }
                    else
                    {
                        summary = BuildSummaryFromObservations(session, sessionObs, request, prompts);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Summary] LLM generation failed: {ex.Message}");
                    summary = BuildSummaryFromObservations(session, sessionObs, request, prompts);
                }
            }
            else
            {
                summary = BuildSummaryFromObservations(session, sessionObs, request, prompts);
            }

            var summaryId = summaries.Store(summary);

            return Results.Ok(new
            {
                status = "stored",
                summaryId,
                usedLlm,
                observationCount = sessionObs.Count
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

    /// <summary>
    /// Build a meaningful summary from observations + prompts when LLM is unavailable.
    /// </summary>
    private static Summary BuildSummaryFromObservations(
        Session session,
        List<Observation> sessionObs,
        SummarizeRequest request,
        IUserPromptRepository prompts)
    {
        var memSessionId = session.MemorySessionId ?? session.ContentSessionId;

        // Request: use first user prompt or session's stored prompt
        string? requestText = request.LastUserMessage;
        if (string.IsNullOrWhiteSpace(requestText))
        {
            requestText = session.UserPrompt;
        }
        if (string.IsNullOrWhiteSpace(requestText))
        {
            // Try to get the first stored prompt for this session
            var sessionPrompts = prompts.GetRecent(1, 0, session.Project);
            if (sessionPrompts.Count > 0)
                requestText = sessionPrompts[0].PromptText;
        }

        // Collect files read and edited from observations
        var filesRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesEdited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toolsUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var obs in sessionObs)
        {
            if (obs.FilesRead != null)
                foreach (var f in obs.FilesRead.Where(f => !string.IsNullOrWhiteSpace(f)))
                    filesRead.Add(f);
            if (obs.FilesModified != null)
                foreach (var f in obs.FilesModified.Where(f => !string.IsNullOrWhiteSpace(f)))
                    filesEdited.Add(f);
            if (!string.IsNullOrWhiteSpace(obs.Title))
            {
                // Extract tool name from title (e.g., "Read: path..." -> "Read")
                var colonIdx = obs.Title.IndexOf(':');
                if (colonIdx > 0)
                    toolsUsed.Add(obs.Title[..colonIdx].Trim());
            }
        }

        // Investigated: summarize what tools were used and how many observations
        string? investigated = null;
        if (sessionObs.Count > 0)
        {
            var parts = new List<string>();
            if (toolsUsed.Count > 0)
                parts.Add($"Tools used: {string.Join(", ", toolsUsed)}");
            parts.Add($"{sessionObs.Count} tool interaction(s) recorded");
            if (filesRead.Count > 0)
                parts.Add($"{filesRead.Count} file(s) examined");
            investigated = string.Join(". ", parts);
        }

        // Completed: use last assistant message
        var completed = request.LastAssistantMessage;

        // Notes: include discovery-type observations' titles
        var discoveries = sessionObs
            .Where(o => o.Type == ObservationType.Discovery && !string.IsNullOrWhiteSpace(o.Title))
            .Select(o => o.Title!)
            .Take(5)
            .ToList();
        string? notes = discoveries.Count > 0
            ? "Key discoveries: " + string.Join("; ", discoveries)
            : null;

        return new Summary
        {
            MemorySessionId = memSessionId,
            Project = session.Project,
            Request = requestText,
            Investigated = investigated,
            Completed = completed,
            FilesRead = filesRead.Count > 0 ? string.Join(", ", filesRead.Take(20)) : null,
            FilesEdited = filesEdited.Count > 0 ? string.Join(", ", filesEdited.Take(20)) : null,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };
    }
}

// JsonPropertyName attributes ensure hooks sending camelCase still bind correctly
// despite the global snake_case naming policy
public class SessionInitRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("contentSessionId")]
    public string? ContentSessionId { get; set; }
    public string? Project { get; set; }
    public string? Prompt { get; set; }
}

public class ObservationRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("contentSessionId")]
    public string? ContentSessionId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("toolName")]
    public string? ToolName { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("toolInput")]
    public object? ToolInput { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("toolResponse")]
    public string? ToolResponse { get; set; }
    public string? Cwd { get; set; }
    public string? Title { get; set; }
    public string? Narrative { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("observationType")]
    public string? ObservationType { get; set; }
    public List<string>? Facts { get; set; }
    public List<string>? Concepts { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("filesRead")]
    public List<string>? FilesRead { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("filesModified")]
    public List<string>? FilesModified { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("discoveryTokens")]
    public int? DiscoveryTokens { get; set; }
    public bool? Enrich { get; set; }
}

public class SummarizeRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("contentSessionId")]
    public string? ContentSessionId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("lastUserMessage")]
    public string? LastUserMessage { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("lastAssistantMessage")]
    public string? LastAssistantMessage { get; set; }
}

public class SessionCompleteRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("contentSessionId")]
    public string? ContentSessionId { get; set; }
    public string? Reason { get; set; }
}
