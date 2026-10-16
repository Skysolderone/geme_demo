## MODIFIED Requirements

### Requirement: 本大回合的 Pass 名单

公开快照 SHALL 含"本大回合已 Pass 的在局玩家"名单（Pass 是公开行动）。按座位视图中，该名单 MUST 只保留与本座位同一战场的玩家；他战场玩家是否 Pass MUST NOT 暴露（与「其他战场不可见」一致）。

#### Scenario: 同战场 Pass 可见
- **WHEN** 本大回合战场 1 的对手已 Pass，本机玩家在战场 1
- **THEN** 本机座位视图的 Pass 名单含该对手

#### Scenario: 他战场 Pass 不可见
- **WHEN** 本大回合战场 4 的玩家已 Pass，本机玩家在战场 1
- **THEN** 本机座位视图的 Pass 名单不含战场 4 的玩家
