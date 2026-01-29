using System.CommandLine;
using System.Net.Http;
using System.Text.Json;

namespace ClaudeMem.Hooks.Commands;

public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show ClaudeMem installation status");

        command.SetHandler(async () =>
        {
            await ShowStatusAsync();
        });

        return command;
    }

    private static async Task ShowStatusAsync()
    {
        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-mem-csharp"
        );

        Console.WriteLine("ClaudeMem Status");
        Console.WriteLine("================");
        Console.WriteLine();

        // Check installation
        Console.Write("Installation: ");
        if (Directory.Exists(installDir))
        {
            var hasHooks = File.Exists(Path.Combine(installDir, "claude-mem-csharp.dll"));
            var hasMcp = File.Exists(Path.Combine(installDir, "ClaudeMem.Mcp.dll"));
            var hasWorker = File.Exists(Path.Combine(installDir, "ClaudeMem.Worker.dll"));

            if (hasHooks && hasMcp && hasWorker)
            {
                Console.WriteLine($"Installed at {installDir}");
            }
            else
            {
                Console.WriteLine("Partial installation");
                Console.WriteLine($"  Hooks: {(hasHooks ? "Yes" : "No")}");
                Console.WriteLine($"  MCP: {(hasMcp ? "Yes" : "No")}");
                Console.WriteLine($"  Worker: {(hasWorker ? "Yes" : "No")}");
            }
        }
        else
        {
            Console.WriteLine("Not installed");
            Console.WriteLine();
            Console.WriteLine("Run 'claude-mem-csharp install' to install.");
            return;
        }

        Console.WriteLine();

        // Check worker
        var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777";
        Console.Write($"Worker (port {port}): ");

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(2)
            };

            var healthResponse = await client.GetAsync("/api/health");
            if (healthResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Running");

                // Get processing status
                var statusResponse = await client.GetStringAsync("/api/processing-status");
                var status = JsonDocument.Parse(statusResponse);
                var queueDepth = status.RootElement.GetProperty("queueDepth").GetInt32();
                var isProcessing = status.RootElement.GetProperty("isProcessing").GetBoolean();

                Console.WriteLine($"  Queue depth: {queueDepth}");
                Console.WriteLine($"  Processing: {(isProcessing ? "Yes" : "No")}");
            }
            else
            {
                Console.WriteLine("Not responding");
            }
        }
        catch
        {
            Console.WriteLine("Not running");
            Console.WriteLine();
            Console.WriteLine("Run 'claude-mem-csharp worker start' to start the worker.");
        }

        Console.WriteLine();

        // Check Claude Code configuration
        var globalSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "settings.json"
        );

        Console.Write("Claude Code settings: ");
        if (File.Exists(globalSettingsPath))
        {
            try
            {
                var settingsJson = await File.ReadAllTextAsync(globalSettingsPath);
                var settings = JsonDocument.Parse(settingsJson);

                var hasHooks = settings.RootElement.TryGetProperty("hooks", out _);
                var hasMcp = settings.RootElement.TryGetProperty("mcpServers", out var mcpServers) &&
                            mcpServers.TryGetProperty("claude-mem-csharp", out _);

                if (hasHooks && hasMcp)
                {
                    Console.WriteLine("Configured");
                }
                else
                {
                    Console.WriteLine("Partially configured");
                    Console.WriteLine($"  Hooks: {(hasHooks ? "Yes" : "No")}");
                    Console.WriteLine($"  MCP Server: {(hasMcp ? "Yes" : "No")}");
                }
            }
            catch
            {
                Console.WriteLine("Error reading settings");
            }
        }
        else
        {
            Console.WriteLine("Not configured");
            Console.WriteLine();
            Console.WriteLine("Run 'claude-mem-csharp install --global' to configure.");
        }

        Console.WriteLine();

        // Show database stats
        Console.WriteLine("Database:");
        try
        {
            var dbPath = Path.Combine(installDir, "claude-mem-csharp.db");
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                Console.WriteLine($"  Path: {dbPath}");
                Console.WriteLine($"  Size: {FormatBytes(fileInfo.Length)}");
            }
            else
            {
                Console.WriteLine("  Not created yet");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
