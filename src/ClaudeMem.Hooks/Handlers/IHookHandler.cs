namespace ClaudeMem.Hooks.Handlers;

public interface IHookHandler
{
    Task<HookResult> HandleAsync(HookInput input);
}

public record HookInput(
    string SessionId,
    string Cwd,
    string? Platform,
    string? Prompt,
    string? ToolName,
    object? ToolInput,
    object? ToolResponse
);

public record HookResult(
    bool Continue = true,
    bool SuppressOutput = true,
    string? Output = null,
    int ExitCode = 0
);
