using System.Text.Json;

namespace Game.Mcp.Communication;

public sealed record RpcRequest(string Id, string Method, JsonElement Params);
public sealed record RpcResponse(string Id, object Result = null, string Error = null);
