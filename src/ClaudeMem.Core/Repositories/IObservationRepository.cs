using ClaudeMem.Core.Models;

namespace ClaudeMem.Core.Repositories;

public interface IObservationRepository
{
    long Store(Observation observation);
    Observation? GetById(long id);
    List<Observation> GetRecent(int limit = 20, int offset = 0, string? project = null);
    List<Observation> GetByIds(IEnumerable<long> ids);
    List<Observation> GetBySessionId(string sessionId);
    int GetCount(string? project = null);
}
