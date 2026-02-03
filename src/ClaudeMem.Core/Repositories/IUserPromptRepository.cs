using ClaudeMem.Core.Models;

namespace ClaudeMem.Core.Repositories;

public interface IUserPromptRepository
{
    List<UserPrompt> GetRecent(int limit, int offset, string? project);
    UserPrompt? GetById(long id);
    long GetCount(string? project = null);
    long Store(UserPrompt prompt);
    int GetNextPromptNumber(string contentSessionId);
}
