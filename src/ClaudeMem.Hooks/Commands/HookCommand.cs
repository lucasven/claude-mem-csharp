using System.CommandLine;
using System.Text.Json;
using ClaudeMem.Hooks.Handlers;

namespace ClaudeMem.Hooks.Commands;

public static class HookCommand
{
    public static Command Create()
    {
        var platformArg = new Argument<string>("platform", "The platform (e.g., claude-code)");
        var eventArg = new Argument<string>("event", "The hook event type");

        var command = new Command("hook", "Handle a Claude Code hook event")
        {
            platformArg,
            eventArg
        };

        command.SetHandler(async (platform, eventType) =>
        {
            var input = await ReadStdinAsync();
            var handler = GetHandler(eventType);
            var result = await handler.HandleAsync(input);

            if (!string.IsNullOrEmpty(result.Output))
            {
                Console.WriteLine(result.Output);
            }

            Environment.ExitCode = result.ExitCode;
        }, platformArg, eventArg);

        return command;
    }

    private static async Task<HookInput> ReadStdinAsync()
    {
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
                catch (JsonException) { }
            }
        }

        return new HookInput("", "", null, null, null, null, null);
    }

    private static IHookHandler GetHandler(string eventType) => eventType switch
    {
        "context" => new ContextHandler(),
        "session-init" => new SessionInitHandler(),
        "observation" => new ObservationHandler(),
        "summarize" => new SummarizeHandler(),
        _ => throw new ArgumentException($"Unknown event type: {eventType}")
    };
}
