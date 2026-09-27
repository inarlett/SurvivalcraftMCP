using System.Collections.Generic;
using Engine;
namespace Game.Mcp;

public interface IBlockOperator
{
    IReadOnlyList<BlockTypeInfo> ListBlocks();
    BlockTypeInfo ResolveBlock(string name);
    BlockTypeInfo ResolveBlock(int contents);
    int MakeBlockValue(int contents, int data = 0, int light = 0);
    BlockData GetBlock(BlockPosition position);
    void SetBlock(BlockPosition position, BlockData block);
    void SetBlocks(IReadOnlyList<(BlockPosition Position, BlockData Block)> blocks);
    void Fill(BlockPosition min, BlockPosition max, BlockData block);
    int BuildBox(BlockPosition origin, int sizeX, int sizeY, int sizeZ, BlockData block, bool hollow);
    int Flatten(BlockPosition min, BlockPosition max, int targetY, BlockData ground);
    BlockData[] GetRegion(BlockPosition min, BlockPosition max);
    IReadOnlyList<RegionCell> GetRegionCells(BlockPosition min, BlockPosition max, int? contents, bool nonAirOnly);
    IReadOnlyList<RegionCell> FindBlocks(BlockPosition min, BlockPosition max, int? contents, bool nonAirOnly, int maxResults);
    IReadOnlyList<SurfaceCell> GetSurfaceHeights(BlockPosition min, BlockPosition max);
    IReadOnlyList<CollisionHit> CheckCollision(Vector3 min, Vector3 max, int maxResults);
    int BuildWall(BlockPosition origin, int width, int height, int thickness, string axis, BlockData block);
    int BuildCylinder(BlockPosition origin, int radius, int height, BlockData block, bool hollow);
    int BuildStaircase(BlockPosition origin, int steps, int width, string axis, int direction, BlockData block);
    int ReplaceContents(BlockPosition min, BlockPosition max, int fromContents, int toContents, bool preserveData);
    StructureData CaptureStructure(BlockPosition min, BlockPosition max);
    void PlaceStructure(BlockPosition origin, StructureData structure, int rotationY, bool mirrorX, bool skipAir);
    bool UndoLast();
}
