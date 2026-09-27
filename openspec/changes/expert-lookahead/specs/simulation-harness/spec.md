## ADDED Requirements

### Requirement: 各入口的难度选项

批量跑局、终端版与图形版 SHALL 都能选择简单、标准、高难、专家四档 AI 难度。未选择时 SHALL 取标准，与改动前相同。

- **批量跑局**：`--difficulty <名称>` 对全部玩家生效；配置文件的逐玩家难度（`Players[].Difficulty`）SHALL 支持各玩家取不同难度，例如 1 名专家与 3 名标准。各玩家实际生效的难度 MUST 写入批次配置记录与日志首部。
- **终端版**：`play --difficulty <名称>` 设定全部 AI 玩家的难度；用法说明 MUST 列出四档名称。
- **图形版**：选图界面 SHALL 提供难度选择（缺省标准），并支持 `--difficulty=<名称>` 用户参数；自动演示、拾取自检与截图模式 SHALL 保持标准难度。

难度名称的解析 SHALL 严格：只接受 `Easy`、`Standard`、`Hard`、`Expert` 四个名称，不区分大小写。数字（包括既有三档的序号）、未知名称与空值 MUST 在开局或跑局之前报错退出，并列出可用名称，MUST NOT 回落到缺省值。三个入口与配置文件 MUST 共用同一处解析。

#### Scenario: 批量跑局选专家
- **WHEN** 以 `run --difficulty Expert --count 2` 执行
- **THEN** 配置记录与每局日志首部写明四名玩家的难度都是专家、前瞻宽度 4

#### Scenario: 逐玩家难度
- **WHEN** 以配置文件 `Players` 为专家、标准、标准、标准执行一批 4 人对局
- **THEN** 每局中玩家 1 按专家决策、其余三名按标准决策，配置记录与日志首部逐名写明

#### Scenario: 数字难度被拒绝
- **WHEN** 以 `run --difficulty 3` 或 `play --difficulty 1` 执行
- **THEN** 入口在创建输出目录或开局之前报错退出，列出四个可用名称，不回落到标准

#### Scenario: 未知难度被拒绝
- **WHEN** 以 `--difficulty Master` 执行，或配置文件某玩家的难度为 `"Master"`
- **THEN** 入口在跑局或开局之前报错退出，不回落到标准

#### Scenario: 名称不区分大小写
- **WHEN** 以 `play --difficulty expert` 执行
- **THEN** 全部 AI 玩家按专家难度开局

#### Scenario: 缺省仍为标准
- **WHEN** 批量跑局、终端版与图形版都不给难度
- **THEN** 三个入口的 AI 都是标准难度，对局与改动前逐步相同

#### Scenario: 图形版选专家
- **WHEN** 在图形版选图界面把难度选为专家后开局，或以 `--difficulty=Expert` 启动
- **THEN** 本局全部 AI 玩家按专家难度决策；以 `--auto-demo` 启动时 AI 仍为标准难度
