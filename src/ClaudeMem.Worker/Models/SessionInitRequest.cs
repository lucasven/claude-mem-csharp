namespace ClaudeMem.Worker.Models;

public record SessionInitRequest(
    string ContentSessionId,
    string Project,
    string? Prompt
);

public record ObservationRequest(
    string ContentSessionId,
    string ToolName,
    object? ToolInput,
    object? ToolResponse,
    string Cwd
);

public record SummarizeRequest(
    string ContentSessionId,
    string? LastAssistantMessage
);
