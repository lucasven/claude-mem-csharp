using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Core.Tests.Repositories;

public class SessionRepositoryTests : IDisposable
{
    private readonly ClaudeMemDatabase _db;
    private readonly SessionRepository _repo;

    public SessionRepositoryTests()
    {
        _db = new ClaudeMemDatabase(":memory:");
        _repo = new SessionRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Create_ShouldInsertSession()
    {
        var session = new Session
        {
            ContentSessionId = "content-123",
            Project = "test-project",
            StartedAt = DateTime.UtcNow
        };

        var id = _repo.Create(session);

        Assert.True(id > 0);
    }

    [Fact]
    public void GetByContentSessionId_ShouldReturnSession()
    {
        var session = new Session
        {
            ContentSessionId = "content-456",
            Project = "my-project",
            UserPrompt = "Hello",
            StartedAt = DateTime.UtcNow
        };
        _repo.Create(session);

        var result = _repo.GetByContentSessionId("content-456");

        Assert.NotNull(result);
        Assert.Equal("my-project", result.Project);
        Assert.Equal("Hello", result.UserPrompt);
    }

    [Fact]
    public void UpdateMemorySessionId_ShouldUpdateField()
    {
        var session = new Session
        {
            ContentSessionId = "content-789",
            Project = "test",
            StartedAt = DateTime.UtcNow
        };
        _repo.Create(session);

        _repo.UpdateMemorySessionId("content-789", "memory-xyz");
        var result = _repo.GetByContentSessionId("content-789");

        Assert.Equal("memory-xyz", result?.MemorySessionId);
    }
}
