using System;
using System.Collections.Generic;
using Engine;
namespace Game.Mcp;

/// <summary>Strongly typed adapter for the Survivalcraft API 1.9 Terrain/SubsystemTerrain API.</summary>
public sealed class TerrainBlockOperator : IBlockOperator
{
    private const int MaxCellsPerOperation = 65536;
    private const int MaxUndoOperations = 16;
    private readonly SubsystemTerrain _subsystemTerrain;
    private readonly Stack<List<(BlockPosition Position, int Value)>> _undo = new();
    private List<(BlockPosition Position, int Value)> _activeUndo;
    private bool _undoing;
    public WriteReport LastWriteReport { get; private set; } = new();

    public TerrainBlockOperator(SubsystemTerrain subsystemTerrain) =>
        _subsystemTerrain = subsystemTerrain ?? throw new ArgumentNullException(nameof(subsystemTerrain));

    private Terrain Terrain => _subsystemTerrain.Terrain
        ?? throw new InvalidOperationException("Terrain is not ready.");

    public IReadOnlyList<BlockTypeInfo> ListBlocks()
    {
        var result = new List<BlockTypeInfo>();
        for (int i = 0; i < BlocksManager.Blocks.Length; i++)
        {
            var block = BlocksManager.Blocks[i];
            if (block != null)
            {
                int value = Terrain.MakeBlockValue(i);
                result.Add(CreateBlockTypeInfo(i, block));
            }
        }
        return result;
    }

    public BlockTypeInfo ResolveBlock(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Block name is required.");
        for (int i = 0; i < BlocksManager.Blocks.Length; i++)
        {
            var block = BlocksManager.Blocks[i];
            if (block == null) continue;
            if (string.Equals(block.GetType().Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(block.DefaultDisplayName, name, StringComparison.OrdinalIgnoreCase))
            {
                return CreateBlockTypeInfo(i, block);
            }
        }
        throw new ArgumentException($"Unknown block name: {name}");
    }

    public BlockTypeInfo ResolveBlock(int contents)
    {
        if (contents < 0 || contents >= BlocksManager.Blocks.Length || BlocksManager.Blocks[contents] == null)
            throw new ArgumentException($"Unknown block contents: {contents}");
        return CreateBlockTypeInfo(contents, BlocksManager.Blocks[contents]);
    }

    private BlockTypeInfo CreateBlockTypeInfo(int index, Block block)
    {
        var schema = DescribeDataSchema(block);
        var variants = new List<BlockVariantInfo>();
        foreach (int value in block.GetCreativeValues())
        {
            int data = Terrain.ExtractData(value);
            variants.Add(new BlockVariantInfo(value, data, block.GetDisplayName(_subsystemTerrain, value), DecodeData(block, data)));
        }
        return new BlockTypeInfo(index, block.GetType().Name, block.GetDisplayName(_subsystemTerrain, Terrain.MakeBlockValue(index)), variants,
            schema, block.DefaultCreativeData, block.IsCollidable);
    }

    private IReadOnlyList<BlockDataFieldInfo> DescribeDataSchema(Block block)
    {
        var result = new List<BlockDataFieldInfo>();
        if (block is DoorBlock)
        {
            result.Add(new("rotation", "0x03", 0, "Door rotation: 0..3, quarter turns."));
            result.Add(new("open", "0x04", 2, "Whether the door is open."));
            result.Add(new("rightHanded", "0x08", 3, "Door hinge handedness."));
        }
        else if (block is StairsBlock)
        {
            result.Add(new("rotation", "0x03", 0, "Stair rotation: 0..3, quarter turns."));
            result.Add(new("upsideDown", "0x04", 2, "Whether the stair is upside down."));
            result.Add(new("cornerType", "0x18", 3, "0 none, 1 outer corner, 2 inner corner."));
            result.Add(new("color", "0xE0", 5, "Optional palette color."));
        }
        else if (block is SlabBlock)
        {
            result.Add(new("top", "0x01", 0, "Whether the slab is mounted in the top half."));
            result.Add(new("color", "0x1E", 1, "Optional palette color."));
        }
        else if (block is FurnitureBlock)
        {
            result.Add(new("rotation", "0x03", 0, "Furniture rotation: 0..3, quarter turns."));
            result.Add(new("designIndex", "bits 2..12 and 15..17", 0, "FurnitureDesign index, not a normal block variant."));
        }
        else if (block is TrapdoorBlock)
        {
            result.Add(new("rotation", "0x03", 0, "Trapdoor rotation: 0..3."));
            result.Add(new("open", "0x04", 2, "Whether the trapdoor is open."));
            result.Add(new("upsideDown", "0x08", 3, "Whether the trapdoor is mounted on the ceiling."));
        }
        else if (block is TorchBlock)
        {
            result.Add(new("mountingFace", "0x07", 0, "0..3 wall faces, 4 floor-mounted."));
        }
        else if (block is PaintedCubeBlock)
        {
            result.Add(new("color", "0x1E", 1, "Optional palette color; null means unpainted."));
        }
        else if (block is CarpetBlock)
        {
            result.Add(new("color", "0x0F", 0, "Carpet palette color."));
        }
        return result;
    }

    private IReadOnlyDictionary<string, object> DecodeData(Block block, int data)
    {
        var fields = new Dictionary<string, object>();
        switch (block)
        {
            case DoorBlock:
                fields["rotation"] = DoorBlock.GetRotation(data);
                fields["open"] = DoorBlock.GetOpen(data);
                fields["rightHanded"] = DoorBlock.GetRightHanded(data);
                fields["hingeFace"] = DoorBlock.GetHingeFace(data);
                break;
            case StairsBlock:
                fields["rotation"] = StairsBlock.GetRotation(data);
                fields["upsideDown"] = StairsBlock.GetIsUpsideDown(data);
                fields["cornerType"] = StairsBlock.GetCornerType(data).ToString();
                fields["color"] = StairsBlock.GetColor(data);
                break;
            case SlabBlock:
                fields["top"] = SlabBlock.GetIsTop(data);
                fields["color"] = SlabBlock.GetColor(data);
                break;
            case FurnitureBlock:
                fields["rotation"] = FurnitureBlock.GetRotation(data);
                int designIndex = FurnitureBlock.GetDesignIndex(data);
                fields["designIndex"] = designIndex;
                FurnitureDesign design = _subsystemTerrain.SubsystemFurnitureBlockBehavior?.GetDesign(designIndex);
                fields["designName"] = design?.Name;
                break;
            case TrapdoorBlock:
                fields["rotation"] = TrapdoorBlock.GetRotation(data);
                fields["open"] = TrapdoorBlock.GetOpen(data);
                fields["upsideDown"] = TrapdoorBlock.GetUpsideDown(data);
                fields["mountingFace"] = TrapdoorBlock.GetMountingFace(data);
                break;
            case TorchBlock:
                fields["mountingFace"] = data;
                break;
            case PaintedCubeBlock:
                fields["color"] = PaintedCubeBlock.GetColor(data);
                break;
            case CarpetBlock:
                fields["color"] = CarpetBlock.GetColor(data);
                break;
        }
        return fields;
    }

    public int MakeBlockValue(int contents, int data = 0, int light = 0) =>
        Terrain.MakeBlockValue(contents, light, data);

    public BlockData GetBlock(BlockPosition position) =>
        new(Terrain.GetCellValue(position.X, position.Y, position.Z));

    public void SetBlock(BlockPosition position, BlockData block) =>
        WriteSingle(position, block.Value);

    public void SetBlocks(IReadOnlyList<(BlockPosition Position, BlockData Block)> blocks)
    {
        if (blocks.Count > MaxCellsPerOperation)
            throw new ArgumentException($"Too many cells. Maximum is {MaxCellsPerOperation}.");

        BeginWrite();
        foreach (var item in blocks)
            WriteCell(item.Position.X, item.Position.Y, item.Position.Z, item.Block.Value);
        FinishWrite();
    }

    public void Fill(BlockPosition min, BlockPosition max, BlockData block)
    {
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        long volume = Volume(minX, minY, minZ, maxX, maxY, maxZ);
        EnsureLimit(volume);

        BeginWrite();
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    WriteCell(x, y, z, block.Value);
        FinishWrite();
    }

    public int BuildBox(BlockPosition origin, int sizeX, int sizeY, int sizeZ, BlockData block, bool hollow)
    {
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            throw new ArgumentException("Box dimensions must be positive.");
        EnsureLimit((long)sizeX * sizeY * sizeZ);
        BeginWrite();
        int count = 0;
        for (int y = 0; y < sizeY; y++)
            for (int z = 0; z < sizeZ; z++)
                for (int x = 0; x < sizeX; x++)
                {
                    bool boundary = x == 0 || y == 0 || z == 0 || x == sizeX - 1 || y == sizeY - 1 || z == sizeZ - 1;
                    if (hollow && !boundary) continue;
                    WriteCell(origin.X + x, origin.Y + y, origin.Z + z, block.Value);
                    count++;
                }
        FinishWrite();
        return count;
    }

    public int Flatten(BlockPosition min, BlockPosition max, int targetY, BlockData ground)
    {
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        if (targetY < minY || targetY > maxY)
            throw new ArgumentException("targetY must be inside the requested vertical range.");
        EnsureLimit(Volume(minX, minY, minZ, maxX, maxY, maxZ));
        BeginWrite();
        int count = 0;
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    int value = y < targetY ? ground.Value : 0;
                    WriteCell(x, y, z, value);
                    count++;
                }
        FinishWrite();
        return count;
    }

    public BlockData[] GetRegion(BlockPosition min, BlockPosition max)
    {
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        long volume = Volume(minX, minY, minZ, maxX, maxY, maxZ);
        EnsureLimit(volume);

        var result = new BlockData[(int)volume];
        int i = 0;
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    result[i++] = new BlockData(Terrain.GetCellValue(x, y, z));
        return result;
    }

    public IReadOnlyList<RegionCell> GetRegionCells(BlockPosition min, BlockPosition max, int? contents, bool nonAirOnly)
    {
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        EnsureLimit(Volume(minX, minY, minZ, maxX, maxY, maxZ));
        var result = new List<RegionCell>();
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    int value = Terrain.GetCellValue(x, y, z);
                    int blockContents = Terrain.ExtractContents(value);
                    if ((nonAirOnly && blockContents == 0) || (contents.HasValue && blockContents != contents.Value)) continue;
                    result.Add(new RegionCell(x, y, z, value));
                }
        return result;
    }

    public IReadOnlyList<RegionCell> FindBlocks(BlockPosition min, BlockPosition max, int? contents, bool nonAirOnly, int maxResults)
    {
        if (maxResults <= 0 || maxResults > MaxCellsPerOperation) throw new ArgumentException("maxResults must be between 1 and 65536.");
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        EnsureLimit(Volume(minX, minY, minZ, maxX, maxY, maxZ));
        var result = new List<RegionCell>();
        for (int y = minY; y <= maxY && result.Count < maxResults; y++)
            for (int z = minZ; z <= maxZ && result.Count < maxResults; z++)
                for (int x = minX; x <= maxX && result.Count < maxResults; x++)
                {
                    int value = Terrain.GetCellValue(x, y, z);
                    int blockContents = Terrain.ExtractContents(value);
                    if ((nonAirOnly && blockContents == 0) || (contents.HasValue && blockContents != contents.Value)) continue;
                    result.Add(new RegionCell(x, y, z, value));
                }
        return result;
    }

    public IReadOnlyList<SurfaceCell> GetSurfaceHeights(BlockPosition min, BlockPosition max)
    {
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        long columns = ((long)maxX - minX + 1) * (maxZ - minZ + 1);
        EnsureLimit(columns);
        var result = new List<SurfaceCell>((int)columns);
        for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                int y = maxY;
                while (y >= minY && Terrain.ExtractContents(Terrain.GetCellValue(x, y, z)) == 0) y--;
                result.Add(new SurfaceCell(x, z, y, y >= minY ? Terrain.GetCellValue(x, y, z) : 0));
            }
        return result;
    }

    public IReadOnlyList<CollisionHit> CheckCollision(Vector3 min, Vector3 max, int maxResults)
    {
        if (maxResults <= 0 || maxResults > MaxCellsPerOperation)
            throw new ArgumentException("maxResults must be between 1 and 65536.");
        if (max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z)
            throw new ArgumentException("Collision bounds must have positive size.");

        int minX = (int)MathF.Floor(min.X);
        int minY = (int)MathF.Floor(min.Y);
        int minZ = (int)MathF.Floor(min.Z);
        int maxX = (int)MathF.Ceiling(max.X) - 1;
        int maxY = (int)MathF.Ceiling(max.Y) - 1;
        int maxZ = (int)MathF.Ceiling(max.Z) - 1;
        EnsureLimit(Volume(minX, minY, minZ, maxX, maxY, maxZ));

        var query = new BoundingBox(min, max);
        var result = new List<CollisionHit>();
        for (int y = minY; y <= maxY && result.Count < maxResults; y++)
            for (int z = minZ; z <= maxZ && result.Count < maxResults; z++)
                for (int x = minX; x <= maxX && result.Count < maxResults; x++)
                {
                    int value = Terrain.GetCellValue(x, y, z);
                    int contents = Terrain.ExtractContents(value);
                    if (contents == 0) continue;
                    Block block = BlocksManager.Blocks[contents];
                    if (block == null || !block.GetIsCollidable(null, value)) continue;
                    BoundingBox[] collisionBoxes = block.GetCustomCollisionBoxes(_subsystemTerrain, value);
                    if (collisionBoxes == null) continue;
                    foreach (BoundingBox localBox in collisionBoxes)
                    {
                        var worldBox = new BoundingBox(
                            localBox.Min + new Vector3(x, y, z),
                            localBox.Max + new Vector3(x, y, z));
                        if (!worldBox.Intersection(query)) continue;
                        BoundingBox intersection = BoundingBox.Intersection(worldBox, query);
                        if (intersection.Max.X <= intersection.Min.X
                            || intersection.Max.Y <= intersection.Min.Y
                            || intersection.Max.Z <= intersection.Min.Z) continue;
                        result.Add(new CollisionHit(x, y, z, value,
                            worldBox.Min.X, worldBox.Min.Y, worldBox.Min.Z,
                            worldBox.Max.X, worldBox.Max.Y, worldBox.Max.Z));
                        if (result.Count >= maxResults) break;
                    }
                }
        return result;
    }

    public int BuildWall(BlockPosition origin, int width, int height, int thickness, string axis, BlockData block)
    {
        if (width <= 0 || height <= 0 || thickness <= 0) throw new ArgumentException("Wall dimensions must be positive.");
        if (!string.Equals(axis, "x", StringComparison.OrdinalIgnoreCase) && !string.Equals(axis, "z", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("axis must be 'x' or 'z'.");
        EnsureLimit((long)width * height * thickness);
        BeginWrite();
        int count = 0;
        for (int y = 0; y < height; y++)
            for (int t = 0; t < thickness; t++)
                for (int w = 0; w < width; w++)
                {
                    int x = origin.X + (axis.Equals("x", StringComparison.OrdinalIgnoreCase) ? w : t);
                    int z = origin.Z + (axis.Equals("x", StringComparison.OrdinalIgnoreCase) ? t : w);
                    WriteCell(x, origin.Y + y, z, block.Value);
                    count++;
                }
        FinishWrite();
        return count;
    }

    public int BuildCylinder(BlockPosition origin, int radius, int height, BlockData block, bool hollow)
    {
        if (radius <= 0 || height <= 0) throw new ArgumentException("Cylinder dimensions must be positive.");
        EnsureLimit((long)(radius * 2 + 1) * (radius * 2 + 1) * height);
        BeginWrite();
        int count = 0;
        int radiusSquared = radius * radius;
        for (int y = 0; y < height; y++)
            for (int z = -radius; z <= radius; z++)
                for (int x = -radius; x <= radius; x++)
                {
                    int distance = x * x + z * z;
                    if (distance > radiusSquared || (hollow && distance < (radius - 1) * (radius - 1))) continue;
                    WriteCell(origin.X + x, origin.Y + y, origin.Z + z, block.Value);
                    count++;
                }
        FinishWrite();
        return count;
    }

    public int BuildStaircase(BlockPosition origin, int steps, int width, string axis, int direction, BlockData block)
    {
        if (steps <= 0 || width <= 0) throw new ArgumentException("Staircase dimensions must be positive.");
        if (!string.Equals(axis, "x", StringComparison.OrdinalIgnoreCase) && !string.Equals(axis, "z", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("axis must be 'x' or 'z'.");
        if (direction is not (-1 or 1)) throw new ArgumentException("direction must be -1 or 1.");
        EnsureLimit((long)steps * steps * width);
        BeginWrite();
        int count = 0;
        for (int step = 0; step < steps; step++)
            for (int y = 0; y <= step; y++)
                for (int w = 0; w < width; w++)
                {
                    int x = origin.X + (axis.Equals("x", StringComparison.OrdinalIgnoreCase) ? direction * step : w);
                    int z = origin.Z + (axis.Equals("x", StringComparison.OrdinalIgnoreCase) ? w : direction * step);
                    WriteCell(x, origin.Y + y, z, block.Value);
                    count++;
                }
        FinishWrite();
        return count;
    }

    public int ReplaceContents(BlockPosition min, BlockPosition max, int fromContents, int toContents, bool preserveData)
    {
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        EnsureLimit(Volume(minX, minY, minZ, maxX, maxY, maxZ));
        if (fromContents < 0 || fromContents >= BlocksManager.Blocks.Length || toContents < 0 || toContents >= BlocksManager.Blocks.Length)
            throw new ArgumentException("Block contents ID is out of range.");
        BeginWrite();
        int count = 0;
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    int oldValue = Terrain.GetCellValue(x, y, z);
                    if (Terrain.ExtractContents(oldValue) != fromContents) continue;
                    int newValue = preserveData
                        ? Terrain.ReplaceContents(oldValue, toContents)
                        : Terrain.MakeBlockValue(toContents);
                    WriteCell(x, y, z, newValue);
                    count++;
                }
        FinishWrite();
        return count;
    }

    public StructureData CaptureStructure(BlockPosition min, BlockPosition max)
    {
        Normalize(min, max, out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ);
        long volume = Volume(minX, minY, minZ, maxX, maxY, maxZ);
        EnsureLimit(volume);

        var values = new int[(int)volume];
        int i = 0;
        for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    values[i++] = Terrain.GetCellValue(x, y, z);

        return new StructureData(maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1, values);
    }

    public void PlaceStructure(BlockPosition origin, StructureData structure, int rotationY, bool mirrorX, bool skipAir)
    {
        ValidateStructure(structure);
        rotationY = ((rotationY % 360) + 360) % 360;
        if (rotationY is not (0 or 90 or 180 or 270))
            throw new ArgumentException("rotationY must be 0, 90, 180 or 270.");

        BeginWrite();
        for (int y = 0; y < structure.SizeY; y++)
        {
            for (int z = 0; z < structure.SizeZ; z++)
            {
                for (int x = 0; x < structure.SizeX; x++)
                {
                    int value = structure.Get(x, y, z);
                    if (skipAir && Terrain.ExtractContents(value) == 0)
                        continue;

                    Transform(x, z, structure.SizeX, structure.SizeZ, rotationY, mirrorX, out int tx, out int tz);
                    WriteCell(origin.X + tx, origin.Y + y, origin.Z + tz, value);
                }
            }
        }
        FinishWrite();
    }

    public bool UndoLast()
    {
        if (_undo.Count == 0)
            return false;

        List<(BlockPosition Position, int Value)> changes = _undo.Pop();
        BeginWrite();
        _undoing = true;
        try
        {
            for (int i = changes.Count - 1; i >= 0; i--)
            {
                (BlockPosition position, int value) = changes[i];
                WriteCell(position.X, position.Y, position.Z, value);
            }
        }
        finally
        {
            _undoing = false;
        }
        FinishWrite();
        LastWriteReport.UndoAvailable = _undo.Count > 0;
        return true;
    }

    private void WriteSingle(BlockPosition position, int value)
    {
        BeginWrite();
        WriteCell(position.X, position.Y, position.Z, value);
        FinishWrite();
    }

    private void BeginWrite()
    {
        if (_activeUndo != null)
            throw new InvalidOperationException("Nested terrain write is not supported.");
        _activeUndo = new List<(BlockPosition Position, int Value)>();
        LastWriteReport = new WriteReport();
    }

    private void FinishWrite()
    {
        if (_activeUndo == null)
            throw new InvalidOperationException("Terrain write was not started.");
        if (!_undoing && _activeUndo.Count > 0)
        {
            _undo.Push(_activeUndo);
            while (_undo.Count > MaxUndoOperations)
            {
                var keep = _undo.ToArray();
                _undo.Clear();
                for (int i = MaxUndoOperations - 1; i >= 0; i--)
                    _undo.Push(keep[i]);
            }
        }
        _activeUndo = null;
        LastWriteReport.UndoAvailable = _undo.Count > 0;
    }

    private void WriteCell(int x, int y, int z, int requested)
    {
        if (_activeUndo == null)
            throw new InvalidOperationException("Terrain write was not started.");
        LastWriteReport.Requested++;
        int before = Terrain.GetCellValue(x, y, z);
        if (before == requested)
            return;

        var position = new BlockPosition(x, y, z);
        if (Terrain.ExtractContents(before) != 0)
        {
            LastWriteReport.OverwrittenNonAir++;
            AddSample(LastWriteReport.OverwrittenSamples, new WriteChange(position, before, requested, requested));
        }
        _subsystemTerrain.ChangeCell(x, y, z, requested);
        int after = Terrain.GetCellValue(x, y, z);
        if (after != before)
        {
            LastWriteReport.Changed++;
            if (!_undoing)
                _activeUndo.Add((position, before));
        }
        if (Terrain.ExtractContents(requested) != 0 && Terrain.ExtractContents(after) == 0)
        {
            LastWriteReport.Disappeared++;
            AddSample(LastWriteReport.DisappearedSamples, new WriteChange(position, before, requested, after));
        }
    }

    private static void AddSample(List<WriteChange> samples, WriteChange change)
    {
        if (samples.Count < 64)
            samples.Add(change);
    }

    private static void Transform(int x, int z, int sizeX, int sizeZ, int rotationY, bool mirrorX, out int tx, out int tz)
    {
        if (mirrorX) x = sizeX - 1 - x;
        switch (rotationY)
        {
            case 0: tx = x; tz = z; break;
            case 90: tx = sizeZ - 1 - z; tz = x; break;
            case 180: tx = sizeX - 1 - x; tz = sizeZ - 1 - z; break;
            case 270: tx = z; tz = sizeX - 1 - x; break;
            default: throw new ArgumentOutOfRangeException(nameof(rotationY));
        }
    }

    private static void ValidateStructure(StructureData structure)
    {
        if (structure.SizeX <= 0 || structure.SizeY <= 0 || structure.SizeZ <= 0)
            throw new ArgumentException("Structure dimensions must be positive.");
        if (structure.SizeX > 256 || structure.SizeY > 256 || structure.SizeZ > 256)
            throw new ArgumentException("Structure dimensions are too large.");
        EnsureLimit(structure.Volume);
        if (structure.Values.Length != structure.Volume)
            throw new ArgumentException("Structure values length does not match its dimensions.");
    }

    private static void Normalize(BlockPosition a, BlockPosition b,
        out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ)
    {
        minX = Math.Min(a.X, b.X); maxX = Math.Max(a.X, b.X);
        minY = Math.Min(a.Y, b.Y); maxY = Math.Max(a.Y, b.Y);
        minZ = Math.Min(a.Z, b.Z); maxZ = Math.Max(a.Z, b.Z);
    }

    private static long Volume(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) =>
        ((long)maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);

    private static void EnsureLimit(long volume)
    {
        if (volume <= 0 || volume > MaxCellsPerOperation)
            throw new ArgumentException($"Operation volume must be between 1 and {MaxCellsPerOperation} cells.");
    }
}
