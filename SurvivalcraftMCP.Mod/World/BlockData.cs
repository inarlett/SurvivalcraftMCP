using System.Collections.Generic;
using Engine;

namespace Game.Mcp;

/// <summary>Serializable representation of one Survivalcraft terrain cell.</summary>
public readonly record struct BlockData(int Value)
{
    public int Contents => Game.Terrain.ExtractContents(Value);
    public int Data => Game.Terrain.ExtractData(Value);
    public int Light => Game.Terrain.ExtractLight(Value);
}

public readonly record struct RegionCell(int X, int Y, int Z, int Value)
{
    public int Contents => Game.Terrain.ExtractContents(Value);
    public int Data => Game.Terrain.ExtractData(Value);
    public int Light => Game.Terrain.ExtractLight(Value);
}

public readonly record struct SurfaceCell(int X, int Z, int Y, int Value)
{
    public int Contents => Game.Terrain.ExtractContents(Value);
    public int Data => Game.Terrain.ExtractData(Value);
    public int Light => Game.Terrain.ExtractLight(Value);
}

public readonly record struct CollisionHit(
    int X,
    int Y,
    int Z,
    int Value,
    float MinX,
    float MinY,
    float MinZ,
    float MaxX,
    float MaxY,
    float MaxZ);

public readonly record struct WriteChange(BlockPosition Position, int Before, int Requested, int After);

public sealed class WriteReport
{
    public int Requested { get; internal set; }
    public int Changed { get; internal set; }
    public int OverwrittenNonAir { get; internal set; }
    public int Disappeared { get; internal set; }
    public bool UndoAvailable { get; internal set; }
    public List<WriteChange> OverwrittenSamples { get; } = new();
    public List<WriteChange> DisappearedSamples { get; } = new();
}
