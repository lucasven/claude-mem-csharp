namespace ClaudeMem.Hooks.Handlers;

public class SummarizeHandler : IHookHandler
{
    public Task<HookResult> HandleAsync(HookInput input)
    {
        // TODO: Call worker API to generate summary
        return Task.FromResult(new HookResult(Continue: true, SuppressOutput: true));
    }
}
