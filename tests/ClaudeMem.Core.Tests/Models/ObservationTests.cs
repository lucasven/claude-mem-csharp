using ClaudeMem.Core.Models;

namespace ClaudeMem.Core.Tests.Models;

public class ObservationTests
{
    [Fact]
    public void Observation_ShouldSerializeFactsAsJson()
    {
        var observation = new Observation
        {
            Id = 1,
            MemorySessionId = "test-session",
            Project = "test-project",
            Type = ObservationType.Feature,
            Title = "Test Feature",
            Text = "Test text",
            Facts = ["Fact 1", "Fact 2"],
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal(2, observation.Facts.Count);
        Assert.Equal("Fact 1", observation.Facts[0]);
    }

    [Fact]
    public void Observation_ShouldHaveRequiredFields()
    {
        var observation = new Observation
        {
            MemorySessionId = "session-123",
            Project = "my-project",
            Type = ObservationType.Bugfix,
            Text = "Fixed the bug",
            CreatedAt = DateTime.UtcNow
        };

        Assert.NotNull(observation.MemorySessionId);
        Assert.NotNull(observation.Project);
        Assert.NotNull(observation.Text);
    }
}
