namespace ClaudeMem.Core.Models;

public class Summary
{
    public long Id { get; set; }
    public required string MemorySessionId { get; set; }
    public required string Project { get; set; }
    public string? Request { get; set; }
    public string? Investigated { get; set; }
    public string? Learned { get; set; }
    public string? Completed { get; set; }
    public string? NextSteps { get; set; }
    public string? FilesRead { get; set; }
    public string? FilesEdited { get; set; }
    public string? Notes { get; set; }
    public int? PromptNumber { get; set; }
    public int DiscoveryTokens { get; set; }
    public required DateTime CreatedAt { get; set; }
    public long CreatedAtEpoch => new DateTimeOffset(CreatedAt).ToUnixTimeMilliseconds();
}
