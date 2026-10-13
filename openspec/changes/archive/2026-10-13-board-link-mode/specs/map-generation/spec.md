## ADDED Requirements

### Requirement: 既有生成器降级为测试夹具

边疆档生成器（`gen:` 标识族）与棋盘档生成器（`board:` 标识族）SHALL 继续存在并保持本能力既有的全部 Requirement（含"逐字节不变"条款），但 MUST NOT 由任何玩家入口到达；`gen:` 与 `board:` 标识只能经 Core 内部的夹具解析与 `map` 调试子命令使用。新模式的地图生成见 `link-map-generation`。

#### Scenario: 夹具生成不变
- **WHEN** Core 测试经夹具解析生成 `gen:12345` 与 `board:12345`
- **THEN** 导出文件与引入本 change 之前逐字节相同

#### Scenario: 玩家入口拒绝旧标识
- **WHEN** 以 `--map=gen:12345` 启动终端版
- **THEN** 入口报错退出并说明只接受 `link:<地图种子>`
