using ClaudeMem.Core.Data;
using ClaudeMem.Worker.Services;
using Microsoft.Data.Sqlite;

namespace ClaudeMem.Worker.Endpoints;

public static class ViewerEndpoints
{
    public static void MapViewerEndpoints(this WebApplication app)
    {
        // Serve viewer HTML at root
        app.MapGet("/", async (HttpContext context) =>
        {
            var viewerPath = Path.Combine(AppContext.BaseDirectory, "ui", "viewer.html");

            if (!File.Exists(viewerPath))
            {
                return Results.NotFound("Viewer UI not found. Please ensure ui/viewer.html is in the output directory.");
            }

            var html = await File.ReadAllTextAsync(viewerPath);
            return Results.Content(html, "text/html");
        });

        // SSE stream endpoint
        app.MapGet("/stream", async (
            HttpContext context,
            SSEBroadcaster broadcaster,
            ClaudeMemDatabase db,
            CancellationToken ct) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            var clientId = Guid.NewGuid().ToString();
            broadcaster.AddClient(clientId, context.Response);

            try
            {
                // Send initial connected event
                var connectedEvent = new { type = "connected", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                await WriteSSEEvent(context.Response, connectedEvent, ct);

                // Send initial_load with projects list
                var projects = GetAllProjects(db);
                var initialLoadEvent = new { type = "initial_load", projects, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                await WriteSSEEvent(context.Response, initialLoadEvent, ct);

                // Send initial processing status
                var processingEvent = new { type = "processing_status", isProcessing = false, queueDepth = 0 };
                await WriteSSEEvent(context.Response, processingEvent, ct);

                // Keep connection alive with heartbeats
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(30000, ct); // 30 second heartbeat
                    await WriteSSEEvent(context.Response, new { type = "heartbeat", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected - expected
            }
            finally
            {
                broadcaster.RemoveClient(clientId);
            }
        });
    }

    private static async Task WriteSSEEvent(HttpResponse response, object data, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await response.Body.WriteAsync(bytes, ct);
        await response.Body.FlushAsync(ct);
    }

    private static List<string> GetAllProjects(ClaudeMemDatabase db)
    {
        var projects = new List<string>();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT project
            FROM observations
            WHERE project IS NOT NULL
            GROUP BY project
            ORDER BY MAX(created_at_epoch) DESC
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                projects.Add(reader.GetString(0));
        }

        return projects;
    }
}
