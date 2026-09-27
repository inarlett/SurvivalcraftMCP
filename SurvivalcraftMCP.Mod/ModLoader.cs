using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Engine;
namespace Game;

public sealed class McpModLoader : ModLoader
{
    public const string PackageName = "SurvivalcraftMCP";
    public const int DefaultPort = 28899;

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        var harmony = new Harmony(PackageName);
        harmony.PatchAll();
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        actions.Add(() => Log.Information($"Survivalcraft MCP Bridge loaded. Waiting for MCP clients on localhost:{GetConfiguredPort()}."));
    }

    public static int GetConfiguredPort()
    {
        if (ModSettingsManager.TryGet(out string portText, PackageName, "BridgeSettings", "ExternalPort")
            && int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
            && port is >= 1024 and <= 65535)
            return port;
        return DefaultPort;
    }

}