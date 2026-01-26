using ClaudeMem.Core.Data;

namespace ClaudeMem.Core.Tests.Data;

public class DatabaseTests : IDisposable
{
    private readonly ClaudeMemDatabase _db;

    public DatabaseTests()
    {
        _db = new ClaudeMemDatabase(":memory:");
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void Database_ShouldCreateTablesOnInit()
    {
        var tables = _db.GetTableNames();

        Assert.Contains("sdk_sessions", tables);
        Assert.Contains("observations", tables);
        Assert.Contains("session_summaries", tables);
        Assert.Contains("user_prompts", tables);
    }

    [Fact]
    public void Database_ShouldConfigurePragmas()
    {
        // In-memory databases don't support WAL mode, they use "memory" journal mode
        // For file-based databases, WAL would be enabled
        var journalMode = _db.GetPragma("journal_mode");
        Assert.True(journalMode.ToLower() == "wal" || journalMode.ToLower() == "memory",
            $"Expected journal_mode to be 'wal' or 'memory', got '{journalMode}'");

        // Verify foreign keys are enabled
        var foreignKeys = _db.GetPragma("foreign_keys");
        Assert.Equal("1", foreignKeys);
    }
}
