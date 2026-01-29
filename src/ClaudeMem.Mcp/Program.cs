using ClaudeMem.Core.Data;
using ClaudeMem.Mcp;

// MCP servers communicate over stdio - don't write anything else to stdout
var db = new ClaudeMemDatabase();
var server = new McpServer(db);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunAsync(cts.Token);
