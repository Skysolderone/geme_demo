## 1. 呈现层与测试

- [x] 1.1 遮罩新增"被揭示棋串的常驻标注显现进度"（design D1），计入遮罩是否为空。验证：`TacticalLayers/棋串军势常驻标注Tests` 新增六个场景（算例：一档条目末步开始后 600 ms → 0；1050 ms → 500‰；1400 ms → 不再列出）；变异验证记录。

## 2. Godot 接线

- [x] 2.1 `BoardView` 画常驻标注时按显现进度隐藏 / 淡入，演出期间按需重画（D2）。验证：源码扫描守门（标注的透明度只取自遮罩给的进度）与变异；`--shot-show=reveal` 重拍 `art/tiered-number-show/reveal-real-tier5.png`，图上同一棋串只有一个完整显示的数。

## 3. 验收

- [x] 3.1 构建 0 警告；`dotnet test` 失败集合与本机基线相同；`--auto-demo` 53 帧不变；`--pick-check` 通过；`openspec validate reveal-label-handoff --strict` 通过。验证：命令退出码与读数。
- [x] 3.2 设计文档 §14.4 军势揭示一条补一句，变更记录 v1.22 → v1.23。验证：人工检查。
