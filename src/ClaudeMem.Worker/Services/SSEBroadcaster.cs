using System.Collections.Concurrent;
using System.Text.Json;

namespace ClaudeMem.Worker.Services;

public class SSEBroadcaster
{
    private readonly ConcurrentDictionary<string, HttpResponse> _clients = new();

    public void AddClient(string clientId, HttpResponse response)
    {
        _clients.TryAdd(clientId, response);
    }

    public void RemoveClient(string clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    public async Task BroadcastAsync(object eventData, CancellationToken ct = default)
    {
        if (_clients.IsEmpty) return;

        var json = JsonSerializer.Serialize(eventData);
        var data = $"data: {json}\n\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(data);

        var deadClients = new List<string>();

        foreach (var (clientId, response) in _clients)
        {
            try
            {
                await response.Body.WriteAsync(bytes, ct);
                await response.Body.FlushAsync(ct);
            }
            catch
            {
                deadClients.Add(clientId);
            }
        }

        foreach (var clientId in deadClients)
        {
            _clients.TryRemove(clientId, out _);
        }
    }

    public int GetClientCount() => _clients.Count;
}
