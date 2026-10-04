using System.Text.Json.Nodes;

namespace DeltaStudio.Mcp.Protocol;

/// <summary>JSON-RPC 2.0 error codes used by this server.</summary>
public static class RpcError
{
    /// <summary>Standard codes.</summary>
    public const int ParseError = -32700;
    /// <summary>Invalid Request.</summary>
    public const int InvalidRequest = -32600;
    /// <summary>Method not found.</summary>
    public const int MethodNotFound = -32601;
    /// <summary>Invalid params.</summary>
    public const int InvalidParams = -32602;
    /// <summary>Internal error.</summary>
    public const int InternalError = -32603;
}

/// <summary>JSON-RPC helpers (newline-delimited JSON framing per the MCP stdio transport).</summary>
public static class JsonRpcMessage
{
    /// <summary>Builds a success response.</summary>
    public static JsonObject Result(JsonNode? id, JsonNode? result)
    {
        var o = new JsonObject { ["jsonrpc"] = "2.0" };
        if (id is not null)
        {
            o["id"] = id.DeepClone();
        }

        o["result"] = result;
        return o;
    }

    /// <summary>Builds an error response.</summary>
    public static JsonObject Error(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var err = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null)
        {
            err["data"] = data;
        }

        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = err };
    }

    /// <summary>Builds a notification.</summary>
    public static JsonObject Notification(string method, JsonObject? @params)
    {
        var o = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (@params is not null)
        {
            o["params"] = @params;
        }

        return o;
    }
}
