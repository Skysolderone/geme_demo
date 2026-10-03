# 09-30-show-sound-cues

> 规格权威：`openspec/changes/show-sound-cues/`。

结算演出加程序合成的占位音效：落子 / 提子 / 信物 / 领地到账 / 军势到账 / 横幅。提示导出为 Presentation 纯函数；Godot 启动时合成波形，不引入文件；`--mute` 与无人值守零影响。

负责人 2026-09-30 裁决：方案 A（程序合成占位音）。

约束：不得 git commit；不跑批量对局；同一时间只跑一个 dotnet / Godot。
