namespace ClaudeMem.Hooks.Handlers;

public class SessionInitHandler : IHookHandler
{
    public Task<HookResult> HandleAsync(HookInput input)
    {
        // TODO: Call worker API to init session
        return Task.FromResult(new HookResult(Continue: true, SuppressOutput: true));
    }
}
