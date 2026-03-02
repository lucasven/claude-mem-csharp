using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Core.Tests.Repositories;

public class SummaryRepositoryTests : IDisposable
{
    private readonly ClaudeMemDatabase _db;
    private readonly SummaryRepository _repo;

    public SummaryRepositoryTests()
    {
        _db = new ClaudeMemDatabase(":memory:");
        _repo = new SummaryRepository(_db);
        SeedSession();
    }

    private void SeedSession()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sdk_sessions (content_session_id, memory_session_id, project, started_at, started_at_epoch)
            VALUES ('content-123', 'memory-123', 'test-project', '2026-01-26T00:00:00Z', 1737849600000)
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Store_ShouldInsertSummary()
    {
        var summary = new Summary
        {
            MemorySessionId = "memory-123",
            Project = "test-project",
            Request = "User asked to fix bug",
            Completed = "Fixed the authentication issue",
            CreatedAt = DateTime.UtcNow
        };

        var id = _repo.Store(summary);

        Assert.True(id > 0);
    }

    [Fact]
    public void GetByMemorySessionId_ShouldReturnSummary()
    {
        var summary = new Summary
        {
            MemorySessionId = "memory-123",
            Project = "test-project",
            Request = "Build feature",
            Learned = "Learned about the API",
            CreatedAt = DateTime.UtcNow
        };
        _repo.Store(summary);

        var result = _repo.GetByMemorySessionId("memory-123");

        Assert.NotNull(result);
        Assert.Equal("Build feature", result.Request);
    }
}
