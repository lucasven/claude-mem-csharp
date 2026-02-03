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
/// <param name="ContentSessionId">Session identifier (required)</param>
/// <param name="ToolName">Name of the tool that generated this observation</param>
/// <param name="ToolInput">Input provided to the tool</param>
/// <param name="ToolResponse">Response from the tool</param>
/// <param name="Cwd">Current working directory</param>
/// <param name="Title">Optional title for the observation</param>
/// <param name="ObservationType">Type of observation (discovery, modification, action, observation)</param>
public record ObservationRequest(
    string? ContentSessionId,
    string? ToolName,
    object? ToolInput,
    object? ToolResponse,
    string? Cwd,
    string? Title,
    string? ObservationType
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
