using System.Text.Json;
using ClaudeMem.Core.Models;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

namespace ClaudeMem.Worker.Services;

public class ClaudeService : IClaudeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string ObservationSystemPrompt = """
        You are an observation extractor for a coding memory system. Given a tool interaction from a Claude Code session,
        extract meaningful observations that would be useful to remember for future sessions.

        IMPORTANT: Only create an observation if the interaction contains genuinely useful information worth remembering.
        Skip routine operations like simple file reads or directory listings unless they reveal something significant.

        Respond ONLY with valid JSON in this exact format (no markdown, no explanation):
        {
            "shouldCreate": true/false,
            "type": "Decision|Bugfix|Feature|Refactor|Discovery",
            "title": "Brief title (max 60 chars)",
            "subtitle": "One-line summary",
            "narrative": "2-3 sentence explanation of what happened and why it matters",
            "facts": ["Specific fact 1", "Specific fact 2"],
            "concepts": ["concept1", "concept2"],
            "filesRead": ["path/to/file"],
            "filesModified": ["path/to/file"]
        }

        If the interaction is not worth remembering, respond with:
        {"shouldCreate": false}
        """;

    private const string SummarySystemPrompt = """
        You are a session summarizer for a coding memory system. Given a list of observations from a Claude Code session,
        create a structured summary that captures the key work done and decisions made.

        Respond ONLY with valid JSON in this exact format (no markdown, no explanation):
        {
            "request": "What the user originally asked for",
            "investigated": "What was explored or researched",
            "learned": "Key insights or discoveries made",
            "completed": "What was accomplished or implemented",
            "nextSteps": "Potential follow-up work or recommendations",
            "filesRead": ["path/to/file"],
            "filesEdited": ["path/to/file"],
            "notes": "Any additional context or important observations"
        }

        All fields are optional - only include fields that have meaningful content.
        """;

    public async Task<Observation?> ExtractObservationAsync(
        string sessionId,
        string project,
        string toolName,
        object? toolInput,
        object? toolResponse,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
            Tool: {toolName}
            Input: {SerializeObject(toolInput)}
            Response: {SerializeObject(toolResponse)}
            """;

        var options = ClaudeApi.Options()
            .SystemPrompt(ObservationSystemPrompt)
            .Model("claude-3-5-haiku-20241022")  // Fast, cheap model for extraction
            .MaxTurns(1)
            .Tools()                             // No tools needed - pure text generation
            .BypassPermissions()                 // Non-interactive mode
            .Build();

        var response = await GetResponseTextAsync(prompt, options, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
            return null;

        try
        {
            var result = JsonSerializer.Deserialize<ObservationExtraction>(response, JsonOptions);
            if (result == null || !result.ShouldCreate)
                return null;

            return new Observation
            {
                MemorySessionId = sessionId,
                Project = project,
                Type = ParseObservationType(result.Type),
                Title = result.Title,
                Subtitle = result.Subtitle,
                Narrative = result.Narrative,
                Text = prompt,
                Facts = result.Facts ?? [],
                Concepts = result.Concepts ?? [],
                FilesRead = result.FilesRead ?? [],
                FilesModified = result.FilesModified ?? [],
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<SummaryExtraction?> GenerateSummaryAsync(
        string sessionId,
        IEnumerable<Observation> observations,
        string? lastAssistantMessage,
        CancellationToken cancellationToken = default)
    {
        var observationList = observations.ToList();
        if (observationList.Count == 0)
            return null;

        var observationsText = string.Join("\n\n", observationList.Select((o, i) => $"""
            Observation {i + 1}: {o.Title}
            Type: {o.Type}
            {o.Narrative}
            Facts: {string.Join(", ", o.Facts)}
            Files read: {string.Join(", ", o.FilesRead)}
            Files modified: {string.Join(", ", o.FilesModified)}
            """));

        var prompt = $"""
            Session observations:
            {observationsText}

            {(lastAssistantMessage != null ? $"Last assistant message:\n{lastAssistantMessage}" : "")}
            """;

        var options = ClaudeApi.Options()
            .SystemPrompt(SummarySystemPrompt)
            .Model("claude-3-5-haiku-20241022")  // Fast, cheap model for summarization
            .MaxTurns(1)
            .Tools()                             // No tools needed - pure text generation
            .BypassPermissions()                 // Non-interactive mode
            .Build();

        var response = await GetResponseTextAsync(prompt, options, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SummaryExtraction>(response, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> GetResponseTextAsync(
        string prompt,
        ClaudeAgentOptions options,
        CancellationToken cancellationToken)
    {
        var responseText = new System.Text.StringBuilder();

        // Add stderr logging to diagnose issues
        var optionsWithLogging = ClaudeApi.Options()
            .SystemPrompt(options.SystemPrompt ?? "")
            .Model(options.Model ?? "claude-3-5-haiku-20241022")
            .MaxTurns(options.MaxTurns ?? 1)
            .Tools()
            .BypassPermissions()
            .OnStderr(line => Console.WriteLine($"[Claude CLI stderr] {line}"))
            .Build();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout

            await foreach (var message in ClaudeApi.QueryAsync(prompt, optionsWithLogging, cancellationToken: cts.Token))
            {
                Console.WriteLine($"[Claude CLI] Received message type: {message.GetType().Name}");
                if (message is AssistantMessage am)
                {
                    foreach (var block in am.Content)
                    {
                        if (block is TextBlock tb)
                            responseText.Append(tb.Text);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Claude CLI] Request timed out after 30 seconds");
            return "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Claude CLI] Error: {ex.Message}");
            throw;
        }

        return responseText.ToString().Trim();
    }

    private static string SerializeObject(object? obj)
    {
        if (obj == null) return "null";
        try
        {
            return JsonSerializer.Serialize(obj, JsonOptions);
        }
        catch
        {
            return obj.ToString() ?? "null";
        }
    }

    private static ObservationType ParseObservationType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "decision" => ObservationType.Decision,
            "bugfix" => ObservationType.Bugfix,
            "feature" => ObservationType.Feature,
            "refactor" => ObservationType.Refactor,
            _ => ObservationType.Discovery
        };
    }

    private record ObservationExtraction(
        bool ShouldCreate,
        string? Type,
        string? Title,
        string? Subtitle,
        string? Narrative,
        List<string>? Facts,
        List<string>? Concepts,
        List<string>? FilesRead,
        List<string>? FilesModified
    );
}
