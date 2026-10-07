# builtin-board-maps（内置棋盘图）截图与自检记录

change `builtin-board-maps` 段 D 的选图界面截图（`mapselect-*.png`）、候选图（`candidates/`）与自检日志（`*.log`），由 `shots.sh` 产出。

> `shots.sh` 第 5 步里的 `autodemo-v5`（`--map=siege-4p-base-v5`）所用地图已于 `retire-legacy-maps`（2026-10-07）删除，该步的 Godot 进程会报“已删除”并以退出码 1 结束，脚本因此不再可原样复跑出全部产物（脚本没有 `set -e`，其余步骤照常）；`autodemo-v5.log` 作为历史留档保留，脚本不改。其余步骤只用内置棋盘图、`board:` 图与缺省图。
