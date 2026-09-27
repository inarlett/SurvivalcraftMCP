using System.Collections.Generic;
namespace Game.Mcp;

public readonly record struct BlockDataFieldInfo(string Name, string Mask, int Shift, string Description);
public readonly record struct BlockVariantInfo(int Value, int Data, string DisplayName, IReadOnlyDictionary<string, object> Fields);
public readonly record struct BlockTypeInfo(
    int Index,
    string Name,
    string DisplayName,
    IReadOnlyList<BlockVariantInfo> Variants,
    IReadOnlyList<BlockDataFieldInfo> DataSchema,
    int DefaultCreativeData,
    bool IsCollidable);
