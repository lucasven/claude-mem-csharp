using System.Text.Json;
using ClaudeMem.Core.Data;
using ClaudeMem.Mcp.Protocol;
using ClaudeMem.Mcp.Tools;

namespace ClaudeMem.Mcp;

public class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "claude-mem-csharp";
    private const string ServerVersion = "1.0.0";

    private readonly MemoryTools _tools;
    private readonly TextWriter _output;
    private readonly TextReader _input;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public McpServer(ClaudeMemDatabase db, TextReader? input = null, TextWriter? output = null)
    {
        _tools = new MemoryTools(db);
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOptions);
                if (request == null) continue;

                var response = HandleRequest(request);
                if (response != null)
                {
                    var json = JsonSerializer.Serialize(response, JsonOptions);
                    await _output.WriteLineAsync(json);
                    await _output.FlushAsync(cancellationToken);
                }
            }
            catch (JsonException ex)
            {
                var errorResponse = new JsonRpcResponse
                {
                    Id = null,
                    Error = new JsonRpcError(-32700, $"Parse error: {ex.Message}")
                };
                await _output.WriteLineAsync(JsonSerializer.Serialize(errorResponse, JsonOptions));
                await _output.FlushAsync(cancellationToken);
            }
        }
    }

    private JsonRpcResponse? HandleRequest(JsonRpcRequest request)
    {
        // Notifications (no id) don't get a response
        if (request.Id == null && request.Method == "notifications/initialized")
            return null;

        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "tools/list" => HandleToolsList(request),
            "tools/call" => HandleToolsCall(request),
            _ => new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError(-32601, $"Method not found: {request.Method}")
            }
        };
    }

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        var result = new McpInitializeResult(
            ProtocolVersion: ProtocolVersion,
            Capabilities: new McpCapabilities(
                Tools: new McpToolsCapability(ListChanged: false)
            ),
            ServerInfo: new McpServerInfo(
                Name: ServerName,
                Version: ServerVersion
            )
        );

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = result
        };
    }

    private JsonRpcResponse HandleToolsList(JsonRpcRequest request)
    {
        var result = new McpToolsListResult(
            Tools: MemoryTools.GetToolDefinitions()
        );

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = result
        };
    }

    private JsonRpcResponse HandleToolsCall(JsonRpcRequest request)
    {
        try
        {
            var paramsJson = JsonSerializer.SerializeToElement(request.Params, JsonOptions);
            var callParams = JsonSerializer.Deserialize<McpCallToolParams>(paramsJson, JsonOptions);

            if (callParams == null)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError(-32602, "Invalid params: missing tool call parameters")
                };
            }

            var result = _tools.CallTool(callParams.Name, callParams.Arguments);

            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = new McpCallToolResult(
                    Content: [new McpToolContent("text", $"Error: {ex.Message}")],
                    IsError: true
                )
            };
        }
    }
}
