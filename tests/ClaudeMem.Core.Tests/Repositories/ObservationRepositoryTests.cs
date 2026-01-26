using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Core.Tests.Repositories;

public class ObservationRepositoryTests : IDisposable
{
    private readonly ClaudeMemDatabase _db;
    private readonly ObservationRepository _repo;

    public ObservationRepositoryTests()
    {
        _db = new ClaudeMemDatabase(":memory:");
        _repo = new ObservationRepository(_db);
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
    public void Store_ShouldInsertObservation()
    {
        var observation = new Observation
        {
            MemorySessionId = "memory-123",
            Project = "test-project",
            Type = ObservationType.Feature,
            Title = "New Feature",
            Text = "Implemented the feature",
            Facts = ["Fact 1", "Fact 2"],
            CreatedAt = DateTime.UtcNow
        };

        var id = _repo.Store(observation);

        Assert.True(id > 0);
    }

    [Fact]
    public void GetById_ShouldReturnObservation()
    {
        var observation = new Observation
        {
            MemorySessionId = "memory-123",
            Project = "test-project",
            Type = ObservationType.Bugfix,
            Title = "Bug Fix",
            Text = "Fixed the bug",
            CreatedAt = DateTime.UtcNow
        };
        var id = _repo.Store(observation);

        var result = _repo.GetById(id);

        Assert.NotNull(result);
        Assert.Equal("Bug Fix", result.Title);
        Assert.Equal(ObservationType.Bugfix, result.Type);
    }

    [Fact]
    public void GetRecent_ShouldReturnOrderedObservations()
    {
        _repo.Store(new Observation
        {
            MemorySessionId = "memory-123",
            Project = "test-project",
            Type = ObservationType.Feature,
            Text = "First",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        _repo.Store(new Observation
        {
            MemorySessionId = "memory-123",
            Project = "test-project",
            Type = ObservationType.Feature,
            Text = "Second",
            CreatedAt = DateTime.UtcNow
        });

        var results = _repo.GetRecent(limit: 10);

        Assert.Equal(2, results.Count);
        Assert.Equal("Second", results[0].Text);
    }
}
