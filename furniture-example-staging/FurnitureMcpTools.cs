using System;
using System.Collections.Generic;
using System.Text.Json;
using Engine;
using Game.Mcp.Communication;

namespace Game;

internal static class FurnitureMcpTools
{
    private const int FurnitureContents = 227;
    private const int MaxDimension = 8;
    private static bool s_registered;

    public static void Register()
    {
        if (s_registered)
            return;

        CustomMcpToolRegistry.Register(
            "furniture_list_designs",
            "List furniture designs currently registered in the loaded world.",
            ObjectSchema(),
            _ => ListDesigns());

        CustomMcpToolRegistry.Register(
            "furniture_create",
            "Create and register a furniture design from a material block. Shapes are solid, table, or bench.",
            new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Design name, up to 20 characters." },
                    contents = new { type = "integer", description = "Material block contents ID. Resolve it with the MCP resolve_block tool." },
                    data = new { type = "integer", description = "Material block data value, normally 0." },
                    width = new { type = "integer", minimum = 2, maximum = MaxDimension },
                    height = new { type = "integer", minimum = 2, maximum = MaxDimension },
                    depth = new { type = "integer", minimum = 2, maximum = MaxDimension },
                    shape = new { type = "string", @enum = new[] { "solid", "table", "bench" } }
                },
                required = new[] { "name", "contents", "width", "height", "depth" }
            },
            CreateDesign);

        CustomMcpToolRegistry.Register(
            "furniture_place",
            "Place a registered furniture design at a terrain cell. Rotation is a quarter-turn around the Y axis.",
            new
            {
                type = "object",
                properties = new
                {
                    x = new { type = "integer" },
                    y = new { type = "integer" },
                    z = new { type = "integer" },
                    designIndex = new { type = "integer", minimum = 0 },
                    rotation = new { type = "integer", minimum = 0, maximum = 3 },
                    overwrite = new { type = "boolean", description = "Allow replacing a non-air cell. Defaults to false." }
                },
                required = new[] { "x", "y", "z", "designIndex" }
            },
            PlaceDesign);

        s_registered = true;
    }

    private static object ObjectSchema() => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    private static object ListDesigns()
    {
        var furniture = GetFurnitureSubsystem(out _);
        var designs = new List<object>();
        for (int i = 0; i < furniture.m_furnitureDesigns.Length; i++)
        {
            FurnitureDesign design = furniture.m_furnitureDesigns[i];
            if (design != null)
            {
                designs.Add(new
                {
                    index = design.Index,
                    name = design.Name,
                    resolution = design.Resolution,
                    terrainUseCount = design.m_terrainUseCount,
                    interactionMode = design.InteractionMode.ToString()
                });
            }
        }

        return new { ok = true, designs };
    }

    private static object CreateDesign(JsonElement arguments)
    {
        var furniture = GetFurnitureSubsystem(out SubsystemTerrain terrain);
        string name = RequiredString(arguments, "name");
        if (name.Length > FurnitureDesign.MaxNameLength)
            throw new ArgumentException($"name must be at most {FurnitureDesign.MaxNameLength} characters.");

        int contents = RequiredInt(arguments, "contents");
        int data = OptionalInt(arguments, "data", 0);
        int width = RequiredDimension(arguments, "width");
        int height = RequiredDimension(arguments, "height");
        int depth = RequiredDimension(arguments, "depth");
        string shape = OptionalString(arguments, "shape", "table");
        if (shape is not ("solid" or "table" or "bench"))
            throw new ArgumentException("shape must be solid, table, or bench.");
        if (contents <= 0 || contents >= BlocksManager.Blocks.Length || BlocksManager.Blocks[contents] == null)
            throw new ArgumentException($"Unknown material contents ID: {contents}.");
        if (!BlocksManager.Blocks[contents].CanBeBuiltIntoFurniture)
            throw new ArgumentException($"Block {contents} ({BlocksManager.Blocks[contents].GetType().Name}) cannot be built into furniture.");

        int resolution = Math.Max(Math.Max(width, height), depth);
        int materialValue = Terrain.MakeBlockValue(contents, data, 0);
        int[] values = new int[resolution * resolution * resolution];
        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (ShouldFill(shape, x, y, z, width, height, depth))
                        values[x + y * resolution + z * resolution * resolution] = materialValue;
                }
            }
        }

        var design = new FurnitureDesign(terrain);
        design.Name = name;
        design.InteractionMode = FurnitureInteractionMode.None;
        design.SetValues(resolution, values);
        FurnitureDesign registered = furniture.TryAddDesign(design);
        if (registered == null)
            throw new InvalidOperationException("The world has no free furniture design slots.");

        return new
        {
            ok = true,
            index = registered.Index,
            name = registered.Name,
            resolution = registered.Resolution,
            shape,
            materialContents = contents,
            materialData = data,
            placementValue = MakeFurnitureValue(registered, 0)
        };
    }

    private static object PlaceDesign(JsonElement arguments)
    {
        var furniture = GetFurnitureSubsystem(out SubsystemTerrain terrain);
        int x = RequiredInt(arguments, "x");
        int y = RequiredInt(arguments, "y");
        int z = RequiredInt(arguments, "z");
        int designIndex = RequiredInt(arguments, "designIndex");
        int rotation = RequiredInt(arguments, "rotation", 0);
        bool overwrite = OptionalBool(arguments, "overwrite", false);
        if (rotation is < 0 or > 3)
            throw new ArgumentException("rotation must be between 0 and 3.");
        FurnitureDesign design = furniture.GetDesign(designIndex)
            ?? throw new ArgumentException($"Unknown furniture design index: {designIndex}.");
        if (!terrain.Terrain.IsCellValid(x, y, z))
            throw new ArgumentException("The target cell is outside the valid terrain height range.");

        int oldValue = terrain.Terrain.GetCellValue(x, y, z);
        if (!overwrite && Terrain.ExtractContents(oldValue) != 0)
            throw new InvalidOperationException("Target cell is not empty. Set overwrite=true to replace it.");

        int value = MakeFurnitureValue(design, rotation);
        terrain.ChangeCell(x, y, z, value);
        int actualValue = terrain.Terrain.GetCellValue(x, y, z);
        return new
        {
            ok = Terrain.ExtractContents(actualValue) == FurnitureContents,
            x,
            y,
            z,
            designIndex,
            rotation,
            value,
            actualValue,
            overwritten = Terrain.ExtractContents(oldValue) != 0
        };
    }

    private static int MakeFurnitureValue(FurnitureDesign design, int rotation)
    {
        int data = FurnitureBlock.SetDesignIndex(
            0,
            design.Index,
            design.ShadowStrengthFactor,
            design.IsLightEmitter);
        data = FurnitureBlock.SetRotation(data, rotation);
        return Terrain.MakeBlockValue(FurnitureContents, 0, data);
    }

    private static bool ShouldFill(string shape, int x, int y, int z, int width, int height, int depth)
    {
        if (shape == "solid")
            return true;
        if (shape == "bench")
            return y == height - 1 || (y < height - 1 && (x == 0 || x == width - 1));
        if (y == height - 1)
            return true;
        return (x == 0 || x == width - 1) && (z == 0 || z == depth - 1);
    }

    private static SubsystemFurnitureBlockBehavior GetFurnitureSubsystem(out SubsystemTerrain terrain)
    {
        terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>(false)
            ?? throw new InvalidOperationException("No active game world is loaded.");
        return terrain.SubsystemFurnitureBlockBehavior
            ?? throw new InvalidOperationException("The furniture subsystem is not available in this world.");
    }

    private static int RequiredDimension(JsonElement args, string name)
    {
        int value = RequiredInt(args, name);
        if (value is < 2 or > MaxDimension)
            throw new ArgumentException($"{name} must be between 2 and {MaxDimension}.");
        return value;
    }

    private static int RequiredInt(JsonElement args, string name, int? defaultValue = null)
    {
        if (!args.TryGetProperty(name, out JsonElement value))
        {
            if (defaultValue.HasValue)
                return defaultValue.Value;
            throw new ArgumentException($"Missing required integer argument: {name}.");
        }
        if (!value.TryGetInt32(out int result))
            throw new ArgumentException($"Argument {name} must be an integer.");
        return result;
    }

    private static int OptionalInt(JsonElement args, string name, int defaultValue)
        => RequiredInt(args, name, defaultValue);

    private static string RequiredString(JsonElement args, string name)
    {
        string value = OptionalString(args, name, null);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing required string argument: {name}.");
        return value;
    }

    private static string OptionalString(JsonElement args, string name, string defaultValue)
    {
        if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return defaultValue;
        return value.GetString() ?? defaultValue;
    }

    private static bool OptionalBool(JsonElement args, string name, bool defaultValue)
    {
        if (!args.TryGetProperty(name, out JsonElement value))
            return defaultValue;
        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            throw new ArgumentException($"Argument {name} must be a boolean.");
        return value.GetBoolean();
    }
}
