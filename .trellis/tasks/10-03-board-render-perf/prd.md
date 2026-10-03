# 10-03-board-render-perf

> 规格权威：`openspec/changes/board-render-perf/`。

棋盘图渲染开销：场景装饰与面砖合批（绘制调用不随场景格线性增长）、结算演出期间增量重画棋子。画面逐像素不变；规则与 Presentation 零改动。

负责人 2026-10-03：全部由实施方决定，只要结果。

约束：不得 git commit；不跑批量对局；同一时间只跑一个 dotnet / Godot；截图只存盘、比对用脚本。
