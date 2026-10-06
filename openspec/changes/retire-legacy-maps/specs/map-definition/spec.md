## REMOVED Requirements

### Requirement: 4 人基准地图
**Reason**: 负责人 2026-10-06 裁决所有地图都由互不连通的棋盘组成、旧地图彻底删除；`siege-4p-base-v5` 已不是缺省地图（`builtin-board-maps`）。
**Migration**: 使用内置棋盘图 `siege-4p-board-v1`；以 `--map=siege-4p-base-v5` 启动时报"已删除"。

### Requirement: 边疆档基准地图
**Reason**: 同上；边疆档整体删除。
**Migration**: 使用内置棋盘图或随机棋盘图 `board:<种子>`。

### Requirement: 2 人基准地图
**Reason**: 同上。
**Migration**: 使用内置棋盘图 `siege-2p-board-v1`。

### Requirement: 3 人基准地图
**Reason**: 同上。
**Migration**: 使用内置棋盘图 `siege-3p-board-v1`。
