namespace Game.Mcp;

/// <summary>
/// MCP-owned structure format. Values are stored in Y,Z,X order so the representation is
/// deterministic and independent of Survivalcraft save/serialization formats.
/// </summary>
public sealed record StructureData(int SizeX, int SizeY, int SizeZ, int[] Values)
{
    public int Volume => checked(SizeX * SizeY * SizeZ);

    public int Get(int x, int y, int z) => Values[(y * SizeZ + z) * SizeX + x];
}
