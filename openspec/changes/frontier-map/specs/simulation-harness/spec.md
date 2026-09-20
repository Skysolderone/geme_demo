## ADDED Requirements

### Requirement: 各入口按地图标识选图

批量跑局、终端版与图形版 SHALL 都能通过一个地图选项按地图标识选图；未给出该选项时 MUST 加载 `siege-4p-base-v4`。三个入口 MUST 共用同一份"标识 → 地图"的解析，MUST NOT 各自维护一份地图清单。

地图标识无法解析时，入口 MUST 报错并列出可用的地图标识，MUST NOT 静默回落到缺省地图。地图选项 MUST 在各入口的严格命令行解析中登记（未登记的选项按 `strict-cli` 报错）。

实际加载的地图标识 MUST 写入对局日志首部与批次配置记录（现状已有，边疆图同样适用）。面向人的出生区编号显示 MUST 支持到该地图的出生区数，MUST NOT 假定不超过 4。

#### Scenario: 缺省地图不变
- **WHEN** 不带地图选项启动终端版
- **THEN** 加载 `siege-4p-base-v4`，对局与引入本选项之前在同一种子下逐步相同

#### Scenario: 选边疆图
- **WHEN** 以地图标识 `siege-frontier-v1` 启动终端版
- **THEN** 插旗提示列出 1–6 号平台，对局日志首部记录的地图标识为 `siege-frontier-v1`

#### Scenario: 未知标识报错
- **WHEN** 以地图标识 `no-such-map` 启动任一入口
- **THEN** 入口报错退出并列出可用地图标识，不开始对局

#### Scenario: 边疆图批量跑局
- **WHEN** 在 `siege-frontier-v1` 上用 4 个 AI 批量跑 20 局
- **THEN** 全部对局正常终局，批次报告给出平均大回合数、各平台被选次数与胜率、AI 单步决策耗时的均值与最大值
