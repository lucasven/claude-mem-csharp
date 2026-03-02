using System.Text.Json;
using ClaudeMem.Hooks.Handlers;

// Parse command line: hook <platform> <event>
if (args.Length < 3 || args[0] != "hook")
{
    Console.Error.WriteLine("Usage: ClaudeMem.Hooks hook <platform> <event>");
    Console.Error.WriteLine("Events: context, session-init, observation, summarize");
    return 1;
}

var platform = args[1];
var eventType = args[2];

var input = await ReadStdinAsync();
var handler = GetHandler(eventType);
var result = await handler.HandleAsync(input);

if (!string.IsNullOrEmpty(result.Output))
{
    Console.WriteLine(result.Output);
}

return result.ExitCode;

static async Task<HookInput> ReadStdinAsync()
{
    // Check if stdin has data (not interactive)
    if (Console.IsInputRedirected)
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        var json = await reader.ReadToEndAsync();

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new HookInput(
                    SessionId: root.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? "" : "",
                    Cwd: root.TryGetProperty("cwd", out var cwd) ? cwd.GetString() ?? "" : "",
                    Platform: root.TryGetProperty("platform", out var p) ? p.GetString() : null,
                    Prompt: root.TryGetProperty("prompt", out var pr) ? pr.GetString() : null,
                    ToolName: root.TryGetProperty("toolName", out var tn) ? tn.GetString() : null,
                    ToolInput: root.TryGetProperty("toolInput", out var ti) ? ti.Clone() : null,
                    ToolResponse: root.TryGetProperty("toolResponse", out var tr) ? tr.Clone() : null
                );
            }
            catch (JsonException)
            {
                // Fall through to default
            }
        }
    }

    return new HookInput("", "", null, null, null, null, null);
}

static IHookHandler GetHandler(string eventType) => eventType switch
{
    "context" => new ContextHandler(),
    "session-init" => new SessionInitHandler(),
    "observation" => new ObservationHandler(),
    "summarize" => new SummarizeHandler(),
    _ => throw new ArgumentException($"Unknown event type: {eventType}")
};
