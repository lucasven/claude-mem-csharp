using System.Net.Http.Json;

namespace ClaudeMem.Hooks.Handlers;

public class SessionInitHandler : IHookHandler
{
    private readonly HttpClient _client;

    public SessionInitHandler()
    {
        var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777";
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public async Task<HookResult> HandleAsync(HookInput input)
    {
        try
        {
            var project = Path.GetFileName(input.Cwd);
            var request = new
            {
                ContentSessionId = input.SessionId,
                Project = project,
                Prompt = input.Prompt
            };

            var response = await _client.PostAsJsonAsync("/api/sessions/init", request);
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
