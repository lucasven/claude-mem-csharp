using System.Text.Json;
using ClaudeMem.Core.Data;
using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;
using ClaudeMem.Mcp.Protocol;

namespace ClaudeMem.Mcp.Tools;

public class MemoryTools
{
    private readonly IObservationRepository _observationRepo;
    private readonly ISummaryRepository _summaryRepo;
    private readonly ISessionRepository _sessionRepo;

    public MemoryTools(ClaudeMemDatabase db)
    {
        _observationRepo = new ObservationRepository(db);
        _summaryRepo = new SummaryRepository(db);
        _sessionRepo = new SessionRepository(db);
    }

    public static List<McpTool> GetToolDefinitions() =>
    [
        new McpTool(
            Name: "memory_search",
            Description: "Search through stored observations and summaries. Use to find relevant context from past sessions.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search query to find relevant memories" },
                    project = new { type = "string", description = "Optional: filter by project name" },
                    limit = new { type = "integer", description = "Maximum results to return (default: 10)" }
                },
                required = new[] { "query" }
            }
        ),
        new McpTool(
            Name: "memory_get_context",
            Description: "Get relevant context for the current project. Returns recent observations and summaries.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    project = new { type = "string", description = "Project name to get context for" },
                    limit = new { type = "integer", description = "Maximum observations to return (default: 5)" }
                },
                required = new[] { "project" }
            }
        ),
        new McpTool(
            Name: "memory_get_observations",
            Description: "Get recent observations, optionally filtered by project.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    project = new { type = "string", description = "Optional: filter by project name" },
                    limit = new { type = "integer", description = "Maximum results (default: 20)" },
                    offset = new { type = "integer", description = "Pagination offset (default: 0)" }
                }
            }
        ),
        new McpTool(
            Name: "memory_get_summaries",
            Description: "Get session summaries, optionally filtered by project.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    project = new { type = "string", description = "Optional: filter by project name" },
                    limit = new { type = "integer", description = "Maximum results (default: 10)" }
                }
            }
        )
    ];

    public McpCallToolResult CallTool(string name, Dictionary<string, object?>? args)
    {
        return name switch
        {
            "memory_search" => SearchMemory(args),
            "memory_get_context" => GetContext(args),
            "memory_get_observations" => GetObservations(args),
            "memory_get_summaries" => GetSummaries(args),
            _ => new McpCallToolResult(
                Content: [new McpToolContent("text", $"Unknown tool: {name}")],
                IsError: true
            )
        };
    }

    private McpCallToolResult SearchMemory(Dictionary<string, object?>? args)
    {
        var query = GetStringArg(args, "query") ?? "";
        var project = GetStringArg(args, "project");
        var limit = GetIntArg(args, "limit") ?? 10;

        var observations = _observationRepo.GetRecent(limit: limit * 2, project: project);

        // Simple search - filter by query in title, narrative, or facts
        var queryLower = query.ToLowerInvariant();
        var matched = observations
            .Where(o =>
                (o.Title?.Contains(queryLower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Narrative?.Contains(queryLower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                o.Facts.Any(f => f.Contains(queryLower, StringComparison.OrdinalIgnoreCase)) ||
                o.Concepts.Any(c => c.Contains(queryLower, StringComparison.OrdinalIgnoreCase)))
            .Take(limit)
            .ToList();

        var result = matched.Select(o => new
        {
            id = o.Id,
            type = o.Type.ToString(),
            title = o.Title,
            narrative = o.Narrative,
            facts = o.Facts,
            project = o.Project,
            createdAt = o.CreatedAt
        });

        return new McpCallToolResult(
            Content: [new McpToolContent("text", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }))]
        );
    }

    private McpCallToolResult GetContext(Dictionary<string, object?>? args)
    {
        var project = GetStringArg(args, "project") ?? "";
        var limit = GetIntArg(args, "limit") ?? 5;

        var observations = _observationRepo.GetRecent(limit: limit, project: project);
        var summaries = _summaryRepo.GetRecent(limit: 2, project: project);

        var context = new
        {
            project,
            recentObservations = observations.Select(o => new
            {
                type = o.Type.ToString(),
                title = o.Title,
                narrative = o.Narrative,
                facts = o.Facts
            }),
            recentSummaries = summaries.Select(s => new
            {
                request = s.Request,
                completed = s.Completed,
                learned = s.Learned,
                nextSteps = s.NextSteps
            })
        };

        return new McpCallToolResult(
            Content: [new McpToolContent("text", JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true }))]
        );
    }

    private McpCallToolResult GetObservations(Dictionary<string, object?>? args)
    {
        var project = GetStringArg(args, "project");
        var limit = GetIntArg(args, "limit") ?? 20;
        var offset = GetIntArg(args, "offset") ?? 0;

        var observations = _observationRepo.GetRecent(limit: limit, offset: offset, project: project);
        var total = _observationRepo.GetCount(project);

        var result = new
        {
            total,
            observations = observations.Select(o => new
            {
                id = o.Id,
                type = o.Type.ToString(),
                title = o.Title,
                subtitle = o.Subtitle,
                narrative = o.Narrative,
                facts = o.Facts,
                concepts = o.Concepts,
                filesRead = o.FilesRead,
                filesModified = o.FilesModified,
                project = o.Project,
                createdAt = o.CreatedAt
            })
        };

        return new McpCallToolResult(
            Content: [new McpToolContent("text", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }))]
        );
    }

    private McpCallToolResult GetSummaries(Dictionary<string, object?>? args)
    {
        var project = GetStringArg(args, "project");
        var limit = GetIntArg(args, "limit") ?? 10;

        var summaries = _summaryRepo.GetRecent(limit: limit, project: project);

        var result = summaries.Select(s => new
        {
            id = s.Id,
            project = s.Project,
            request = s.Request,
            investigated = s.Investigated,
            learned = s.Learned,
            completed = s.Completed,
            nextSteps = s.NextSteps,
            filesRead = s.FilesRead,
            filesEdited = s.FilesEdited,
            notes = s.Notes,
            createdAt = s.CreatedAt
        });

        return new McpCallToolResult(
            Content: [new McpToolContent("text", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }))]
        );
    }

    private static string? GetStringArg(Dictionary<string, object?>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value)) return null;
        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => value?.ToString()
        };
    }

    private static int? GetIntArg(Dictionary<string, object?>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value)) return null;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            string s when int.TryParse(s, out var i) => i,
            _ => null
        };
    }
}
