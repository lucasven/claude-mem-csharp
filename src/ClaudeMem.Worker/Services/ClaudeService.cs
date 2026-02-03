using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeMem.Core.Models;

namespace ClaudeMem.Worker.Services;

/// <summary>
/// LLM service for observation extraction and session summarization.
/// Supports Anthropic Claude API (preferred) or OpenAI API (fallback).
/// </summary>
public class ClaudeService : IClaudeService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string? _anthropicApiKey;
    private readonly string? _openAiApiKey;
    private readonly string _provider;
    private readonly string _model;

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

    public ClaudeService()
    {
        _anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        // Determine provider and model
        if (!string.IsNullOrEmpty(_anthropicApiKey))
        {
            _provider = "anthropic";
            _model = Environment.GetEnvironmentVariable("CLAUDE_MEM_LLM_MODEL") ?? "claude-3-5-haiku-20241022";
            Console.WriteLine($"[ClaudeService] Using Anthropic API with model: {_model}");
        }
        else if (!string.IsNullOrEmpty(_openAiApiKey))
        {
            _provider = "openai";
            _model = Environment.GetEnvironmentVariable("CLAUDE_MEM_LLM_MODEL") ?? "gpt-4o-mini";
            Console.WriteLine($"[ClaudeService] Using OpenAI API with model: {_model}");
        }
        else
        {
            _provider = "none";
            _model = "";
            Console.WriteLine("[ClaudeService] No API key found (ANTHROPIC_API_KEY or OPENAI_API_KEY). LLM features disabled.");
        }
    }

    public bool IsAvailable => _provider != "none";

    public async Task<Observation?> ExtractObservationAsync(
        string sessionId,
        string project,
        string toolName,
        object? toolInput,
        object? toolResponse,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return null;

        var prompt = $"""
            Tool: {toolName}
            Input: {SerializeObject(toolInput)}
            Response: {SerializeObject(toolResponse)}
            """;

        try
        {
            var response = await CallLlmAsync(ObservationSystemPrompt, prompt, cancellationToken);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            // Extract JSON from response (handle markdown code blocks)
            response = ExtractJson(response);
            
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
        catch (Exception ex)
        {
            Console.WriteLine($"[ClaudeService] ExtractObservation failed: {ex.Message}");
            return null;
        }
    }

    public async Task<SummaryExtraction?> GenerateSummaryAsync(
        string sessionId,
        IEnumerable<Observation> observations,
        string? lastAssistantMessage,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return null;

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

        try
        {
            var response = await CallLlmAsync(SummarySystemPrompt, prompt, cancellationToken);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            // Extract JSON from response
            response = ExtractJson(response);
            
            return JsonSerializer.Deserialize<SummaryExtraction>(response, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClaudeService] GenerateSummary failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string> CallLlmAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        return _provider switch
        {
            "anthropic" => await CallAnthropicAsync(systemPrompt, userPrompt, cancellationToken),
            "openai" => await CallOpenAiAsync(systemPrompt, userPrompt, cancellationToken),
            _ => throw new InvalidOperationException("No LLM provider configured")
        };
    }

    private async Task<string> CallAnthropicAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _model,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", _anthropicApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Anthropic API error: {response.StatusCode} - {responseContent}");
        }

        using var doc = JsonDocument.Parse(responseContent);
        var content = doc.RootElement.GetProperty("content");
        if (content.GetArrayLength() > 0)
        {
            var textBlock = content[0];
            if (textBlock.TryGetProperty("text", out var text))
                return text.GetString() ?? "";
        }

        return "";
    }

    private async Task<string> CallOpenAiAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _model,
            max_tokens = 1024,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI API error: {response.StatusCode} - {responseContent}");
        }

        using var doc = JsonDocument.Parse(responseContent);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message");
            if (message.TryGetProperty("content", out var content))
                return content.GetString() ?? "";
        }

        return "";
    }

    private static string ExtractJson(string response)
    {
        // Handle markdown code blocks
        response = response.Trim();
        
        if (response.StartsWith("```json"))
            response = response[7..];
        else if (response.StartsWith("```"))
            response = response[3..];
            
        if (response.EndsWith("```"))
            response = response[..^3];
            
        return response.Trim();
    }

    private static string SerializeObject(object? obj)
    {
        if (obj == null) return "null";
        try
        {
            // Limit response size to avoid huge payloads
            var serialized = JsonSerializer.Serialize(obj, JsonOptions);
            if (serialized.Length > 4000)
                return serialized[..4000] + "... (truncated)";
            return serialized;
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
