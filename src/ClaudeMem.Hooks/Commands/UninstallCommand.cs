using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeMem.Hooks.Commands;

public static class UninstallCommand
{
    public static Command Create()
    {
        var keepDataOption = new Option<bool>("--keep-data", "Keep the database and settings");
        var globalOption = new Option<bool>("--global", "Remove global Claude Code configuration");

        var command = new Command("uninstall", "Uninstall ClaudeMem")
        {
            keepDataOption,
            globalOption
        };

        command.SetHandler(async (keepData, global) =>
        {
            await UninstallAsync(keepData, global);
        }, keepDataOption, globalOption);

        return command;
    }

    private static async Task UninstallAsync(bool keepData, bool global)
    {
        Console.WriteLine("Uninstalling ClaudeMem...");

        // Stop the worker if running
        var pidFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-mem-csharp",
            "worker.pid"
        );

        if (File.Exists(pidFilePath))
        {
            Console.WriteLine("Stopping worker...");
            try
            {
                var pidStr = await File.ReadAllTextAsync(pidFilePath);
                if (int.TryParse(pidStr.Trim(), out var pid))
                {
                    var process = System.Diagnostics.Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch { }
        }

        // Remove Claude Code configuration
        if (global)
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                "settings.json"
            );

            if (File.Exists(settingsPath))
            {
                Console.WriteLine("Removing Claude Code configuration...");
                await RemoveClaudeCodeConfigAsync(settingsPath);
            }
        }

        // Remove installation directory
        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-mem-csharp"
        );

        if (Directory.Exists(installDir))
        {
            if (keepData)
            {
                Console.WriteLine("Removing executables (keeping data)...");
                // Only remove executables, keep the database
                foreach (var file in Directory.GetFiles(installDir, "*.dll"))
                {
                    try { File.Delete(file); } catch { }
                }
                foreach (var file in Directory.GetFiles(installDir, "*.exe"))
                {
                    try { File.Delete(file); } catch { }
                }
                foreach (var file in Directory.GetFiles(installDir, "*.json"))
                {
                    if (!file.EndsWith("settings.json")) // Keep user settings
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            else
            {
                Console.WriteLine($"Removing installation directory: {installDir}");
                try
                {
                    Directory.Delete(installDir, recursive: true);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Could not fully remove directory: {ex.Message}");
                }
            }
        }

        Console.WriteLine("ClaudeMem uninstalled.");
    }

    private static async Task RemoveClaudeCodeConfigAsync(string settingsPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonNode.Parse(json)?.AsObject();

            if (settings == null) return;

            // Remove hooks configuration
            if (settings.ContainsKey("hooks"))
            {
                var hooks = settings["hooks"]?.AsObject();
                if (hooks != null)
                {
                    // Remove our specific hook entries
                    RemoveHookEntry(hooks, "PreToolUse");
                    RemoveHookEntry(hooks, "PostToolUse");
                    RemoveHookEntry(hooks, "Stop");

                    // If hooks is empty, remove it entirely
                    if (hooks.Count == 0)
                    {
                        settings.Remove("hooks");
                    }
                }
            }

            // Remove MCP server configuration
            if (settings.ContainsKey("mcpServers"))
            {
                var mcpServers = settings["mcpServers"]?.AsObject();
                if (mcpServers != null)
                {
                    mcpServers.Remove("claude-mem-csharp");

                    if (mcpServers.Count == 0)
                    {
                        settings.Remove("mcpServers");
                    }
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(settingsPath, settings.ToJsonString(options));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not update settings: {ex.Message}");
        }
    }

    private static void RemoveHookEntry(JsonObject hooks, string hookType)
    {
        if (!hooks.ContainsKey(hookType)) return;

        var hookArray = hooks[hookType]?.AsArray();
        if (hookArray == null) return;

        // Remove entries that contain claude-mem-csharp commands
        for (int i = hookArray.Count - 1; i >= 0; i--)
        {
            var entry = hookArray[i]?.AsObject();
            if (entry == null) continue;

            var hooksInner = entry["hooks"]?.AsArray();
            if (hooksInner != null)
            {
                for (int j = hooksInner.Count - 1; j >= 0; j--)
                {
                    var hook = hooksInner[j]?.AsObject();
                    var command = hook?["command"]?.GetValue<string>();
                    if (command != null && command.Contains("ClaudeMem"))
                    {
                        hooksInner.RemoveAt(j);
                    }
                }

                if (hooksInner.Count == 0)
                {
                    hookArray.RemoveAt(i);
                }
            }
        }

        if (hookArray.Count == 0)
        {
            hooks.Remove(hookType);
        }
    }
}
