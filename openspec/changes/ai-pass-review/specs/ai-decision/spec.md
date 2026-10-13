## ADDED Requirements

### Requirement: 链接模式下的决胜一手

在 Link 模式中，当 AI 依停手阈值本将 Pass，且同时满足：(1) 本战场其他在局玩家在本大回合都已 Pass（即本次 Pass 会使本战场整轮 Pass 而结算）；(2) AI 的当前势力不是本战场最高（并列最高视为最高）——AI SHALL 检查是否存在一个合法批次（单点，或同一单点排序下的强制贪心批次，枚数至部署上限）使预演后己方势力 ≥ 本战场其他在局玩家的最高势力；存在时 SHALL 落下其中使"己方势力 − 对手最高势力"最大的批次（并列取单点排序靠前者），不存在时照常 Pass（负责人 2026-10-13 裁决 P7 = A 窄版）。

条件 (1) SHALL 读取公开快照中的「本大回合的 Pass 名单」（`information-visibility`），MUST NOT 读取任何非公开信息。判定 MUST 复用正式决策的候选生成、预演与势力计算，MUST NOT 新建第二套评价；本条改变了"哪些候选参与保留"，AI 评价版本 SHALL 由 3 递增为 4（裁决 P13）。MUST NOT 改变不满足上述条件时的任何决策。Legacy 模式 MUST NOT 受影响。

#### Scenario: 最后一个 Pass 会输且有反超手
- **WHEN** Link 模式双人战场中对手本大回合已 Pass，AI 势力 77、对手 156，最好单点评价收益为负，但某合法单点落下后 AI 势力为 160
- **THEN** AI 落下该单点而不是 Pass，本战场本大回合不结算

#### Scenario: 无反超手照常 Pass
- **WHEN** 同一情形下没有任何合法批次能让 AI 势力追平对手
- **THEN** AI Pass，本战场按整轮 Pass 结算

#### Scenario: 对手尚未 Pass 时不触发
- **WHEN** AI 落后、本将 Pass，但本战场还有其他在局玩家本大回合尚未 Pass
- **THEN** 决胜一手不触发，决策与引入本条之前相同

#### Scenario: 领先者不触发
- **WHEN** 对手已 Pass、AI 势力为本战场最高
- **THEN** 决胜一手不触发，AI 照常 Pass

#### Scenario: 旧模式不变
- **WHEN** 以手写 Legacy 夹具重跑既有测试
- **THEN** 决策与引入本条之前逐字节相同
