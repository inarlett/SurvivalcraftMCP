using System.Text.Json;
using SurvivalcraftMCP.Server.Communication;

int port = 28899;
for (int i = 0; i + 1 < args.Length; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out int parsedPort) && parsedPort is >= 1024 and <= 65535)
    {
        port = parsedPort;
        i++;
    }
}

var game = new GameClient(port: port);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

while (Console.ReadLine() is { } line)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonElement id = default;
    try
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        string method = root.GetProperty("method").GetString() ?? "";
        bool hasId = root.TryGetProperty("id", out var idElement);
        if (hasId) id = idElement.Clone();
        if (!hasId)
        {
            // Notifications, including notifications/initialized, do not receive a response.
            continue;
        }

        object response = method switch
        {
            "initialize" => new { jsonrpc = "2.0", id, result = new { protocolVersion = "2024-11-05", capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "survivalcraft-mcp", version = "0.2.0" } } },
            "ping" => new { jsonrpc = "2.0", id, result = new { } },
            "tools/list" => await ToolsListAsync(id, game),
            "tools/call" => await ToolCallAsync(id, root.GetProperty("params"), game),
            _ => Error(id, -32601, $"Method not found: {method}")
        };
        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
    }
    catch (Exception ex)
    {
        if (id.ValueKind != JsonValueKind.Undefined)
            Console.WriteLine(JsonSerializer.Serialize(Error(id, -32603, ex.Message), jsonOptions));
    }
}

static async Task<object> ToolsListAsync(JsonElement id, GameClient game)
{
    object builtInResponse = ToolsList(id);
    try
    {
        JsonElement response = JsonSerializer.SerializeToElement(builtInResponse);
        var tools = new List<JsonElement>();
        foreach (JsonElement tool in response.GetProperty("result").GetProperty("tools").EnumerateArray())
            tools.Add(tool.Clone());

        using var discoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        JsonElement customResponse = await game.CallAsync("list_custom_tools", new { }, discoveryTimeout.Token);
        if (customResponse.TryGetProperty("tools", out JsonElement customTools)
            && customTools.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement tool in customTools.EnumerateArray())
            {
                if (tool.TryGetProperty("name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && !tools.Exists(existing => existing.TryGetProperty("name", out JsonElement existingName)
                        && existingName.GetString() == name.GetString()))
                    tools.Add(tool.Clone());
            }
        }

        return new { jsonrpc = "2.0", id, result = new { tools } };
    }
    catch
    {
        // The server may start before the game. Keep built-in tools available.
        return builtInResponse;
    }
}

static object ToolsList(JsonElement id) => new
{
    jsonrpc = "2.0", id,
    result = new { tools = new object[]
    {
        new { name="get_usage_guide", description="Read the built-in Survivalcraft MCP tool usage guide. Call this before the first world edit, especially when a player names a block in natural language. Topics: general, blocks, query, building, safety.", inputSchema=new { type="object", properties=new { topic=new {type="string", @enum=new[]{"general","blocks","query","building","safety"}} } } },
        PosTool("get_block", "Read one terrain cell. Returns raw value and decoded contents/data/light.", false),
        new { name="capture_screenshot", description="Render an MCP-only PNG without the vanilla screenshot logo. By default it uses the current view; provide position and target together to render from a temporary camera without moving the player. Returned image can be read by vision-capable models.", inputSchema=new { type="object", properties=new { width=new {type="integer", minimum=320, maximum=3840}, height=new {type="integer", minimum=180, maximum=2160}, position=FloatPosSchema(), target=FloatPosSchema()} } },
        new { name="list_blocks", description="List loaded Survivalcraft blocks with contents IDs, runtime type names and display names. Use this first when a player names a block in natural language; never guess a numeric contents ID from the name.", inputSchema=new { type="object", properties=new { } } },
        new { name="resolve_block", description="Resolve blockName as a runtime type name, display name, numeric contents, or {name}/{contents}. Returns the canonical contents ID, creative variants with ready-to-use raw values and data, and dataSchema fields for doors, stairs, slabs, furniture, trapdoors, torches, carpets and colors. Use a returned variant data/value instead of inventing data bits.", inputSchema=new { type="object", properties=new { blockName=BlockRefSchema()}, required=new[]{"blockName"} } },
        new { name="get_world_info", description="Read the loaded world name, seed, elapsed time and time of day.", inputSchema=new { type="object", properties=new { } } },
        new { name="list_players", description="List players currently known to the loaded world and their positions when spawned.", inputSchema=new { type="object", properties=new { } } },
        new { name="make_block_value", description="Build a raw cell value from contents (block ID), data and light.", inputSchema=new { type="object", properties=new { contents=new {type="integer"}, data=new {type="integer"}, light=new {type="integer"}}, required=new[]{"contents"} } },
        PosValueTool("set_block", "Set one terrain cell using a raw Survivalcraft cell value. Response includes a write report."),
        new { name="set_blocks", description="Set up to 65536 cells in one game-thread operation. Response includes a write report.", inputSchema=new { type="object", properties=new { blocks=new { type="array", items=new { type="object", properties=new { x=new {type="integer"}, y=new {type="integer"}, z=new {type="integer"}, value=new {type="integer"}}, required=new[]{"x","y","z","value"} } } }, required=new[]{"blocks"} } },
        RegionValueTool("fill_region", "Fill an inclusive region with one raw cell value. Response includes a write report."),
        new { name="fill_region_by_block", description="Fill an inclusive region using a resolved block reference. Prefer snapshot_region before large edits; response includes overwrittenNonAir, disappeared and undoAvailable.", inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema(), blockName=BlockRefSchema(), data=new {type="integer"}, light=new {type="integer"}}, required=new[]{"min","max","blockName"} } },
        new { name="build_box", description="Build an axis-aligned box from origin with inclusive sizeX/sizeY/sizeZ dimensions. hollow=true skips only strictly interior cells, so bottom and top faces are still filled; use build_box plus a separate air fill for an open floor plan. Response includes a write report.", inputSchema=new { type="object", properties=new { origin=PosSchema(), sizeX=new {type="integer"}, sizeY=new {type="integer"}, sizeZ=new {type="integer"}, blockName=BlockRefSchema(), data=new {type="integer"}, light=new {type="integer"}, hollow=new {type="boolean"}}, required=new[]{"origin","sizeX","sizeY","sizeZ","blockName"} } },
        new { name="flatten_region", description="For an inclusive cell region, fill cells below targetY and clear targetY and above. y is a terrain cell index, not the player's foot height. Snapshot first if existing terrain matters; response includes a write report.", inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema(), targetY=new {type="integer"}, blockName=BlockRefSchema(), data=new {type="integer"}, light=new {type="integer"}}, required=new[]{"min","max","targetY","blockName"} } },
        new { name="get_region", description="Read an inclusive region as coordinate cells. Default is sparse non-air cells and no values array. Set nonAirOnly=false to include air, contents to filter, and includeValues=true to also receive the flat values array. Flat order is y then z then x, with x changing fastest. Coordinates use terrain cell indices.", inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema(), sparse=new {type="boolean"}, nonAirOnly=new {type="boolean"}, contents=new {type="integer"}, includeValues=new {type="boolean"}}, required=new[]{"min","max"} } },
        new { name="find_blocks", description="Find matching block coordinates in a region, optionally by blockName or contents.", inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema(), blockName=new {type="string"}, contents=new {type="integer"}, nonAirOnly=new {type="boolean"}, maxResults=new {type="integer"}}, required=new[]{"min","max"} } },
        new { name="get_surface_heights", description="Return the highest non-air block in each X/Z column. y is a terrain cell index.", inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema() }, required=new[]{"min","max"} } },
        new { name="check_collision", description="Check whether a floating-point world-space AABB intersects custom collision boxes. Returns hit cells and world-space boxes.", inputSchema=new { type="object", properties=new { min=FloatPosSchema(), max=FloatPosSchema(), maxResults=new {type="integer", minimum=1, maximum=65536}}, required=new[]{"min","max"} } },
        new { name="check_walkable", description="Check a player-sized world-space body at a foot position. Default size is width/depth 0.6 and height 1.8; position.y is the player's foot height, unlike a terrain cell y index.", inputSchema=new { type="object", properties=new { position=FloatPosSchema(), width=new {type="number"}, height=new {type="number"}, depth=new {type="number"}, maxResults=new {type="integer", minimum=1, maximum=65536}}, required=new[]{"position"} } },
        new { name="build_wall", description="Build a regular wall along axis x or z. Response includes a write report.", inputSchema=new { type="object", properties=new { origin=PosSchema(), width=new {type="integer"}, height=new {type="integer"}, thickness=new {type="integer"}, axis=new {type="string", @enum=new[]{"x","z"}}, blockName=BlockRefSchema(), data=new {type="integer"}, light=new {type="integer"}}, required=new[]{"origin","width","height","thickness","axis","blockName"} } },
        new { name="build_cylinder", description="Build a vertical solid or hollow cylinder. Response includes a write report.", inputSchema=new { type="object", properties=new { origin=PosSchema(), radius=new {type="integer"}, height=new {type="integer"}, blockName=BlockRefSchema(), data=new {type="integer"}, light=new {type="integer"}, hollow=new {type="boolean"}}, required=new[]{"origin","radius","height","blockName"} } },
        new { name="build_staircase", description="Build a solid staircase rising along axis x or z; direction is 1 or -1. Response includes a write report.", inputSchema=new { type="object", properties=new { origin=PosSchema(), steps=new {type="integer"}, width=new {type="integer"}, axis=new {type="string", @enum=new[]{"x","z"}}, direction=new {type="integer", @enum=new[]{-1,1}}, blockName=BlockRefSchema(), data=new {type="integer"}, light=new {type="integer"}}, required=new[]{"origin","steps","width","axis","direction","blockName"} } },
        new { name="replace_blocks", description="Replace one block reference with another in a region; supports preserveData and dryRun. A real write returns a report.", inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema(), fromBlockName=BlockRefSchema(), toBlockName=BlockRefSchema(), preserveData=new {type="boolean"}, dryRun=new {type="boolean"}}, required=new[]{"min","max","fromBlockName","toBlockName"} } },
        new { name="snapshot_region", description="Capture a named in-memory snapshot for later restore.", inputSchema=new { type="object", properties=new { snapshotId=new {type="string"}, min=PosSchema(), max=PosSchema()}, required=new[]{"min","max"} } },
        new { name="list_snapshots", description="List in-memory snapshots held by the game bridge.", inputSchema=new { type="object", properties=new { } } },
        new { name="restore_snapshot", description="Restore a snapshot at its original or a new origin; supports dryRun and returns a write report when applied.", inputSchema=new { type="object", properties=new { snapshotId=new {type="string"}, origin=PosSchema(), dryRun=new {type="boolean"}}, required=new[]{"snapshotId"} } },
        new { name="delete_snapshot", description="Delete an in-memory snapshot.", inputSchema=new { type="object", properties=new { snapshotId=new {type="string"}}, required=new[]{"snapshotId"} } },
        new { name="list_custom_tools", description="List tools registered by other Survivalcraft mods.", inputSchema=new { type="object", properties=new { } } },
        new { name="invoke_custom_tool", description="Invoke a tool registered by another Survivalcraft mod. Call list_custom_tools first to inspect schemas.", inputSchema=new { type="object", properties=new { toolName=new {type="string"}, arguments=new {type="object"}}, required=new[]{"toolName"} } },
        RegionTool("capture_structure", "Capture an inclusive region into an MCP structure."),
        new { name="place_structure", description="Place a captured/raw structure. rotationY is 0/90/180/270; mirrorX mirrors before rotation; skipAir defaults true. Response includes a write report.", inputSchema=new { type="object", properties=new { origin=PosSchema(), sizeX=new {type="integer"}, sizeY=new {type="integer"}, sizeZ=new {type="integer"}, values=new {type="array", items=new {type="integer"}}, rotationY=new {type="integer"}, mirrorX=new {type="boolean"}, skipAir=new {type="boolean"}}, required=new[]{"origin","sizeX","sizeY","sizeZ","values"} } },
        new { name="undo_last", description="Undo the most recent terrain write operation. The bridge keeps up to 16 write operations in memory and returns another report.", inputSchema=new { type="object", properties=new { } } }
    }}
};

static object PosSchema() => new { type="object", properties=new { x=new {type="integer"}, y=new {type="integer"}, z=new {type="integer"}}, required=new[]{"x","y","z"} };
static object FloatPosSchema() => new { type="object", properties=new { x=new {type="number"}, y=new {type="number"}, z=new {type="number"}}, required=new[]{"x","y","z"} };
static object BlockRefSchema() => new { oneOf = new object[]
{
    new { type="string" },
    new { type="integer" },
    new { type="object", properties=new { name=new {type="string"}, contents=new {type="integer"}} }
} };
static object PosTool(string name,string desc,bool value) => new { name, description=desc, inputSchema=new { type="object", properties=new { x=new {type="integer"}, y=new {type="integer"}, z=new {type="integer"}}, required=new[]{"x","y","z"} } };
static object PosValueTool(string name,string desc) => new { name, description=desc, inputSchema=new { type="object", properties=new { x=new {type="integer"}, y=new {type="integer"}, z=new {type="integer"}, value=new {type="integer"}}, required=new[]{"x","y","z","value"} } };
static object RegionTool(string name,string desc) => new { name, description=desc, inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema() }, required=new[]{"min","max"} } };
static object RegionValueTool(string name,string desc) => new { name, description=desc, inputSchema=new { type="object", properties=new { min=PosSchema(), max=PosSchema(), value=new {type="integer"} }, required=new[]{"min","max","value"} } };

static async Task<object> ToolCallAsync(JsonElement id, JsonElement parameters, GameClient game)
{
    string name = parameters.GetProperty("name").GetString() ?? "";
    JsonElement args = parameters.TryGetProperty("arguments", out var a) ? a : default;
    try
    {
        if (name == "get_usage_guide")
        {
            string topic = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("topic", out var topicElement)
                ? topicElement.GetString() ?? "general"
                : "general";
            string guide = McpToolGuide.Get(topic);
            return new { jsonrpc="2.0", id, result=new { content=new[]{new {type="text", text=guide}}, structuredContent=new { topic, guide } } };
        }

        JsonElement result = name switch
        {
            "get_block" or "capture_screenshot" or "list_blocks" or "resolve_block" or "get_world_info" or "list_players" or "make_block_value" or "set_block" or "set_blocks" or "fill_region" or "fill_region_by_block" or "build_box" or "flatten_region" or "get_region" or "find_blocks" or "get_surface_heights" or "check_collision" or "check_walkable" or "build_wall" or "build_cylinder" or "build_staircase" or "replace_blocks" or "snapshot_region" or "list_snapshots" or "restore_snapshot" or "delete_snapshot" or "capture_structure" or "place_structure" or "undo_last" or "list_custom_tools"
                => await game.CallAsync(name, args, CancellationToken.None),
            "invoke_custom_tool" => await game.CallAsync("call_custom_tool", new { toolName = args.GetProperty("toolName").GetString(), arguments = args.TryGetProperty("arguments", out var customArgs) ? customArgs : default }, CancellationToken.None),
            _ => await game.CallAsync("call_custom_tool", new
            {
                toolName = name,
                arguments = args
            }, CancellationToken.None)
        };
        if (name == "capture_screenshot" && result.TryGetProperty("imageBase64", out var image))
        {
            string path = result.TryGetProperty("path", out var pathElement) ? pathElement.GetString() ?? "" : "";
            var content = new object[]
            {
                new { type = "text", text = $"Screenshot captured: {path}" },
                new { type = "image", data = image.GetString(), mimeType = "image/png" }
            };
            return new { jsonrpc="2.0", id, result=new { content, structuredContent=new { path, width=result.GetProperty("width").GetInt32(), height=result.GetProperty("height").GetInt32() } } };
        }
        return new { jsonrpc="2.0", id, result=new { content=new[]{new {type="text", text=result.GetRawText()}}, structuredContent=result } };
    }
    catch(Exception ex) { return new { jsonrpc="2.0", id, result=new { isError=true, content=new[]{new {type="text", text=ex.Message}} } }; }
}

static object Error(JsonElement id, int code, string message) => new { jsonrpc="2.0", id, error=new {code,message} };
