using ClaudeMem.Core.Models;

namespace ClaudeMem.Worker.Services;

public interface IClaudeService
{
    Task<Observation?> ExtractObservationAsync(
        string sessionId,
        string project,
        string toolName,
        object? toolInput,
        object? toolResponse,
        CancellationToken cancellationToken = default);

    Task<SummaryExtraction?> GenerateSummaryAsync(
        string sessionId,
        IEnumerable<Observation> observations,
        string? lastAssistantMessage,
        CancellationToken cancellationToken = default);
}

public record SummaryExtraction(
    string? Request,
    string? Investigated,
    string? Learned,
    string? Completed,
    string? NextSteps,
    List<string>? FilesRead,
    List<string>? FilesEdited,
    string? Notes
);
