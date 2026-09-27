using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Game.Mcp.Communication;

/// <summary>Registration point for other mods to expose safe, main-thread MCP tools.</summary>
public sealed record CustomMcpTool(string Name, string Description, object InputSchema, Func<JsonElement, object> Handler);

public static class CustomMcpToolRegistry
{
    private static readonly Dictionary<string, CustomMcpTool> Tools = new(StringComparer.Ordinal);
    private static readonly object Sync = new();

    public static void Register(string name, string description, object inputSchema, Func<JsonElement, object> handler)
    {
        if (string.IsNullOrWhiteSpace(name) || !IsValidName(name))
            throw new ArgumentException("Tool name must contain only letters, digits, '_' or '-'.", nameof(name));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Tool description is required.", nameof(description));
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);
        lock (Sync)
        {
            if (Tools.ContainsKey(name)) throw new InvalidOperationException($"MCP tool already registered: {name}");
            Tools.Add(name, new CustomMcpTool(name, description, inputSchema, handler));
        }
    }

    public static bool TryGet(string name, out CustomMcpTool tool)
    {
        lock (Sync) return Tools.TryGetValue(name, out tool);
    }

    public static IReadOnlyList<object> Describe()
    {
        lock (Sync)
        {
            var result = new List<object>(Tools.Count);
            foreach (var tool in Tools.Values)
                result.Add(new { name = tool.Name, description = tool.Description, inputSchema = tool.InputSchema });
            return result;
        }
    }

    private static bool IsValidName(string name)
    {
        foreach (char c in name)
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-')) return false;
        return true;
    }
}
