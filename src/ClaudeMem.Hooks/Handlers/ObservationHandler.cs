namespace ClaudeMem.Hooks.Handlers;

public class ObservationHandler : IHookHandler
{
    public Task<HookResult> HandleAsync(HookInput input)
    {
        // TODO: Call worker API to store observation
        return Task.FromResult(new HookResult(Continue: true, SuppressOutput: true));
    }
}
