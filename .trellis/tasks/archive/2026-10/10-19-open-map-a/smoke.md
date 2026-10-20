# open-map 段 A 冒烟（design D10，只报数不调参）

> 命令：`run --out sim-out/open-map/smoke/link1 --map link:1 --seed 1 --count 10 --difficulty Standard --turn-limit 600`；`run --out sim-out/open-map/smoke/per-map --map link:100 --map-per-match --seed 11 --count 10 --difficulty Standard --turn-limit 600`（link:100–109）；统计由脚本按日志 result / turn 行独立算出，`analyze` 报告节选附后，完整报告见 analyze-all.txt。

## link1
```
局数 10；大回合 中位 22.5 / 最小 20 / 最大 28
终局原因： {'LastBattlefieldSettled': 10} ；turn_limit 截断 0/10 = 0%
单小回合耗时 中位 64.0 ms（样本 932，最大 665）
五主题平均名次（截断局不计）： {'Forest': 2.8, 'Mixed': 2.5, 'Palisade': 2.42, 'Plateau': 2.5, 'River': 2.43} {'Forest': 5, 'Mixed': 6, 'Palisade': 12, 'Plateau': 10, 'River': 7}
```
## per-map
```
局数 10；大回合 中位 24.5 / 最小 21 / 最大 29
终局原因： {'LastBattlefieldSettled': 10} ；turn_limit 截断 0/10 = 0%
单小回合耗时 中位 80.0 ms（样本 988，最大 650）
五主题平均名次（截断局不计）： {'Forest': 2.5, 'Mixed': 2.5, 'Palisade': 2.64, 'Plateau': 2.22, 'River': 2.62} {'Forest': 6, 'Mixed': 6, 'Palisade': 11, 'Plateau': 9, 'River': 8}
```
## all
```
局数 20；大回合 中位 24.0 / 最小 20 / 最大 29
终局原因： {'LastBattlefieldSettled': 20} ；turn_limit 截断 0/20 = 0%
单小回合耗时 中位 72.0 ms（样本 1920，最大 665）
五主题平均名次（截断局不计）： {'Forest': 2.64, 'Mixed': 2.5, 'Palisade': 2.52, 'Plateau': 2.37, 'River': 2.53} {'Forest': 11, 'Mixed': 12, 'Palisade': 23, 'Plateau': 19, 'River': 15}
```

## 结论（只报数）
- 20 局全部以规则终局（最后战场结算）收尾，`turn_limit`（600 小回合）截断 0/20 = 0%；大回合中位 24（旧规则 15–16）、最小 20、最大 29，对局约长一半。
- 单小回合耗时中位 72 ms（最大 665 ms）。
- 五主题平均名次都在 2.4–2.6（20 局样本，结论不可靠）；锁定人次 Palisade 23 / Plateau 19 / River 15 / Mixed 12 / Forest 11。
- R-3 的量化：Standard ×4 的 20 局没有一局陷入循环提子；循环只在 Easy + 写死权重的终端脚本局（种子 43 等 9/12 个种子）出现，见 notes.md R-3。

## analyze 报告节选（all，20 局）
```
胜率 / 名次类指标排除截断局 0 局，有效样本 20 局（被小回合数截断的局没有名次与胜者，restore-go-core-rules D5）。
## 收敛情况（未以规则级原因终局 = 旧日志达大回合上限 MajorRoundLimit / 跑局层截断 turn_limit）
- 规则级终局 20 局；未收敛（达上限 / 截断）0 局，不收敛率 0.0% (0/20，95% 区间 0.0%–16.1%)
- 终局局的结束大回合分布：20×2，21×1，22×3，23×2，24×4，25×4，27×2，28×1，29×1；平均 偏离：超出目标上限 10 大回合，实测 24 大回合，高 14 大回合（+140%）
- 终局局的结束大回合：中位 24，最长 29
- 样本 20 局（其中并列 2 局；已排除截断局 0 局）——不足 200 局，结论不可靠
### 7. 以 turn_limit 截断的局数与占比（目标 0；截断是跑局层的技术设施，不是规则终局）
- 截断（turn_limit）0 局（0.0%），平均截断于第 无样本 大回合；在目标 0–0% 内（实测 0%）
- 第 4 大回合行动位置 1 的玩家最终平均名次 2.05（样本 20）
- 第 4 大回合行动位置 2 的玩家最终平均名次 2.5（样本 20）
- 第 4 大回合行动位置 3 的玩家最终平均名次 2.6（样本 20）
- 第 4 大回合行动位置 4 的玩家最终平均名次 2.85（样本 20）
### 8. 三类终局原因各自的占比与平均结束大回合（占比分母为全部纳入局；截断局单列）
- 只剩一名参赛玩家（LastPlayerStanding）0 局（0.0%），平均结束大回合 无样本
```
