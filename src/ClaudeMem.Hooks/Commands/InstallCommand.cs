using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeMem.Hooks.Commands;

public static class InstallCommand
{
    public static Command Create()
    {
        var globalOption = new Option<bool>("--global", "Install globally for all projects");
        var forceOption = new Option<bool>("--force", "Overwrite existing configuration");

        var command = new Command("install", "Install ClaudeMem hooks and MCP server")
        {
            globalOption,
            forceOption
        };

        command.SetHandler(async (global, force) =>
        {
            await InstallAsync(global, force);
        }, globalOption, forceOption);

        return command;
    }

    private static async Task InstallAsync(bool global, bool force)
    {
        Console.WriteLine("Installing ClaudeMem...");

        // Find the installation directory
        var installDir = GetInstallDirectory();
        if (!Directory.Exists(installDir))
        {
            Console.WriteLine($"Creating installation directory: {installDir}");
            Directory.CreateDirectory(installDir);
        }

        // Publish the projects
        await PublishProjectsAsync(installDir);

        // Configure Claude Code settings
        var settingsPath = GetSettingsPath(global);
        await ConfigureClaudeCodeAsync(settingsPath, installDir, force);

        Console.WriteLine();
        Console.WriteLine("ClaudeMem installed successfully!");
        Console.WriteLine();
        Console.WriteLine("To start the worker service, run:");
        Console.WriteLine("  claude-mem-csharp worker start");
        Console.WriteLine();
        Console.WriteLine("To verify the installation:");
        Console.WriteLine("  claude-mem-csharp status");
    }

    private static string GetInstallDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude-mem-csharp");
    }

    private static string GetSettingsPath(bool global)
    {
        if (global)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".claude", "settings.json");
        }
        return Path.Combine(Directory.GetCurrentDirectory(), ".claude", "settings.json");
    }

    private static async Task PublishProjectsAsync(string installDir)
    {
        Console.WriteLine("Publishing projects...");

        // Get the solution directory (go up from the executing assembly)
        var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var binDir = Path.GetDirectoryName(assemblyPath)!;

        // Copy all DLLs and dependencies
        var files = Directory.GetFiles(binDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".dll") || f.EndsWith(".exe") || f.EndsWith(".json") || f.EndsWith(".runtimeconfig.json"));

        foreach (var file in files)
        {
            var destFile = Path.Combine(installDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        // Also need to copy the MCP and Worker executables
        // For now, we'll assume they're built alongside
        var parentBinDir = Path.GetDirectoryName(binDir);
        if (parentBinDir != null)
        {
            var mcpDir = Path.Combine(parentBinDir, "ClaudeMem.Mcp", "Debug", "net9.0");
            var workerDir = Path.Combine(parentBinDir, "ClaudeMem.Worker", "Debug", "net9.0");

            if (Directory.Exists(mcpDir))
            {
                CopyDirectory(mcpDir, installDir);
            }

            if (Directory.Exists(workerDir))
            {
                CopyDirectory(workerDir, installDir);
            }
        }

        Console.WriteLine($"Published to: {installDir}");
        await Task.CompletedTask;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".dll") || f.EndsWith(".exe") || f.EndsWith(".json") || f.EndsWith(".runtimeconfig.json")))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destFile) || new FileInfo(file).LastWriteTimeUtc > new FileInfo(destFile).LastWriteTimeUtc)
            {
                File.Copy(file, destFile, overwrite: true);
            }
        }
    }

    private static async Task ConfigureClaudeCodeAsync(string settingsPath, string installDir, bool force)
    {
        Console.WriteLine($"Configuring Claude Code settings: {settingsPath}");

        var settingsDir = Path.GetDirectoryName(settingsPath)!;
        if (!Directory.Exists(settingsDir))
        {
            Directory.CreateDirectory(settingsDir);
        }

        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            settings = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        }
        else
        {
            settings = new JsonObject();
        }

        // Configure hooks
        ConfigureHooks(settings, installDir, force);

        // Configure MCP server
        ConfigureMcpServer(settings, installDir, force);

        // Write settings
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(settingsPath, settings.ToJsonString(options));

        Console.WriteLine("Claude Code settings configured.");
    }

    private static void ConfigureHooks(JsonObject settings, string installDir, bool force)
    {
        if (!settings.ContainsKey("hooks"))
        {
            settings["hooks"] = new JsonObject();
        }

        var hooks = settings["hooks"]!.AsObject();
        var hookExe = GetExecutablePath(installDir, "claude-mem-csharp");

        // PreToolUse hook for context injection
        if (!hooks.ContainsKey("PreToolUse") || force)
        {
            hooks["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "command",
                            ["command"] = $"{hookExe} hook claude-code context"
                        }
                    }
                }
            };
        }

        // PostToolUse hook for observations
        if (!hooks.ContainsKey("PostToolUse") || force)
        {
            hooks["PostToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "command",
                            ["command"] = $"{hookExe} hook claude-code observation"
                        }
                    }
                }
            };
        }

        // Stop hook for summarization
        if (!hooks.ContainsKey("Stop") || force)
        {
            hooks["Stop"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "command",
                            ["command"] = $"{hookExe} hook claude-code summarize"
                        }
                    }
                }
            };
        }
    }

    private static void ConfigureMcpServer(JsonObject settings, string installDir, bool force)
    {
        if (!settings.ContainsKey("mcpServers"))
        {
            settings["mcpServers"] = new JsonObject();
        }

        var mcpServers = settings["mcpServers"]!.AsObject();

        if (!mcpServers.ContainsKey("claude-mem-csharp") || force)
        {
            var mcpExe = GetExecutablePath(installDir, "ClaudeMem.Mcp");

            mcpServers["claude-mem-csharp"] = new JsonObject
            {
                ["command"] = mcpExe,
                ["args"] = new JsonArray()
            };
        }
    }

    private static string GetExecutablePath(string installDir, string assemblyName)
    {
        // On Windows, look for .exe; on Unix, look for the bare executable or use dotnet
        if (OperatingSystem.IsWindows())
        {
            var exePath = Path.Combine(installDir, $"{assemblyName}.exe");
            if (File.Exists(exePath))
                return exePath;
        }

        // Fall back to dotnet command
        var dllPath = Path.Combine(installDir, $"{assemblyName}.dll");
        return $"dotnet {dllPath}";
    }
}
