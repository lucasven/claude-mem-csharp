using System.CommandLine;
using System.Diagnostics;
using System.Net.Http;

namespace ClaudeMem.Hooks.Commands;

public static class WorkerCommand
{
    private static readonly string PidFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude-mem-csharp",
        "worker.pid"
    );

    public static Command Create()
    {
        var command = new Command("worker", "Manage the ClaudeMem worker service");

        command.AddCommand(CreateStartCommand());
        command.AddCommand(CreateStopCommand());
        command.AddCommand(CreateStatusCommand());
        command.AddCommand(CreateRestartCommand());

        return command;
    }

    private static Command CreateStartCommand()
    {
        var portOption = new Option<int>("--port", () => 37777, "Port to run the worker on");
        var foregroundOption = new Option<bool>("--foreground", "Run in foreground (don't daemonize)");

        var command = new Command("start", "Start the worker service")
        {
            portOption,
            foregroundOption
        };

        command.SetHandler(async (port, foreground) =>
        {
            await StartWorkerAsync(port, foreground);
        }, portOption, foregroundOption);

        return command;
    }

    private static Command CreateStopCommand()
    {
        var command = new Command("stop", "Stop the worker service");

        command.SetHandler(() =>
        {
            StopWorker();
        });

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var command = new Command("status", "Check the worker service status");

        command.SetHandler(async () =>
        {
            await CheckStatusAsync();
        });

        return command;
    }

    private static Command CreateRestartCommand()
    {
        var portOption = new Option<int>("--port", () => 37777, "Port to run the worker on");

        var command = new Command("restart", "Restart the worker service")
        {
            portOption
        };

        command.SetHandler(async (port) =>
        {
            StopWorker();
            await Task.Delay(1000); // Give it time to stop
            await StartWorkerAsync(port, foreground: false);
        }, portOption);

        return command;
    }

    private static async Task StartWorkerAsync(int port, bool foreground)
    {
        // Check if already running
        if (await IsWorkerRunningAsync(port))
        {
            Console.WriteLine($"Worker is already running on port {port}");
            return;
        }

        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-mem-csharp"
        );

        var workerExe = GetWorkerExecutable(installDir);
        if (workerExe == null)
        {
            Console.Error.WriteLine("Worker executable not found. Run 'claude-mem-csharp install' first.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Starting worker on port {port}...");

        var psi = new ProcessStartInfo
        {
            FileName = workerExe,
            UseShellExecute = false,
            CreateNoWindow = !foreground,
            RedirectStandardOutput = !foreground,
            RedirectStandardError = !foreground,
            Environment =
            {
                ["CLAUDE_MEM_WORKER_PORT"] = port.ToString()
            }
        };

        // If it's a dotnet command, we need to handle it differently
        if (workerExe.StartsWith("dotnet "))
        {
            psi.FileName = "dotnet";
            psi.Arguments = workerExe.Substring(7); // Remove "dotnet " prefix
        }

        var process = Process.Start(psi);
        if (process == null)
        {
            Console.Error.WriteLine("Failed to start worker process");
            Environment.ExitCode = 1;
            return;
        }

        if (foreground)
        {
            // Run in foreground - wait for process
            await process.WaitForExitAsync();
        }
        else
        {
            // Save PID for later
            var pidDir = Path.GetDirectoryName(PidFilePath)!;
            if (!Directory.Exists(pidDir))
            {
                Directory.CreateDirectory(pidDir);
            }
            await File.WriteAllTextAsync(PidFilePath, process.Id.ToString());

            // Wait a bit and verify it started
            await Task.Delay(2000);
            if (await IsWorkerRunningAsync(port))
            {
                Console.WriteLine($"Worker started successfully (PID: {process.Id})");
            }
            else
            {
                Console.Error.WriteLine("Worker failed to start. Check logs for details.");
                Environment.ExitCode = 1;
            }
        }
    }

    private static void StopWorker()
    {
        if (!File.Exists(PidFilePath))
        {
            Console.WriteLine("No worker PID file found. Worker may not be running.");
            return;
        }

        var pidStr = File.ReadAllText(PidFilePath).Trim();
        if (!int.TryParse(pidStr, out var pid))
        {
            Console.Error.WriteLine($"Invalid PID in file: {pidStr}");
            return;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            Console.WriteLine($"Stopping worker (PID: {pid})...");
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            File.Delete(PidFilePath);
            Console.WriteLine("Worker stopped.");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("Worker process not found. Cleaning up PID file.");
            File.Delete(PidFilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to stop worker: {ex.Message}");
        }
    }

    private static async Task CheckStatusAsync()
    {
        var port = Environment.GetEnvironmentVariable("CLAUDE_MEM_WORKER_PORT") ?? "37777";
        var running = await IsWorkerRunningAsync(int.Parse(port));

        if (running)
        {
            Console.WriteLine($"Worker: Running on port {port}");

            // Get processing status
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
                var response = await client.GetStringAsync("/api/processing-status");
                Console.WriteLine($"Processing status: {response}");
            }
            catch { }

            // Show PID if available
            if (File.Exists(PidFilePath))
            {
                var pid = await File.ReadAllTextAsync(PidFilePath);
                Console.WriteLine($"PID: {pid.Trim()}");
            }
        }
        else
        {
            Console.WriteLine("Worker: Not running");
        }
    }

    private static async Task<bool> IsWorkerRunningAsync(int port)
    {
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(2)
            };
            var response = await client.GetAsync("/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetWorkerExecutable(string installDir)
    {
        if (OperatingSystem.IsWindows())
        {
            var exePath = Path.Combine(installDir, "ClaudeMem.Worker.exe");
            if (File.Exists(exePath))
                return exePath;
        }

        var dllPath = Path.Combine(installDir, "ClaudeMem.Worker.dll");
        if (File.Exists(dllPath))
            return $"dotnet {dllPath}";

        return null;
    }
}
