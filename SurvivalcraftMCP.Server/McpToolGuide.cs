internal static class McpToolGuide
{
    public static string Get(string topic) => topic.ToLowerInvariant() switch
    {
        "blocks" => Blocks,
        "query" => Query,
        "building" => Building,
        "safety" => Safety,
        _ => General
    };

    private const string General = """
Survivalcraft MCP tool guide

Recommended workflow:
1. Call list_blocks when the player uses a natural-language block name. Match its displayName or name; do not guess contents IDs.
2. Call resolve_block with the chosen name. Use the returned variants[].value or variants[].data for doors, stairs, furniture, colors and other block states.
3. Read the target with get_region, find_blocks or get_surface_heights. Terrain x/y/z are integer cell indices and both min/max are inclusive.
4. Before a large edit, call snapshot_region. For uncertain layouts, call capture_screenshot with a temporary position and target.
5. Prefer high-level builders (build_box, build_wall, build_cylinder, build_staircase). Check the write report, then inspect with get_region or capture_screenshot.
6. Use undo_last for the most recent write, or restore_snapshot for a named rollback.

The game does not provide an AI chat window. Tool calls go through the external MCP client to the loaded Bridge mod.
""";

    private const string Blocks = """
Block lookup and variants

list_blocks returns loaded blocks with index (the contents ID), name (runtime type name) and displayName. It is the directory for names such as stone, wood, door, furniture or localized player terms.

resolve_block accepts any of:
- "Stone" or a localized display name
- a runtime type name such as StoneBlock
- a numeric contents ID
- {"name":"Stone"} or {"contents":1}

Its result includes:
- index: the block contents ID
- variants: creative states, each with value, data, displayName and decoded fields
- dataSchema: the meaning of data fields when the block exposes them
- defaultCreativeData and isCollidable

For a placed state, prefer a matching variants[].value with set_block/place_structure, or pass its variants[].data to a high-level builder. Do not invent bit masks for door facing, stair orientation, slab top/bottom, torch face, carpet color or furniture design.

If resolve_block fails, call list_blocks and choose from the returned displayName/name values. A block reference in high-level tools may be a string, integer contents, or {name}/{contents}.
""";

    private const string Query = """
Query and coordinate rules

get_block reads one terrain cell and returns value, contents, data and light.
get_region uses inclusive min/max. By default it returns sparse non-air cells as cells[{x,y,z,value,contents,data,light}]. Set nonAirOnly=false to include air, contents=N to filter by contents, and includeValues=true only when a flat array is required. Flat values order is y -> z -> x, with x changing fastest.
find_blocks returns matching coordinates. Resolve the player's block name first, then pass blockName or contents. Use maxResults and check truncated.
get_surface_heights returns the highest non-air cell in every X/Z column.
check_collision accepts a floating-point world-space AABB. check_walkable accepts a player's floating-point foot position; default body size is 0.6 x 1.8 x 0.6. A player's y is foot height, not a terrain cell index.

Use list_players for current player positions. Convert a player foot position to a nearby terrain-cell query deliberately; do not mix float player coordinates with cell y values without checking the intended height.
""";

    private const string Building = """
Building and terrain repair

build_box dimensions are inclusive by size count from origin. hollow=true removes only strictly interior cells; it still writes the bottom and top faces. For a room with an open floor, build the shell and then clear the interior explicitly.
build_wall builds along axis x or z and accepts width, height and thickness.
build_cylinder builds a vertical solid or hollow cylinder.
build_staircase builds a solid staircase rising along axis x or z; direction is 1 or -1.
flatten_region fills cells below targetY and clears targetY and above. This is destructive terrain editing.
replace_blocks supports preserveData and dryRun. dryRun counts candidates without writing.
capture_structure/place_structure are useful for copying a checked structure; rotationY is 0/90/180/270 and skipAir defaults to true.

All write responses include a report with changed, overwrittenNonAir, disappeared and undoAvailable. A requested non-air block in disappeared means the game removed it after placement, often due to block behavior or neighbor validation.
""";

    private const string Safety = """
Safe edit checklist

For edits that can overwrite existing work:
1. snapshot_region the exact area.
2. Use get_region/find_blocks/get_surface_heights to establish the current state.
3. Use replace_blocks dryRun or a small test area when the operation is uncertain.
4. Apply the edit, then inspect report.disappeared and report.overwrittenNonAir.
5. Verify with get_region, check_walkable and capture_screenshot from an outside temporary camera.
6. Call undo_last immediately for the last bad write, or restore_snapshot for the named area.

The bridge limits one generated/write operation to 65536 cells. Snapshots and undo history are in memory and are lost when the game process exits. World edits run on the game thread and may briefly reduce frame rate.
""";
}
