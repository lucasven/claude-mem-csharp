using System.CommandLine;
using ClaudeMem.Hooks.Commands;

var rootCommand = new RootCommand("ClaudeMem - Memory system for Claude Code")
{
    HookCommand.Create(),
    InstallCommand.Create(),
    UninstallCommand.Create(),
    WorkerCommand.Create(),
    StatusCommand.Create()
};

return await rootCommand.InvokeAsync(args);
