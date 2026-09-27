# Survivalcraft MCP 工具使用文档

这份文档面向 MCP 客户端中的 AI。Server 还提供了 `get_usage_guide` 工具，AI 可以直接调用它获取同样的关键规则，不需要搜索游戏源码。

## AI 推荐工作流

1. 第一次使用或不确定参数时，调用 `get_usage_guide`，可选主题为 `general`、`blocks`、`query`、`building`、`safety`。
2. 玩家说出方块名称时，先调用 `list_blocks`，用返回的 `displayName` 或 `name` 匹配，不要猜 contents 数字。
3. 调用 `resolve_block` 获取方块 ID、creative 变体、`data` 和可直接放置的 `value`。
4. 修改前用 `get_region`、`find_blocks` 或 `get_surface_heights` 读取现场；覆盖已有建筑时先 `snapshot_region`。
5. 优先使用 `build_box`、`build_wall`、`build_cylinder`、`build_staircase` 等高层工具，避免逐个生成坐标。
6. 写入后检查返回的 `report`，再用 `get_region`、`check_walkable` 或 `capture_screenshot` 验证；错误时使用 `undo_last` 或 `restore_snapshot`。

## 方块名称和变体

### `list_blocks`

列出当前游戏实际加载的方块。返回内容包括：

- `index`：方块内容 ID（contents）
- `name`：运行时类型名，例如 `StoneBlock`
- `displayName`：游戏显示名称，可能是本地化名称
- `variants`：creative 变体
- `dataSchema`：data 位域的解释

玩家说“石头、木门、石砖、红色地毯、桌子”等自然语言时，应在 `displayName` 和 `name` 中选择最匹配项。找不到时不要猜数字，应把 `list_blocks` 的结果作为候选重新匹配。

### `resolve_block`

`blockName` 支持以下形式：

```json
"Stone"
```

```json
"StoneBlock"
```

```json
1
```

```json
{"name":"Stone"}
```

```json
{"contents":1}
```

返回的 `variants` 中，每个变体包含：

- `value`：完整 raw cell value，可直接给 `set_block` 或 `place_structure`
- `data`：该变体的 data 值
- `displayName`：该变体的显示名称
- `fields`：已解码的朝向、颜色、设计索引等字段

门朝向、台阶上下、楼梯旋转、火把贴面、地毯颜色、家具设计索引等情况，都应优先使用 `variants` 和 `dataSchema`，不要自行猜位运算。

## 查询和坐标

- 地形坐标 `x/y/z` 是整数格子索引；`min` 和 `max` 都包含在区域内。
- 玩家、碰撞和临时相机使用浮点世界坐标。
- `list_players` 返回的玩家 `position.y` 是玩家脚底高度，不是地形格子 y。
- `get_region` 默认返回稀疏非空气 `cells`，每项包含 `x/y/z/value/contents/data/light`。
- `get_region` 设置 `nonAirOnly:false` 才会包含空气；设置 `contents` 可按方块 ID 过滤。
- 只有需要兼容旧格式时才设置 `includeValues:true`。一维数组顺序是 `y -> z -> x`，其中 x 变化最快。
- `find_blocks` 按 `blockName` 或 `contents` 返回匹配坐标；注意检查 `truncated`。
- `get_surface_heights` 一次返回每个 X/Z 柱的最高非空气方块。
- `check_walkable` 的 `position` 是玩家脚底位置，默认身体尺寸为宽 0.6、高 1.8、深 0.6。

## 建筑和地形

### 建筑工具

| 工具 | 用途 |
| --- | --- |
| `fill_region_by_block` | 用方块名称填充包含边界的区域 |
| `build_box` | 创建实心或空心长方体 |
| `build_wall` | 沿 x 或 z 轴创建规则墙体 |
| `build_cylinder` | 创建实心或空心竖直圆柱 |
| `build_staircase` | 创建沿 x 或 z 轴上升的实心楼梯 |
| `capture_structure` / `place_structure` | 复制、旋转和镜像结构 |

`build_box` 的 `sizeX/sizeY/sizeZ` 是从 `origin` 开始的格子数量。`hollow:true` 只跳过严格内部，底面和顶面仍然会铺满；想要房间内部没有地板，需要在建壳后另行清除内部区域。

`flatten_region` 的语义是：填充 `targetY` 以下的格子，清空 `targetY` 及以上的格子。它会破坏原地形，使用前应创建快照。

每次规则生成和批量写入最多处理 65536 格。

## 写入、快照和撤销

所有写入响应都包含 `report`，重点字段如下：

- `changed`：实际发生变化的格子数
- `overwrittenNonAir`：覆盖的原非空气格子数
- `disappeared`：写入非空气方块后立即读回为空的数量
- `overwrittenSamples` / `disappearedSamples`：最多 64 个示例坐标
- `undoAvailable`：是否可以调用 `undo_last`

推荐的安全顺序：

```text
snapshot_region
    -> get_region / find_blocks / get_surface_heights
    -> replace_blocks(dryRun=true)（如果适用）
    -> 执行写入
    -> 检查 report
    -> get_region / check_walkable / capture_screenshot
    -> undo_last 或 restore_snapshot
```

`snapshot_region`、`undo_last` 和快照列表只保存在当前游戏进程内，重启游戏后会丢失。`restore_snapshot` 默认恢复原位置，也可以传入新的 `origin` 复制到其他位置。

## 截图和验证

`capture_screenshot` 不使用原版截图目录，也不会带原版 logo。默认使用当前视角；同时传入 `position` 和 `target` 时，会创建临时相机，不移动玩家。支持视觉输入的模型可以直接读取返回的 PNG。

建议建筑完成后从外部临时机位截图，而不是只使用玩家第一人称视角：

```json
{
  "name": "capture_screenshot",
  "arguments": {
    "position": {"x": 20.5, "y": 75.0, "z": 20.5},
    "target": {"x": 10.5, "y": 65.0, "z": 10.5},
    "width": 1280,
    "height": 720
  }
}
```

## 常见错误

- `Unknown block name`：先调用 `list_blocks`，再用实际返回的 `displayName` 或 `name` 调 `resolve_block`。
- `requires Object`：`resolve_block` 的参数必须放在 `blockName` 中，例如 `{"blockName":"Stone"}`；不要把字符串当作整个 `arguments`。
- 建筑位置偏一格：检查是否混用了玩家脚底浮点坐标和地形格子坐标。
- 空心房子封顶或铺满地板：`build_box hollow:true` 会保留底面和顶面，这是预期行为。
- 方块放置后消失：检查 `report.disappeared`，通常是方块行为或邻居校验移除了方块；先 `undo_last`，再用正确的 creative 变体重试。
- 区域结果太大：使用默认稀疏 `get_region`、`contents` 过滤或 `find_blocks`，不要请求完整空气区域。

## 其他模组工具

先调用 `list_custom_tools` 查看其他模组注册的工具和 schema，再调用 `invoke_custom_tool`。AI 不能通过此接口执行任意 C#、反射或脚本；第三方模组负责参数校验和行为安全。
