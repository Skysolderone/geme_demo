# 10-09-board-link-mode-a

> 规格权威：`openspec/changes/board-link-mode/`（proposal 裁决表 + design D1/D2/D8 + specs：`battlefield`「战场的定义与规则作用域」「链接事件」、`link-map-generation` 全部、`map-definition`「地图为设计师固定的静态数据」「地图规格档」「旧地图降级为测试夹具」、`map-generation`「既有生成器降级为测试夹具」）。

段 A：Core 数据层。战场导出（可落子格连通分量）、`链接` 规格档与地图增长（物化集合 + 版本号）、`link:<种子>` 初始布局生成器、链接生成器（公共棋盘 + 双通道，备选位置，退化直通道）、物化状态校验、`MapCatalog` 收口与 `MapCatalog.Legacy`、场景格不可改造。对应 tasks.md 1.1–1.7。

## Acceptance Criteria

- [ ] tasks.md 1.1–1.7 全部勾掉，每个 Scenario 有可追溯测试
- [ ] 旧地图测试经 `MapCatalog.Legacy` 加载后全绿；`gen:` / `board:` 导出逐字节不变
- [ ] `dotnet build` 零警告、`dotnet test -c Release` 全绿
- [ ] 种子 1–20 穷举链接序列全部通过校验，退化次数写进任务报告

约束：子 agent 不得 git commit；不跑批量对局；同一时间只跑一个 dotnet；理解代码优先 codegraph。
