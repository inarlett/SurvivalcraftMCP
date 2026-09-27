# Furniture MCP Example

这是一个独立的 Survivalcraft API 示例模组，演示如何使用 `SurvivalcraftMCP` 提供的自定义工具接口制作和放置家具。

## 构建

先构建 MCP Bridge，再在本目录执行：

```bash
dotnet build FurnitureExample.csproj
```

构建结果为 `bin/Debug/FurnitureExample.scmod`。把它和 `SurvivalcraftMCP.scmod` 一起放入游戏的 `Mods` 目录。

## MCP 工具

- `furniture_list_designs`：列出当前世界已注册的家具设计。
- `furniture_create`：使用一个可制作家具的方块材料创建 `solid`、`table` 或 `bench` 设计。先用 MCP 的 `resolve_block` 获取 `contents`。
- `furniture_place`：以设计索引、坐标和 0-3 的四向旋转放置家具。

示例调用顺序：

1. 用 `resolve_block` 查询木板或其他材料的 `contents`。
2. 调用 `furniture_create`，例如 `name=Oak Table`、`contents=...`、`width=4`、`height=3`、`depth=3`、`shape=table`。
3. 使用返回的 `index` 调用 `furniture_place`。

模组只修改 MCP 工具注册和当前世界地形，不提供任意 C# 执行能力；工具回调由 Bridge 在游戏主线程调用。
