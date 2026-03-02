namespace ClaudeMem.Hooks.Handlers;

public class ContextHandler : IHookHandler
{
    private readonly HttpClient _client;

    public ContextHandler()
    {
        var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777";
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public async Task<HookResult> HandleAsync(HookInput input)
    {
        try
        {
            var project = Path.GetFileName(input.Cwd);
            var response = await _client.GetStringAsync($"/api/context/inject?projects={project}&colors=true");

            return new HookResult(
                Continue: true,
                SuppressOutput: false,
                Output: response
            );
        }
        catch (HttpRequestException)
        {
            // Worker not running, return empty context
            return new HookResult(Continue: true, Output: "");
        }
    }
}
