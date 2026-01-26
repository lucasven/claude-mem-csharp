namespace ClaudeMem.Core.Models;

public class Session
{
    public long Id { get; set; }
    public required string ContentSessionId { get; set; }
    public string? MemorySessionId { get; set; }
    public required string Project { get; set; }
    public string? UserPrompt { get; set; }
    public required DateTime StartedAt { get; set; }
    public long StartedAtEpoch => new DateTimeOffset(StartedAt).ToUnixTimeMilliseconds();
    public DateTime? CompletedAt { get; set; }
    public long? CompletedAtEpoch => CompletedAt.HasValue
        ? new DateTimeOffset(CompletedAt.Value).ToUnixTimeMilliseconds()
        : null;
    public SessionStatus Status { get; set; } = SessionStatus.Active;
}

public enum SessionStatus
{
    Active,
    Completed,
    Failed
}
