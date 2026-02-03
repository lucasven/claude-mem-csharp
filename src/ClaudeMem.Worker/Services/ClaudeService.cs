using System.Text.Json;
using ClaudeMem.Core.Models;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

namespace ClaudeMem.Worker.Services;

public class ClaudeService : IClaudeService
{
    private static string? _cliPath;
    
    static ClaudeService()
    {
        // Log auth context on startup and find CLI
        _cliPath = LogAuthContext();
    }

    private static string? LogAuthContext()
    {
        Console.WriteLine("\n[ClaudeService] === Auth Context Debug ===");
        
        // Check ANTHROPIC_API_KEY
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Console.WriteLine($"[ClaudeService] ANTHROPIC_API_KEY: {(string.IsNullOrEmpty(apiKey) ? "NOT SET" : $"SET (length: {apiKey.Length})")}");
        
        // Check Claude auth paths (Windows)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        Console.WriteLine($"[ClaudeService] AppData: {appData}");
        Console.WriteLine($"[ClaudeService] LocalAppData: {localAppData}");
        Console.WriteLine($"[ClaudeService] UserProfile: {userProfile}");
        
        // Check common Claude auth locations
        var possibleAuthPaths = new[]
        {
            Path.Combine(appData, "Claude"),
            Path.Combine(localAppData, "Claude"),
            Path.Combine(userProfile, ".claude"),
            Path.Combine(userProfile, ".config", "claude"),
        };
        
        foreach (var path in possibleAuthPaths)
        {
            var exists = Directory.Exists(path);
            Console.WriteLine($"[ClaudeService] Auth path '{path}': {(exists ? "EXISTS" : "NOT FOUND")}");
            
            if (exists)
            {
                try
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    foreach (var file in files.Take(10))
                    {
                        var relativePath = Path.GetRelativePath(path, file);
                        Console.WriteLine($"[ClaudeService]   - {relativePath}");
                    }
                    if (files.Length > 10)
                        Console.WriteLine($"[ClaudeService]   ... and {files.Length - 10} more files");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ClaudeService]   Error listing files: {ex.Message}");
                }
            }
        }
        
        // Check current user context
        Console.WriteLine($"[ClaudeService] Current User: {Environment.UserName}");
        Console.WriteLine($"[ClaudeService] Current Directory: {Environment.CurrentDirectory}");
        
        // Check if claude CLI is accessible
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        Console.WriteLine($"[ClaudeService] PATH contains 'claude': {pathEnv.ToLower().Contains("claude")}");
        
        // Search for Claude CLI in common locations
        Console.WriteLine("[ClaudeService] Searching for Claude CLI...");
        var cliLocations = new[]
        {
            // Windows locations
            Path.Combine(appData, "npm", "claude.cmd"),
            Path.Combine(appData, "npm", "claude.exe"),
            Path.Combine(localAppData, "Claude", "claude.exe"),
            Path.Combine(userProfile, ".local", "bin", "claude.exe"),
            Path.Combine(userProfile, ".npm-global", "bin", "claude.cmd"),
            Path.Combine(userProfile, "node_modules", ".bin", "claude.cmd"),
            Path.Combine(userProfile, ".claude", "local", "claude.exe"),
            // Also check if it's in a node_modules somewhere
            Path.Combine(userProfile, "AppData", "Roaming", "npm", "claude.cmd"),
        };
        
        string? foundCliPath = null;
        foreach (var location in cliLocations)
        {
            var exists = File.Exists(location);
            if (exists)
            {
                Console.WriteLine($"[ClaudeService] CLI FOUND: {location}");
                foundCliPath = location;
            }
            else
            {
                Console.WriteLine($"[ClaudeService] CLI not at: {location}");
            }
        }
        
        // Try to find via 'where' command on Windows
        if (foundCliPath == null && OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "claude",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Console.WriteLine($"[ClaudeService] 'where claude' found: {output.Trim()}");
                        foundCliPath = output.Trim().Split('\n')[0].Trim();
                    }
                    else
                    {
                        Console.WriteLine("[ClaudeService] 'where claude' returned empty");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClaudeService] 'where claude' error: {ex.Message}");
            }
        }
        
        if (foundCliPath != null)
        {
            Console.WriteLine($"[ClaudeService] Will use CLI at: {foundCliPath}");
        }
        else
        {
            Console.WriteLine("[ClaudeService] WARNING: Claude CLI not found in any known location!");
        }
        
        Console.WriteLine("[ClaudeService] === End Auth Context Debug ===\n");
        return foundCliPath;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string ObservationSystemPrompt = """
        You are an observation extractor for a coding memory system. Given a tool interaction from a Claude Code session,
        extract meaningful observations that would be useful to remember for future sessions.

        IMPORTANT: Only create an observation if the interaction contains genuinely useful information worth remembering.
        Skip routine operations like simple file reads or directory listings unless they reveal something significant.

        Respond ONLY with valid JSON in this exact format (no markdown, no explanation):
        {
            "shouldCreate": true/false,
            "type": "Decision|Bugfix|Feature|Refactor|Discovery",
            "title": "Brief title (max 60 chars)",
            "subtitle": "One-line summary",
            "narrative": "2-3 sentence explanation of what happened and why it matters",
            "facts": ["Specific fact 1", "Specific fact 2"],
            "concepts": ["concept1", "concept2"],
            "filesRead": ["path/to/file"],
            "filesModified": ["path/to/file"]
        }

        If the interaction is not worth remembering, respond with:
        {"shouldCreate": false}
        """;

    private const string SummarySystemPrompt = """
        You are a session summarizer for a coding memory system. Given a list of observations from a Claude Code session,
        create a structured summary that captures the key work done and decisions made.

        Respond ONLY with valid JSON in this exact format (no markdown, no explanation):
        {
            "request": "What the user originally asked for",
            "investigated": "What was explored or researched",
            "learned": "Key insights or discoveries made",
            "completed": "What was accomplished or implemented",
            "nextSteps": "Potential follow-up work or recommendations",
            "filesRead": ["path/to/file"],
            "filesEdited": ["path/to/file"],
            "notes": "Any additional context or important observations"
        }

        All fields are optional - only include fields that have meaningful content.
        """;

    public async Task<Observation?> ExtractObservationAsync(
        string sessionId,
        string project,
        string toolName,
        object? toolInput,
        object? toolResponse,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
            Tool: {toolName}
            Input: {SerializeObject(toolInput)}
            Response: {SerializeObject(toolResponse)}
            """;

        var optionsBuilder = ClaudeApi.Options()
            .SystemPrompt(ObservationSystemPrompt)
            .Model("claude-3-5-haiku-20241022")  // Fast, cheap model for extraction
            .MaxTurns(1)
            .Tools()                             // No tools needed - pure text generation
            .BypassPermissions()                 // Non-interactive mode
            .OnStderr(line => Console.WriteLine($"[Claude CLI stderr] {line}"));
        
        // Use explicit CLI path if found
        if (_cliPath != null)
        {
            optionsBuilder = optionsBuilder.CliPath(_cliPath);
            Console.WriteLine($"[ClaudeService] Using explicit CLI path: {_cliPath}");
        }
        
        var options = optionsBuilder.Build();

        var response = await GetResponseTextAsync(prompt, options, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
            return null;

        try
        {
            var result = JsonSerializer.Deserialize<ObservationExtraction>(response, JsonOptions);
            if (result == null || !result.ShouldCreate)
                return null;

            return new Observation
            {
                MemorySessionId = sessionId,
                Project = project,
                Type = ParseObservationType(result.Type),
                Title = result.Title,
                Subtitle = result.Subtitle,
                Narrative = result.Narrative,
                Text = prompt,
                Facts = result.Facts ?? [],
                Concepts = result.Concepts ?? [],
                FilesRead = result.FilesRead ?? [],
                FilesModified = result.FilesModified ?? [],
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<SummaryExtraction?> GenerateSummaryAsync(
        string sessionId,
        IEnumerable<Observation> observations,
        string? lastAssistantMessage,
        CancellationToken cancellationToken = default)
    {
        var observationList = observations.ToList();
        if (observationList.Count == 0)
            return null;

        var observationsText = string.Join("\n\n", observationList.Select((o, i) => $"""
            Observation {i + 1}: {o.Title}
            Type: {o.Type}
            {o.Narrative}
            Facts: {string.Join(", ", o.Facts)}
            Files read: {string.Join(", ", o.FilesRead)}
            Files modified: {string.Join(", ", o.FilesModified)}
            """));

        var prompt = $"""
            Session observations:
            {observationsText}

            {(lastAssistantMessage != null ? $"Last assistant message:\n{lastAssistantMessage}" : "")}
            """;

        var optionsBuilder = ClaudeApi.Options()
            .SystemPrompt(SummarySystemPrompt)
            .Model("claude-3-5-haiku-20241022")  // Fast, cheap model for summarization
            .MaxTurns(1)
            .Tools()                             // No tools needed - pure text generation
            .BypassPermissions()                 // Non-interactive mode
            .OnStderr(line => Console.WriteLine($"[Claude CLI stderr] {line}"));
            
        // Use explicit CLI path if found
        if (_cliPath != null)
        {
            optionsBuilder = optionsBuilder.CliPath(_cliPath);
        }
        
        var options = optionsBuilder.Build();

        var response = await GetResponseTextAsync(prompt, options, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SummaryExtraction>(response, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> GetResponseTextAsync(
        string prompt,
        ClaudeAgentOptions options,
        CancellationToken cancellationToken)
    {
        var responseText = new System.Text.StringBuilder();
        
        Console.WriteLine($"[ClaudeService] Starting CLI call...");
        Console.WriteLine($"[ClaudeService] - CliPath: {options.CliPath ?? "(not set, will auto-detect)"}");
        Console.WriteLine($"[ClaudeService] - Model: {options.Model}");
        Console.WriteLine($"[ClaudeService] - MaxTurns: {options.MaxTurns}");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout

            await foreach (var message in ClaudeApi.QueryAsync(prompt, options, cancellationToken: cts.Token))
            {
                Console.WriteLine($"[Claude CLI] Received message type: {message.GetType().Name}");
                if (message is AssistantMessage am)
                {
                    foreach (var block in am.Content)
                    {
                        if (block is TextBlock tb)
                            responseText.Append(tb.Text);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Claude CLI] Request timed out after 30 seconds");
            return "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Claude CLI] Error: {ex.Message}");
            throw;
        }

        return responseText.ToString().Trim();
    }

    private static string SerializeObject(object? obj)
    {
        if (obj == null) return "null";
        try
        {
            return JsonSerializer.Serialize(obj, JsonOptions);
        }
        catch
        {
            return obj.ToString() ?? "null";
        }
    }

    private static ObservationType ParseObservationType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "decision" => ObservationType.Decision,
            "bugfix" => ObservationType.Bugfix,
            "feature" => ObservationType.Feature,
            "refactor" => ObservationType.Refactor,
            _ => ObservationType.Discovery
        };
    }

    private record ObservationExtraction(
        bool ShouldCreate,
        string? Type,
        string? Title,
        string? Subtitle,
        string? Narrative,
        List<string>? Facts,
        List<string>? Concepts,
        List<string>? FilesRead,
        List<string>? FilesModified
    );
}
