namespace ClaudeMem.Core.Models;

public class Observation
{
    public long Id { get; set; }
    public required string MemorySessionId { get; set; }
    public required string Project { get; set; }
    public required ObservationType Type { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Narrative { get; set; }
    public required string Text { get; set; }
    public List<string> Facts { get; set; } = [];
    public List<string> Concepts { get; set; } = [];
    public List<string> FilesRead { get; set; } = [];
    public List<string> FilesModified { get; set; } = [];
    public int? PromptNumber { get; set; }
    public int DiscoveryTokens { get; set; }
    public required DateTime CreatedAt { get; set; }
    public long CreatedAtEpoch => new DateTimeOffset(CreatedAt).ToUnixTimeMilliseconds();
}
