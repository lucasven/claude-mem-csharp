using ClaudeMem.Core.Models;

namespace ClaudeMem.Core.Repositories;

public interface ISummaryRepository
{
    long Store(Summary summary);
    Summary? GetById(long id);
    Summary? GetByMemorySessionId(string memorySessionId);
    List<Summary> GetRecent(int limit = 20, int offset = 0, string? project = null);
}
