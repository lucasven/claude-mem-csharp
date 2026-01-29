using System.Text.Json.Serialization;

namespace ClaudeMem.Mcp.Protocol;

public record McpServerInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version
);

public record McpCapabilities(
    [property: JsonPropertyName("tools")] McpToolsCapability? Tools = null
);

public record McpToolsCapability(
    [property: JsonPropertyName("listChanged")] bool ListChanged = false
);

public record McpInitializeResult(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("capabilities")] McpCapabilities Capabilities,
    [property: JsonPropertyName("serverInfo")] McpServerInfo ServerInfo
);

public record McpTool(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("inputSchema")] object InputSchema
);

public record McpToolsListResult(
    [property: JsonPropertyName("tools")] List<McpTool> Tools
);

public record McpCallToolParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] Dictionary<string, object?>? Arguments
);

public record McpToolContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text
);

public record McpCallToolResult(
    [property: JsonPropertyName("content")] List<McpToolContent> Content,
    [property: JsonPropertyName("isError")] bool IsError = false
);
