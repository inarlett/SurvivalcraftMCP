# Survivalcraft MCP Bridge 0.2

目标：让 Codex / Claude 等 MCP 客户端可以直接读写 Survivalcraft 世界，并逐步提供“搭建结构”的高层接口。

完整工具使用文档见 [`MCP-TOOLS.md`](MCP-TOOLS.md)。Server 还提供 `get_usage_guide` 工具，AI 可以直接读取内置规则，不需要搜索游戏源码。

## 架构

```text
MCP Client
   │ stdio / MCP
   ▼
SurvivalcraftMCP.Server
   │ localhost TCP :28899
   ▼
SubsystemMcpBridge (game main/update thread)
   ▼
TerrainBlockOperator
   ▼
Survivalcraft Terrain / BlocksManager
```

## 当前工具

- `list_blocks`：列出当前加载的 block ID 与类型名。
- `get_usage_guide`：读取内置工具使用规则；支持 `general`、`blocks`、`query`、`building`、`safety` 主题。
- `resolve_block`：按类型名、显示名称、contents 数字或 `{name}`/`{contents}` 查询方块，并返回 creative variants 和 dataSchema。
- `get_world_info`：读取当前世界名称、种子、游戏时间和昼夜时间。
- `list_players`：读取当前世界玩家名称、等级、准备状态和已生成玩家的位置。
- `make_block_value`：由 `contents/data/light` 生成 raw cell value。
- `get_block`：读取一个方块，同时返回 raw value、contents、data、light。
- `capture_screenshot`：由 MCP 客户端模组独立渲染当前画面，不调用原版截图接口、不绘制原版 logo，并向支持视觉输入的 Agent 返回 PNG 图片。
- `set_block`：修改一个方块。
- `set_blocks`：一次最多 65536 个离散方块。
- `fill_region`：填充一个包含两端点的长方体区域。
- `fill_region_by_block`：按方块名称填充区域，不需要手动计算 raw value。
- `build_box`：按方块名称创建实心或空心长方体建筑。
- `flatten_region`：把区域整平到指定高度，下面填充指定方块，上面清空。
- `find_blocks`：按方块 ID 或名称查找区域内坐标。
- `get_surface_heights`：一次读取区域内每个 X/Z 柱的最高非空气方块。
- `build_wall`、`build_cylinder`、`build_staircase`：生成规则墙体、圆柱和楼梯，避免 AI 逐格传坐标。
- `replace_blocks`：按名称批量替换区域内方块，支持保留 data 和 `dryRun`。
- `snapshot_region`、`restore_snapshot`、`list_snapshots`、`delete_snapshot`：创建、恢复和管理内存快照。
- `list_custom_tools`、`invoke_custom_tool`：发现并调用其他模组注册的 MCP 工具。
- `get_region`：读取区域，最多 65536 格。
- `capture_structure`：把区域保存成 MCP 自己的结构数据。
- `place_structure`：放置结构，支持 Y 轴 0/90/180/270 度旋转、X 镜像和跳过空气。
- `check_collision`、`check_walkable`：使用游戏方块的实际自定义碰撞盒检查空间和玩家尺寸通行性。
- `undo_last`：撤销最近一次地形写入，最多保留 16 次操作。

连接到游戏后，第三方模组注册的工具也会被自动合并到标准 MCP `tools/list` 响应中。客户端通常可以直接看到并调用这些工具；`list_custom_tools` 和 `invoke_custom_tool` 仍保留用于兼容旧客户端。

## 结构数据

结构故意不绑定 `.scb`、`.blueprint` 或其他游戏文件格式：

```text
Structure
├── sizeX
├── sizeY
├── sizeZ
└── values[]       # Y -> Z -> X
```

这样以后可以在 MCP 层增加压缩、palette、结构文件导入导出，而不会污染游戏侧 Terrain API。

`get_region` 默认返回稀疏的 `cells`，每项包含 `x/y/z/value/contents/data/light`，默认只包含非空气；使用 `nonAirOnly: false` 可包含空气，使用 `contents` 可按 contents 过滤。只有显式 `includeValues: true` 才返回兼容用的一维 `values`，其顺序是 `y -> z -> x`，其中 `x` 变化最快。

坐标约定：区域 `x/y/z` 和 `min/max` 是包含边界的地形格索引；玩家/碰撞工具的浮点 `position.y` 是脚底高度，不等于格子 y。`flatten_region` 会填充 `targetY` 以下并清空 `targetY` 及以上；`build_box` 的 `hollow=true` 只跳过严格内部，底面和顶面仍会填充。所有规则生成单次最多写入 65536 格。

方块面 `face` 使用游戏 API 的固定映射：`0=+Z`、`1=+X`、`2=-Z`、`3=-X`、`4=+Y(上)`、`5=-Y(下)`；相对面分别为 `(2,3,0,1,5,4)`。

所有写入响应都包含 `report`：`requested`、`changed`、`overwrittenNonAir`、`disappeared`、最多 64 个 `overwrittenSamples`/`disappearedSamples`，以及 `undoAvailable`。`disappeared` 表示调用 `ChangeCell` 后请求的非空气方块读回为空，通常意味着方块行为或邻居校验立即移除了它；发现异常时可调用 `undo_last`。

## 重要实现细节

所有写操作都由 `SubsystemMcpBridge.Update()` 在游戏线程执行。不要从 TCP 工作线程直接调用 Terrain。

方块修改使用 `SubsystemTerrain.ChangeCell()`，而不是直接 `Terrain.SetCellValueFast()`。因此修改会进入游戏已有的方块行为/邻居通知/区块修改计数/光照失效流程。代价是大型结构放置会比较重，后续可以增加“事务式批处理 + 分帧执行”。

当前操作限制为 65536 cells，避免 AI 一次请求制造巨大的 JSON、内存或主线程卡顿。

快照只保存在当前游戏进程内，重启游戏后会消失。建议大规模修改前先调用 `snapshot_region`，确认结果后再调用 `delete_snapshot` 清理。

### 其他模组扩展 MCP 工具

其他模组引用 `SurvivalcraftMCP` 程序集后，可以在自己的 `ModLoader.__ModInitialize()` 中注册工具。handler 会在游戏主线程执行，因此可以安全调用该模组自己的子系统、实体和行为：

```csharp
using System.Text.Json;
using Game.Mcp.Communication;

public override void __ModInitialize()
{
    CustomMcpToolRegistry.Register(
        "my_mod_place_furniture",
        "Place furniture owned by MyMod.",
        new
        {
            type = "object",
            properties = new
            {
                x = new { type = "integer" },
                y = new { type = "integer" },
                z = new { type = "integer" },
                design = new { type = "string" }
            },
            required = new[] { "x", "y", "z", "design" }
        },
        arguments =>
        {
            int x = arguments.GetProperty("x").GetInt32();
            string design = arguments.GetProperty("design").GetString();
            // 在这里调用 MyMod 的家具子系统或行为。
            return new { ok = true, x, design };
        });
}
```

注册名只能包含字母、数字、`_` 和 `-`，且不能重复。AI 先调用 `list_custom_tools` 获取第三方工具的 schema，再调用 `invoke_custom_tool`：

```json
{
  "name": "invoke_custom_tool",
  "arguments": {
    "toolName": "my_mod_place_furniture",
    "arguments": { "x": 10, "y": 64, "z": 10, "design": "wood_table" }
  }
}
```

该接口只提供受控的注册式回调，不支持 AI 直接执行任意 C#、反射或脚本。第三方模组需要自行校验参数和限制操作范围。

`capture_screenshot` 默认返回 1920x1080，也可指定 `width`/`height`，限制为 PNG 支持的 320x180 至 3840x2160。截图由 MCP 模组直接编码并保存到独立的 `SurvivalcraftMCP/ScreenCapture` 目录，不会写入游戏默认的 `ScreenCapture` 目录，也不会绘制原版截图 logo。默认使用当前视角；同时提供 `position:{x,y,z}` 和 `target:{x,y,z}` 时，会临时创建相机进行渲染，不移动玩家、不改变玩家朝向，响应中的 `camera` 为 `temporary`。MCP 响应包含文件路径和 `image` 内容。截图由游戏主线程执行，调用时可能造成短暂帧率下降。

### 帮助页插入图片和链接

MCP 帮助主题的本地化 `HelpText` 支持按文本顺序插入控件，普通文字、图片和链接可以交错放置：

```text
前面的说明
[[image:Assets/Images/connected.png|800|292]]
中间的说明
[[link:打开 API 文档|https://gitee.com/SC-SPM/SurvivalcraftApi]]
后面的说明
```

图片路径必须是当前模组包内的 `Assets/` 文件，格式为 `image:路径|宽度|高度`；链接格式为 `link:显示文字|https://地址`，只允许 `http` 和 `https`。标记所在位置就是控件在帮助页中的位置；图片和链接只会被解析到 MCP 帮助主题，不会影响其他原生帮助页。

## 构建

需要 .NET 10 SDK。先构建 `SurvivalcraftMCP.Mod`，PostBuild 会产生 `.scmod`；再构建 `SurvivalcraftMCP.Server`。

```bash
dotnet build SurvivalcraftMCP.Mod/SurvivalcraftMCP.Mod.csproj
dotnet build SurvivalcraftMCP.Server/SurvivalcraftMCP.Server.csproj
```

把 `SurvivalcraftMCP.Mod/bin/Debug/SurvivalcraftMCP.scmod` 放进游戏的 `Mods` 目录，启动世界后再启动 MCP server。游戏侧默认监听 `127.0.0.1:28899`，也可以在模组设置中修改端口。

服务端通过标准输入/输出承载 MCP JSON-RPC。MCP 客户端配置示例：

```json
{
  "mcpServers": {
    "survivalcraft": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/SurvivalcraftMCP.Server", "--no-build"]
    }
  }
}
```

服务端可以在游戏尚未启动时启动；调用工具会返回 `isError: true`，游戏加载并监听端口后即可正常工作。

### OpenCode 配置

在 NixOS/Linux 上，建议让 OpenCode 显式调用 `dotnet` 和 DLL，不要直接调用 `SurvivalcraftMCP.Server` apphost。OpenCode 的本地 MCP 配置使用数组形式的 `command`：

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "survivalcraft": {
      "enabled": true,
      "type": "local",
      "command": [
        "/home/inf/.nix-profile/bin/dotnet",
        "/home/inf/repos/SurvivalcraftMCP-0.2.0-source/SurvivalcraftMCP.Server/bin/Debug/net10.0/SurvivalcraftMCP.Server.dll"
      ]
    }
  }
}
```

不要同时保留多个 `survivalcraft` 配置条目；修改后重启 OpenCode。若日志显示 `Ignoring MCP config entry without type`，说明仍有旧格式条目，需要删除或补上 `type: "local"`。
## Bridge 设置与帮助

Release 版 Bridge 模组使用 1.9.3 API 的原生“模组设置”入口，提供两个顶层按钮：

- “MCP 模组设置”：查看当前 Bridge 的连接说明。
- “MCP 模组帮助”：查看“**MCP 接入与使用**”说明。

Bridge 不会在游戏客户端内创建 AI 聊天窗口。AI 仍通过外部 MCP 客户端（例如 OpenCode）连接 `SurvivalcraftMCP.Server`，Server 再通过本机端口调用游戏模组。端口可以在原生模组设置中直接输入 `1024-65535` 范围内的数字；修改后重启游戏，并使用 `--port` 让 Server 使用同一个端口。未配置或输入无效时使用默认端口 `28899`。

## Release 构建

在仓库根目录执行：

```bash
dotnet build SurvivalcraftMCP.Mod/SurvivalcraftMCP.Mod.csproj -c Release
dotnet build SurvivalcraftMCP.Server/SurvivalcraftMCP.Server.csproj -c Release
```

输出文件：

- `SurvivalcraftMCP.Mod/bin/Release/SurvivalcraftMCP.scmod`
- `SurvivalcraftMCP.Server/bin/Release/net10.0/SurvivalcraftMCP.Server.dll`

将 `.scmod` 放入游戏的 `Mods` 目录并重启游戏。Server 使用：

```bash
dotnet SurvivalcraftMCP.Server/bin/Release/net10.0/SurvivalcraftMCP.Server.dll
```

### Windows x64 发布版

在 Linux 或 Windows 上生成带 `.exe` 的 Windows x64 框架依赖发布包：

```bash
dotnet publish SurvivalcraftMCP.Server/SurvivalcraftMCP.Server.csproj -c Release -r win-x64 --self-contained false -o release-package/SurvivalcraftMCP.Server-win-x64-release
```

输出目录中的 `SurvivalcraftMCP.Server.exe` 可直接运行；目标 Windows 需要安装 .NET 10 Runtime。当前仓库也提供压缩包：

`release-package/SurvivalcraftMCP.Server-win-x64-release.zip`

运行时可在同一目录打开命令行：

```powershell
.\SurvivalcraftMCP.Server.exe --port 28899
```
