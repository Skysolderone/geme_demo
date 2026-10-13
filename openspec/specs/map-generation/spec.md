# map-generation Specification

## Purpose
TBD - created by archiving change map-generator. Update Purpose after archive.
## Requirements
### Requirement: 地图生成器已退役

本能力原有的边疆档（`gen:`）与棋盘档（`board:`）地图生成器已随 `retire-legacy-maps` 删除（负责人 2026-10-13 裁决 L1），系统 MUST NOT 再提供任何由地图种子生成非链接档地图的途径。对局地图的生成只由 `link-map-generation` 规定。本条是为保持能力目录可归档而留的说明性条目（OpenSpec 不允许能力不含 Requirement），后续若整体删除本能力目录，本条随之移除。

#### Scenario: 旧生成器标识不可生成
- **WHEN** 以 `gen:12345`、`gen:12345:p7`、`board:12345` 或 `board:12345:n9` 请求生成或解析地图
- **THEN** 系统报错并说明只接受 `link:<地图种子>`，不产生任何地图数据

