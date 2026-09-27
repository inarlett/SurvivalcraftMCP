using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game.Mcp.Communication;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game;

public sealed class SubsystemMcpBridge : Subsystem, IUpdateable
{
    private GameRpcServer _server;
    private Game.Mcp.TerrainBlockOperator _blocks;
    private readonly Dictionary<string, (Game.Mcp.BlockPosition Origin, Game.Mcp.StructureData Structure)> _snapshots = new();

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    public override void Load(ValuesDictionary valuesDictionary)
    {
        _server = new GameRpcServer(McpModLoader.GetConfiguredPort());
        _server.Start();
        TryInitializeWorldAdapter();
    }

    public override void Dispose()
    {
        _server?.Dispose();
        _server = null;
    }

    public void Update(float dt)
    {
        TryInitializeWorldAdapter();
        while (_server != null && _server.TryDequeue(out var request, out var completion))
        {
            try { completion.TrySetResult(Handle(request)); }
            catch (Exception ex) { completion.TrySetResult(new RpcResponse(request.Id, Error: ex.Message)); }
        }
    }

    private void TryInitializeWorldAdapter()
    {
        if (_blocks != null) return;
        try
        {
            var terrain = Project.FindSubsystem<SubsystemTerrain>();
            if (terrain?.Terrain != null) _blocks = new Game.Mcp.TerrainBlockOperator(terrain);
        }
        catch { }
    }

    private RpcResponse Handle(RpcRequest request)
    {
        if (request.Method == "list_custom_tools")
            return new RpcResponse(request.Id, new { tools = CustomMcpToolRegistry.Describe() });
        if (request.Method == "call_custom_tool")
        {
            string toolName = request.Params.GetProperty("toolName").GetString();
            if (!CustomMcpToolRegistry.TryGet(toolName, out var customTool))
                throw new ArgumentException($"Unknown custom MCP tool: {toolName}");
            JsonElement arguments = request.Params.TryGetProperty("arguments", out var args) ? args : default;
            return new RpcResponse(request.Id, customTool.Handler(arguments));
        }
        if (_blocks == null) return new RpcResponse(request.Id, Error: "world_not_ready");
        switch (request.Method)
        {
            case "list_blocks":
                return new RpcResponse(request.Id, new { blocks = _blocks.ListBlocks() });
            case "capture_screenshot":
            {
                int width = request.Params.TryGetProperty("width", out var w) ? w.GetInt32() : 1920;
                int height = request.Params.TryGetProperty("height", out var h) ? h.GetInt32() : 1080;
                if (width < 320 || height < 180 || width > 3840 || height > 2160 || (long)width * height > 8294400)
                    throw new ArgumentException("Screenshot size must be between 320x180 and 3840x2160 and fit PNG limits.");
                string filename = $"MCP-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
                string mcpDirectory = Storage.CombinePaths(ModsManager.ExternalPath, "SurvivalcraftMCP/ScreenCapture");
                string path = Storage.CombinePaths(mcpDirectory, filename);
                if (!Storage.DirectoryExists(mcpDirectory)) Storage.CreateDirectory(mcpDirectory);
                Vector3? cameraPosition = request.Params.TryGetProperty("position", out var positionElement)
                    ? ReadVector3(positionElement)
                    : null;
                Vector3? cameraTarget = request.Params.TryGetProperty("target", out var targetElement)
                    ? ReadVector3(targetElement)
                    : null;
                if (cameraPosition.HasValue != cameraTarget.HasValue)
                    throw new ArgumentException("position and target must be provided together.");
                byte[] screenshot = CaptureMcpScreenshot(width, height, cameraPosition, cameraTarget);
                using (Stream output = Storage.OpenFile(path, OpenFileMode.Create))
                    output.Write(screenshot, 0, screenshot.Length);
                byte[] bytes;
                using (Stream stream = Storage.OpenFile(path, OpenFileMode.Read))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                }
                return new RpcResponse(request.Id, new
                {
                    path,
                    filename,
                    width,
                    height,
                    mimeType = "image/png",
                    camera = cameraPosition.HasValue ? "temporary" : "current",
                    imageBase64 = Convert.ToBase64String(bytes)
                });
            }
            case "resolve_block":
            {
                var block = ResolveBlockArgument(request.Params);
                return new RpcResponse(request.Id, block);
            }
            case "fill_region_by_block":
            {
                var block = ResolveBlockArgument(request.Params);
                int data = request.Params.TryGetProperty("data", out var d) ? d.GetInt32() : 0;
                int light = request.Params.TryGetProperty("light", out var l) ? l.GetInt32() : 0;
                _blocks.Fill(ReadPosition(request.Params.GetProperty("min")), ReadPosition(request.Params.GetProperty("max")),
                    new Game.Mcp.BlockData(_blocks.MakeBlockValue(block.Index, data, light)));
                return new RpcResponse(request.Id, new { ok = true, block = block.Name, report = _blocks.LastWriteReport });
            }
            case "build_box":
            {
                var block = ResolveBlockArgument(request.Params);
                int data = request.Params.TryGetProperty("data", out var d) ? d.GetInt32() : 0;
                int light = request.Params.TryGetProperty("light", out var l) ? l.GetInt32() : 0;
                bool hollow = request.Params.TryGetProperty("hollow", out var h) && h.GetBoolean();
                int count = _blocks.BuildBox(ReadPosition(request.Params.GetProperty("origin")),
                    request.Params.GetProperty("sizeX").GetInt32(), request.Params.GetProperty("sizeY").GetInt32(),
                    request.Params.GetProperty("sizeZ").GetInt32(),
                    new Game.Mcp.BlockData(_blocks.MakeBlockValue(block.Index, data, light)), hollow);
                return new RpcResponse(request.Id, new { ok = true, block = block.Name, count, hollow, report = _blocks.LastWriteReport });
            }
            case "flatten_region":
            {
                var block = ResolveBlockArgument(request.Params);
                int data = request.Params.TryGetProperty("data", out var d) ? d.GetInt32() : 0;
                int light = request.Params.TryGetProperty("light", out var l) ? l.GetInt32() : 0;
                int targetY = request.Params.GetProperty("targetY").GetInt32();
                int count = _blocks.Flatten(ReadPosition(request.Params.GetProperty("min")), ReadPosition(request.Params.GetProperty("max")),
                    targetY, new Game.Mcp.BlockData(_blocks.MakeBlockValue(block.Index, data, light)));
                return new RpcResponse(request.Id, new { ok = true, block = block.Name, targetY, count, report = _blocks.LastWriteReport });
            }
            case "get_world_info":
            {
                var info = Project.FindSubsystem<SubsystemGameInfo>(true);
                var timeOfDay = Project.FindSubsystem<SubsystemTimeOfDay>(true);
                return new RpcResponse(request.Id, new
                {
                    name = info.WorldSettings?.Name,
                    seed = info.WorldSeed,
                    totalElapsedGameTime = info.TotalElapsedGameTime,
                    timeOfDay = timeOfDay.TimeOfDay,
                    day = timeOfDay.Day,
                    timeOfDayMode = info.WorldSettings?.TimeOfDayMode.ToString()
                });
            }
            case "list_players":
            {
                var players = Project.FindSubsystem<SubsystemPlayers>(true).PlayersData;
                var result = new List<object>();
                foreach (var data in players)
                {
                    var position = data.ComponentPlayer?.ComponentBody?.Position;
                    result.Add(new
                    {
                        index = data.PlayerIndex,
                        name = data.Name,
                        level = data.Level,
                        ready = data.IsReadyForPlaying,
                        position = position.HasValue ? new { x = position.Value.X, y = position.Value.Y, z = position.Value.Z } : null
                    });
                }
                return new RpcResponse(request.Id, new { players = result });
            }
            case "make_block_value":
            {
                int contents = request.Params.GetProperty("contents").GetInt32();
                int data = request.Params.TryGetProperty("data", out var d) ? d.GetInt32() : 0;
                int light = request.Params.TryGetProperty("light", out var l) ? l.GetInt32() : 0;
                return new RpcResponse(request.Id, new { value = _blocks.MakeBlockValue(contents, data, light), contents, data, light });
            }
            case "get_block":
            {
                var p = ReadPosition(request.Params);
                var b = _blocks.GetBlock(p);
                return new RpcResponse(request.Id, new { value = b.Value, contents = b.Contents, data = b.Data, light = b.Light });
            }
            case "set_block":
            {
                var p = ReadPosition(request.Params);
                int value = request.Params.GetProperty("value").GetInt32();
                _blocks.SetBlock(p, new Game.Mcp.BlockData(value));
                return new RpcResponse(request.Id, new { ok = true, report = _blocks.LastWriteReport });
            }
            case "set_blocks":
            {
                var blocks = new List<(Game.Mcp.BlockPosition, Game.Mcp.BlockData)>();
                foreach (var item in request.Params.GetProperty("blocks").EnumerateArray())
                    blocks.Add((ReadPosition(item), new Game.Mcp.BlockData(item.GetProperty("value").GetInt32())));
                _blocks.SetBlocks(blocks);
                return new RpcResponse(request.Id, new { ok = true, count = blocks.Count, report = _blocks.LastWriteReport });
            }
            case "fill_region":
            {
                _blocks.Fill(ReadPosition(request.Params.GetProperty("min")), ReadPosition(request.Params.GetProperty("max")),
                    new Game.Mcp.BlockData(request.Params.GetProperty("value").GetInt32()));
                return new RpcResponse(request.Id, new { ok = true, report = _blocks.LastWriteReport });
            }
            case "get_region":
            {
                var min = ReadPosition(request.Params.GetProperty("min"));
                var max = ReadPosition(request.Params.GetProperty("max"));
                bool sparse = !request.Params.TryGetProperty("sparse", out var sparseElement) || sparseElement.GetBoolean();
                bool nonAirOnly = request.Params.TryGetProperty("nonAirOnly", out var nonAir) ? nonAir.GetBoolean() : sparse;
                int? contents = request.Params.TryGetProperty("contents", out var filter) ? filter.GetInt32() : null;
                bool includeValues = request.Params.TryGetProperty("includeValues", out var include) && include.GetBoolean();
                var cells = _blocks.GetRegionCells(min, max, contents, nonAirOnly);
                return new RpcResponse(request.Id, new
                {
                    min, max,
                    order = "y_then_z_then_x, x changes fastest",
                    sparse,
                    includeValues,
                    values = includeValues ? Array.ConvertAll(_blocks.GetRegion(min, max), v => v.Value) : null,
                    cells
                });
            }
            case "find_blocks":
            {
                var min = ReadPosition(request.Params.GetProperty("min"));
                var max = ReadPosition(request.Params.GetProperty("max"));
                int? contents = request.Params.TryGetProperty("contents", out var filter) ? filter.GetInt32() : null;
                if (!contents.HasValue && request.Params.TryGetProperty("blockName", out var blockName))
                    contents = ResolveBlockValue(blockName).Index;
                bool nonAirOnly = !request.Params.TryGetProperty("nonAirOnly", out var nonAir) || nonAir.GetBoolean();
                int maxResults = request.Params.TryGetProperty("maxResults", out var limit) ? limit.GetInt32() : 4096;
                var cells = _blocks.FindBlocks(min, max, contents, nonAirOnly, maxResults);
                return new RpcResponse(request.Id, new { min, max, contents, truncated = cells.Count >= maxResults, cells });
            }
            case "check_collision":
            {
                Vector3 min = ReadVector3(request.Params.GetProperty("min"));
                Vector3 max = ReadVector3(request.Params.GetProperty("max"));
                int maxResults = request.Params.TryGetProperty("maxResults", out var limit) ? limit.GetInt32() : 128;
                var hits = _blocks.CheckCollision(min, max, maxResults);
                return new RpcResponse(request.Id, new { min, max, maxResults, truncated = hits.Count >= maxResults, colliding = hits.Count > 0, hits });
            }
            case "check_walkable":
            {
                Vector3 position = ReadVector3(request.Params.GetProperty("position"));
                float width = request.Params.TryGetProperty("width", out var widthElement) ? widthElement.GetSingle() : 0.6f;
                float height = request.Params.TryGetProperty("height", out var heightElement) ? heightElement.GetSingle() : 1.8f;
                float depth = request.Params.TryGetProperty("depth", out var depthElement) ? depthElement.GetSingle() : 0.6f;
                int maxResults = request.Params.TryGetProperty("maxResults", out var limit) ? limit.GetInt32() : 128;
                if (width <= 0f || height <= 0f || depth <= 0f) throw new ArgumentException("Player dimensions must be positive.");
                Vector3 min = position + new Vector3(-width / 2f, 0f, -depth / 2f);
                Vector3 max = position + new Vector3(width / 2f, height, depth / 2f);
                var hits = _blocks.CheckCollision(min, max, maxResults);
                return new RpcResponse(request.Id, new { position, width, height, depth, walkable = hits.Count == 0, min, max, hits });
            }
            case "get_surface_heights":
            {
                var cells = _blocks.GetSurfaceHeights(ReadPosition(request.Params.GetProperty("min")), ReadPosition(request.Params.GetProperty("max")));
                return new RpcResponse(request.Id, new { cells });
            }
            case "build_wall":
            case "build_cylinder":
            case "build_staircase":
            {
                var block = ResolveBlockArgument(request.Params);
                int data = request.Params.TryGetProperty("data", out var d) ? d.GetInt32() : 0;
                int light = request.Params.TryGetProperty("light", out var l) ? l.GetInt32() : 0;
                var value = new Game.Mcp.BlockData(_blocks.MakeBlockValue(block.Index, data, light));
                int count = request.Method switch
                {
                    "build_wall" => _blocks.BuildWall(ReadPosition(request.Params.GetProperty("origin")), request.Params.GetProperty("width").GetInt32(), request.Params.GetProperty("height").GetInt32(), request.Params.GetProperty("thickness").GetInt32(), request.Params.GetProperty("axis").GetString(), value),
                    "build_cylinder" => _blocks.BuildCylinder(ReadPosition(request.Params.GetProperty("origin")), request.Params.GetProperty("radius").GetInt32(), request.Params.GetProperty("height").GetInt32(), value, request.Params.TryGetProperty("hollow", out var h) && h.GetBoolean()),
                    _ => _blocks.BuildStaircase(ReadPosition(request.Params.GetProperty("origin")), request.Params.GetProperty("steps").GetInt32(), request.Params.GetProperty("width").GetInt32(), request.Params.GetProperty("axis").GetString(), request.Params.GetProperty("direction").GetInt32(), value)
                };
                return new RpcResponse(request.Id, new { ok = true, block = block.Name, count, report = _blocks.LastWriteReport });
            }
            case "replace_blocks":
            {
                var from = _blocks.ResolveBlock(request.Params.GetProperty("fromBlockName").GetString());
                var to = _blocks.ResolveBlock(request.Params.GetProperty("toBlockName").GetString());
                var min = ReadPosition(request.Params.GetProperty("min"));
                var max = ReadPosition(request.Params.GetProperty("max"));
                bool preserveData = !request.Params.TryGetProperty("preserveData", out var preserve) || preserve.GetBoolean();
                bool dryRun = request.Params.TryGetProperty("dryRun", out var dry) && dry.GetBoolean();
                int count;
                if (dryRun)
                    count = _blocks.FindBlocks(min, max, from.Index, false, 65536).Count;
                else
                    count = _blocks.ReplaceContents(min, max, from.Index, to.Index, preserveData);
                return new RpcResponse(request.Id, new { ok = true, dryRun, count, from = from.Name, to = to.Name, report = dryRun ? null : _blocks.LastWriteReport });
            }
            case "snapshot_region":
            {
                string snapshotId = request.Params.TryGetProperty("snapshotId", out var sid)
                    ? sid.GetString()
                    : Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(snapshotId)) throw new ArgumentException("snapshotId is required.");
                var min = ReadPosition(request.Params.GetProperty("min"));
                var max = ReadPosition(request.Params.GetProperty("max"));
                _snapshots[snapshotId] = (min, _blocks.CaptureStructure(min, max));
                return new RpcResponse(request.Id, new { ok = true, snapshotId, min, max, cells = _snapshots[snapshotId].Structure.Volume });
            }
            case "list_snapshots":
                return new RpcResponse(request.Id, new { snapshots = _snapshots.Select(pair => new { snapshotId = pair.Key, origin = pair.Value.Origin, sizeX = pair.Value.Structure.SizeX, sizeY = pair.Value.Structure.SizeY, sizeZ = pair.Value.Structure.SizeZ }) });
            case "delete_snapshot":
            {
                string snapshotId = request.Params.GetProperty("snapshotId").GetString();
                return new RpcResponse(request.Id, new { ok = _snapshots.Remove(snapshotId), snapshotId });
            }
            case "restore_snapshot":
            {
                string snapshotId = request.Params.GetProperty("snapshotId").GetString();
                if (!_snapshots.TryGetValue(snapshotId, out var snapshot)) throw new ArgumentException($"Unknown snapshot: {snapshotId}");
                var origin = request.Params.TryGetProperty("origin", out var originElement) ? ReadPosition(originElement) : snapshot.Origin;
                bool dryRun = request.Params.TryGetProperty("dryRun", out var dry) && dry.GetBoolean();
                if (!dryRun) _blocks.PlaceStructure(origin, snapshot.Structure, 0, false, false);
                return new RpcResponse(request.Id, new { ok = true, dryRun, snapshotId, origin, cells = snapshot.Structure.Volume, report = dryRun ? null : _blocks.LastWriteReport });
            }
            case "capture_structure":
            {
                var structure = _blocks.CaptureStructure(ReadPosition(request.Params.GetProperty("min")), ReadPosition(request.Params.GetProperty("max")));
                return new RpcResponse(request.Id, new { sizeX = structure.SizeX, sizeY = structure.SizeY, sizeZ = structure.SizeZ, values = structure.Values });
            }
            case "place_structure":
            {
                var sizeX = request.Params.GetProperty("sizeX").GetInt32();
                var sizeY = request.Params.GetProperty("sizeY").GetInt32();
                var sizeZ = request.Params.GetProperty("sizeZ").GetInt32();
                var values = new int[sizeX * sizeY * sizeZ];
                int i = 0;
                foreach (var value in request.Params.GetProperty("values").EnumerateArray()) values[i++] = value.GetInt32();
                var structure = new Game.Mcp.StructureData(sizeX, sizeY, sizeZ, values);
                int rotation = request.Params.TryGetProperty("rotationY", out var r) ? r.GetInt32() : 0;
                bool mirror = request.Params.TryGetProperty("mirrorX", out var m) && m.GetBoolean();
                bool skipAir = !request.Params.TryGetProperty("skipAir", out var a) || a.GetBoolean();
                _blocks.PlaceStructure(ReadPosition(request.Params.GetProperty("origin")), structure, rotation, mirror, skipAir);
                return new RpcResponse(request.Id, new { ok = true, sizeX, sizeY, sizeZ, report = _blocks.LastWriteReport });
            }
            case "undo_last":
            {
                bool undone = _blocks.UndoLast();
                return new RpcResponse(request.Id, new { ok = true, undone, report = _blocks.LastWriteReport });
            }
            default: return new RpcResponse(request.Id, Error: $"unknown_method:{request.Method}");
        }
    }

    private static Game.Mcp.BlockPosition ReadPosition(JsonElement e) =>
        new(e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(), e.GetProperty("z").GetInt32());

    private static byte[] CaptureMcpScreenshot(int width, int height, Vector3? requestedPosition, Vector3? requestedTarget)
    {
        using var renderTarget = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
        RenderTarget2D previousTarget = Display.RenderTarget;
        var hiddenControls = new Dictionary<ComponentGui, bool>();
        var replacedCameras = new List<(GameWidget Widget, Camera Camera)>();
        ResolutionMode previousResolution = SettingsManager.ResolutionMode;
        try
        {
            foreach (ComponentPlayer player in GameManager.Project.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers)
            {
                if (player.ComponentGui == null) continue;
                hiddenControls[player.ComponentGui] = player.ComponentGui.ControlsContainerWidget.IsVisible;
                player.ComponentGui.ControlsContainerWidget.IsVisible = false;
            }

            SubsystemGameWidgets gameWidgets = GameManager.Project.FindSubsystem<SubsystemGameWidgets>(true);
            if (gameWidgets.GameWidgets.Count == 0)
                throw new InvalidOperationException("No game view is available for screenshot capture.");
            Vector3 position = requestedPosition ?? gameWidgets.GameWidgets[0].ActiveCamera.ViewPosition;
            Vector3 target = requestedTarget ?? (position + gameWidgets.GameWidgets[0].ActiveCamera.ViewDirection);
            Vector3 direction = target - position;
            if (direction.LengthSquared() < 0.0001f)
                throw new ArgumentException("Screenshot camera position and target must differ.");
            Vector3 normalizedDirection = Vector3.Normalize(direction);
            Vector3 up = MathF.Abs(Vector3.Dot(normalizedDirection, Vector3.UnitY)) > 0.99f
                ? Vector3.UnitZ
                : Vector3.UnitY;
            foreach (GameWidget widget in gameWidgets.GameWidgets)
            {
                Camera previousCamera = widget.ActiveCamera;
                var camera = new McpCamera(widget);
                camera.SetupPerspectiveCamera(position, direction, up);
                replacedCameras.Add((widget, previousCamera));
                widget.ActiveCamera = camera;
            }

            SettingsManager.ResolutionMode = ResolutionMode.High;
            Display.RenderTarget = renderTarget;
            ScreensManager.Draw();

            using var output = new MemoryStream();
            Image.Save(
                renderTarget.GetData(new Rectangle(0, 0, renderTarget.Width, renderTarget.Height)),
                output,
                ImageFileFormat.Png,
                false
            );
            return output.ToArray();
        }
        finally
        {
            Display.RenderTarget = previousTarget;
            SettingsManager.ResolutionMode = previousResolution;
            for (int i = replacedCameras.Count - 1; i >= 0; i--)
                replacedCameras[i].Widget.ActiveCamera = replacedCameras[i].Camera;
            foreach (var item in hiddenControls)
                item.Key.ControlsContainerWidget.IsVisible = item.Value;
        }
    }

    private Game.Mcp.BlockTypeInfo ResolveBlockArgument(JsonElement parameters, string propertyName = "blockName")
    {
        JsonElement value = parameters.TryGetProperty(propertyName, out var named) ? named : parameters;
        return ResolveBlockValue(value);
    }

    private Game.Mcp.BlockTypeInfo ResolveBlockValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => _blocks.ResolveBlock(value.GetString()),
            JsonValueKind.Number when value.TryGetInt32(out int contents) => _blocks.ResolveBlock(contents),
            JsonValueKind.Object when value.TryGetProperty("name", out var name) => ResolveBlockValue(name),
            JsonValueKind.Object when value.TryGetProperty("contents", out var contents) => ResolveBlockValue(contents),
            _ => throw new ArgumentException("Block reference must be a name string, contents number, or {name}/{contents} object.")
        };
    }

    private static Vector3 ReadVector3(JsonElement e) =>
        new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());

    private sealed class McpCamera : BasePerspectiveCamera
    {
        public McpCamera(GameWidget gameWidget) : base(gameWidget) { }
        public override bool UsesMovementControls => false;
        public override bool IsEntityControlEnabled => false;
        public override void Activate(Camera previousCamera) { }
        public override void Update(float dt) { }
    }
}
