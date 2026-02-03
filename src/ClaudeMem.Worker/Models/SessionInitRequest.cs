using System.Collections.Generic;

namespace ClaudeMem.Worker.Models;

/// <summary>
/// Request to initialize a new session or retrieve an existing one.
/// </summary>
/// <param name="ContentSessionId">Unique session identifier (required)</param>
/// <param name="Project">Project path or name (required)</param>
/// <param name="Prompt">Optional initial prompt</param>
public record SessionInitRequest(
    string? ContentSessionId,
    string? Project,
    string? Prompt
);

/// <summary>
/// Request to queue an observation for memory storage.
/// </summary>
public record ObservationRequest(
    string? ContentSessionId,
    string? ToolName,
    object? ToolInput,
    object? ToolResponse,
    string? Cwd,
    string? Title,
    string? ObservationType,
    string? Narrative,
    List<string>? Facts,
    List<string>? Concepts,
    List<string>? FilesRead,
    List<string>? FilesModified,
    int? PromptNumber,
    int? DiscoveryTokens
);

/// <summary>
/// Request to generate a summary for the session.
/// </summary>
/// <param name="ContentSessionId">Session identifier (required)</param>
/// <param name="LastAssistantMessage">Last message from the assistant</param>
public record SummarizeRequest(
    string? ContentSessionId,
    string? LastAssistantMessage
);
