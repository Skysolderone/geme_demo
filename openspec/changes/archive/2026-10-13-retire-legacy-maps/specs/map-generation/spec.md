## ADDED Requirements

### Requirement: 地图生成器已退役

本能力原有的边疆档（`gen:`）与棋盘档（`board:`）地图生成器已随 `retire-legacy-maps` 删除（负责人 2026-10-13 裁决 L1），系统 MUST NOT 再提供任何由地图种子生成非链接档地图的途径。对局地图的生成只由 `link-map-generation` 规定。本条是为保持能力目录可归档而留的说明性条目（OpenSpec 不允许能力不含 Requirement），后续若整体删除本能力目录，本条随之移除。

#### Scenario: 旧生成器标识不可生成
- **WHEN** 以 `gen:12345`、`gen:12345:p7`、`board:12345` 或 `board:12345:n9` 请求生成或解析地图
- **THEN** 系统报错并说明只接受 `link:<地图种子>`，不产生任何地图数据

## REMOVED Requirements

### Requirement: 地图种子与确定性
**Reason**: 边疆档生成器（`gen:`）已删除（负责人 2026-10-13 裁决 L1）。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 生成参数
**Reason**: 边疆档生成器已删除（L1），平台数与新地表开关随之移除。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 布局规则
**Reason**: 边疆档生成器已删除（L1）。新地表的地形规则本身保留在 `terrain`（L4），只删投放器。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 校验闭环
**Reason**: 边疆档与棋盘档生成器已删除（L1）。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 生成图的地图标识
**Reason**: `gen:` 标识族已删除（L1）。入口取随机地图种子的短种子折叠迁入链接档标识，输出不变。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 棋盘档生成参数
**Reason**: 棋盘档生成器（`board:`）已删除（L1）。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 棋盘档布局规则
**Reason**: 棋盘档生成器已删除（L1）。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 棋盘档生成图标识
**Reason**: `board:` 标识族已删除（L1）。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。

### Requirement: 既有生成器降级为测试夹具
**Reason**: 过渡期条款：两套生成器已彻底删除（L1），不再存在夹具解析。
**Migration**: 新模式地图由 `link-map-generation` 生成（`link:<地图种子>`）；测试改用 `link:` 地图或手写夹具。
