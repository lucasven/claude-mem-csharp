namespace ClaudeMem.Worker.Services;

public interface IBackgroundQueue
{
    void QueueObservation(ObservationWorkItem item);
    void QueueSummary(SummaryWorkItem item);
    Task<ObservationWorkItem?> DequeueObservationAsync(CancellationToken cancellationToken);
    Task<SummaryWorkItem?> DequeueSummaryAsync(CancellationToken cancellationToken);
    int ObservationQueueDepth { get; }
    int SummaryQueueDepth { get; }
}

public record ObservationWorkItem(
    string ContentSessionId,
    string ToolName,
    object? ToolInput,
    object? ToolResponse,
    string Cwd
);

public record SummaryWorkItem(
    string ContentSessionId,
    string? LastAssistantMessage
);
