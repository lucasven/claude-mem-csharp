using System.Net.Http.Json;

namespace ClaudeMem.Hooks.Handlers;

public class ObservationHandler : IHookHandler
{
    private readonly HttpClient _client;

    public ObservationHandler()
    {
        var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777";
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public async Task<HookResult> HandleAsync(HookInput input)
    {
        try
        {
            var request = new
            {
                ContentSessionId = input.SessionId,
                ToolName = input.ToolName,
                ToolInput = input.ToolInput,
                ToolResponse = input.ToolResponse,
                Cwd = input.Cwd
            };

            var response = await _client.PostAsJsonAsync("/api/sessions/observations", request);
            response.EnsureSuccessStatusCode();

            return new HookResult(Continue: true, SuppressOutput: true);
        }
        catch (HttpRequestException)
        {
            // Worker not running, continue without error
            return new HookResult(Continue: true, SuppressOutput: true);
        }
    }
}
