namespace ClaudeMem.Core.Models;

public class UserPrompt
{
    public long Id { get; set; }
    public required string ContentSessionId { get; set; }
    public required string Project { get; set; }
    public required int PromptNumber { get; set; }
    public required string PromptText { get; set; }
    public string? MemorySessionId { get; set; }
    public required DateTime CreatedAt { get; set; }
    public long CreatedAtEpoch => new DateTimeOffset(CreatedAt).ToUnixTimeMilliseconds();
}
