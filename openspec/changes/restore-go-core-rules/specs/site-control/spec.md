## REMOVED Requirements

### Requirement: 据点档位与分值
**Reason**: 裁决 #2 / #14——总势力回归"独占空格 + 棋串军势"，据点不再计分，整体移除，不保留无功能概念。
**Migration**: 无替代。地图数据去掉据点字段（见 `map-definition`）；批量跑局配置去掉据点分值（见 `simulation-harness`）。

### Requirement: 据点控制判定
**Reason**: 据点概念移除，控制判定随之退役。
**Migration**: 无替代。「唯一覆盖查询」仍服务于信物控制（见 `coverage-territory`）。

### Requirement: 据点分计入势力
**Reason**: 总势力改为"独占空格数 + 棋串军势"。
**Migration**: 见 `power-score`「总势力」。

### Requirement: 据点公开
**Reason**: 据点概念移除，无可公开内容。
**Migration**: 见 `information-visibility`「始终公开的信息」。
