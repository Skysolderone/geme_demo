# 实施

按 `openspec/changes/board-map/tasks.md` 顺序分段：A（坐标与地图数据）→ B（校验与生成器）→ C（选图与图形表现）→ D（验证与收尾）。每段结束报告，负责人确认后进下一段。

- 不得 git commit；同一时间只跑一个 dotnet；用 `python` 不用 `python3`。
- 理解代码优先用 codegraph；不读大文件与媒体回传。
- 段 D 的冒烟先问负责人规模（至多 20 局），不得自行开跑。
- 回滚点：段 A 的 `Coord` 签名变化；既有地图与 `gen:` 种子导出必须逐字节不变。
