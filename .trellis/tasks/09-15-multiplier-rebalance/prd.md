# 09-15-multiplier-rebalance

> 规格权威：`openspec/changes/multiplier-rebalance/`。本文件只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §9.2、§10.1

## Goal

growth-pass-1 之后 200 局终局盘面势力归因：倍增子占全部势力 76.3%（每颗平均 53.0），普通 1.0 / 堡垒 4.0 / 连珠 2.2 / 协同 8.3。根因是倍率同时放大基础军势与位置加值。本任务把公式改为 `⌊基础军势 × 1.5^min(n,3)⌋ + 位置加值`，并在 Sim 报告里补上"各棋子势力占比"。

## Requirements

- 势力组装：`Multiplier.Apply(基础军势)` + 位置加值；`MaxExponent` 4 → 3；取整只作用于"基础 × 倍率"。
- 势力明细字段不变，新增"明细可复算棋串军势"守门。
- Sim 快照棋串明细记各棋子类型计数；报告新增"各棋子势力占比"段落（倍增子计其放大出的部分，其余计基础军势与分得的位置加值）。
- 设计文档 §9.2、§10.1 与变更记录。

## Acceptance Criteria

- [ ] **power-score** / 棋串军势公式（8 个 Scenario）
- [ ] **power-score** / 势力明细（4 个 Scenario，含新增"明细可复算棋串军势"）
- [ ] **piece-effects** / 倍增子的棋串倍率（4 个 Scenario，含新增"倍率不作用于位置加值"）
- [ ] `openspec validate multiplier-rebalance` 通过
- [ ] `implement.md` 清单完成（4.2 的 200 局回归由主会话执行）
- [ ] 既有算例按新公式改写，逐条列出，不改实现语义凑绿

## Out of Scope

- 不改倍数 1.5、连珠与协同加值公式、普通子、碾压起始、部署上限、征募权重。
