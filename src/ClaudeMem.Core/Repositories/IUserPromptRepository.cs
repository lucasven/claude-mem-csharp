using ClaudeMem.Core.Models;

namespace ClaudeMem.Core.Repositories;

public interface IUserPromptRepository
{
    long Store(UserPrompt prompt);
    List<UserPrompt> GetRecent(int limit, int offset, string? project);
    UserPrompt? GetById(long id);
    long GetCount(string? project = null);
}
