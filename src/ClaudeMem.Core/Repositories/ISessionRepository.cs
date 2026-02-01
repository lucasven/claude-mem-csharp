using ClaudeMem.Core.Models;

namespace ClaudeMem.Core.Repositories;

public interface ISessionRepository
{
    long Create(Session session);
    Session? GetById(long id);
    Session? GetByContentSessionId(string contentSessionId);
    Session? GetByMemorySessionId(string memorySessionId);
    void UpdateMemorySessionId(string contentSessionId, string memorySessionId);
    void Complete(long id);
    void MarkComplete(long id, string reason);
    int GetCount();
}
