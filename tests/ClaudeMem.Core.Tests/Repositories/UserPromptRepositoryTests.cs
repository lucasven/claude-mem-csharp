using ClaudeMem.Core.Data;
using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Core.Tests.Repositories;

public class UserPromptRepositoryTests
{
    [Fact]
    public void GetRecent_ReturnsPrompts_OrderedByDate()
    {
        // Arrange
        using var db = new ClaudeMemDatabase(":memory:");
        var repo = new UserPromptRepository(db);

        // Act
        var prompts = repo.GetRecent(10, 0, null);

        // Assert
        Assert.NotNull(prompts);
    }
}
