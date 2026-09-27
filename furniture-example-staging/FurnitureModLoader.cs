using System;
using System.Collections.Generic;
using Engine;

namespace Game;

public sealed class FurnitureModLoader : ModLoader
{
    public const string PackageName = "survivalcraft.furniture-mcp-example";

    public override void __ModInitialize()
    {
        ModsManager.RegisterHook("OnLoadingFinished", this);
    }

    public override void OnLoadingFinished(List<Action> actions)
    {
        actions.Add(() =>
        {
            FurnitureMcpTools.Register();
            Log.Information("Furniture MCP Example loaded. Furniture tools are ready.");
        });
    }
}
