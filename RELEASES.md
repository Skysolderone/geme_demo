# 围杀 Siege 生产发布记录

本文件是《围杀》对外发布的唯一台账：每次发版按下面的流程执行，并在“版本台账”里登记一行。下载包放在 [GitHub Releases](https://github.com/Skysolderone/geme_demo/releases)，官网是 <https://siege.wws741.workers.dev>（`docs/` 目录）。

## 发版流程（Windows x64）

1. **定版**：在 `main` 上构建 Release 并跑快速测试，全部通过才能发版。
   ```powershell
   dotnet build siege.sln -c Release
   dotnet test tests/Siege.Core.Tests/Siege.Core.Tests.csproj --filter "Category!=Slow&Category!=Perf"
   ```
2. **打标签**：在定版提交上打附注标签 `vX.Y.Z`，版本号只能是三段数字（导出脚本会校验）。
3. **导出**：运行 `tools/export-release.ps1 -Version X.Y.Z`，产物是 `build/Siege-windows-x64-vX.Y.Z.zip`。脚本会重写本地的 `src/godot/export_presets.cfg`，并清掉 `build/` 里旧的 zip。
4. **验收**：把 zip 解压到新目录，用包内的 exe 以无界面方式自动对局，要能跑到终局、退出码为 0。
   ```powershell
   Siege.exe --headless -- --auto-demo --rounds=0 --seed=5
   ```
5. **校验值**：`(Get-FileHash build/Siege-windows-x64-vX.Y.Z.zip -Algorithm SHA256).Hash`。
6. **GitHub Release**：`gh release create vX.Y.Z <zip> --title ... --notes-file ...`。正式版加 `--latest`，预发布加 `--prerelease`。
7. **官网**：更新 `docs/index.html` 里的下载链接、包大小、SHA256、当前版本文字和更新记录，然后运行 `npx wrangler deploy`。wrangler 上传的是本地 `docs/` 目录，不看 git；不想公开的文件要写进 `docs/.assetsignore`。
8. **登记**：在下面的台账里加一行，写好该版说明，和官网改动一起提交并推送 `main` 与标签。

## 版本台账

| 版本 | 日期 | 类型 | 标签提交 | 附件 | 大小（字节） | SHA256 | 官网 | 验收 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| v0.6.0 战斗流程 MVP | 2026-10-26 | 正式 · Latest | `52a32e5` | `Siege-windows-x64-v0.6.0.zip` | 75400650 | `2915CFD5CD4BB0AFDAAB2CDF6CCBBC69732375D0378A06842FBA1A18493D9745` | Workers，与本条登记同一提交 | Windows 本机解压，headless 自动对局 seed=5 跑到终局；快速测试 2294 条全过 |
| v0.5.0 开放地图版·美术版 | 2026-10-23 | 预发布 | `c490828` | `Siege-windows-x64-v0.5.0.zip` | 75351865 | `682067048344607D02D9F872A5E0FD182F07F5677D3FE8F9DAFB0445BC0F88D1` | Workers，`0ff938c` | Windows 本机 headless 自动对局 |
| v0.4.0 开放地图版 | 2026-10-23 | 预发布 | `4344772` | `Siege-windows-x64-20261023.zip` | 73543253 | `78FEF6B4F367391715E4F1D3F42543C2521EE6E262E6AC8E26992AC4F9046F93` | Workers，`55e7b8a` | Windows 本机 headless 自动对局 |
| v0.3.0 | 2026-10-05 | 正式（旧线） | `f3108b1` | `Siege-windows-x64.zip` / `Siege-macos.zip` | 73607533 / 126262240 | Win `0cbdcaf9cd7e81fae944518890409ac109998bc5e5aa01957fd3cab59bc1aea1` / Mac `5f8edd81ca90a8a9ceb874f195869d3df754718598e41ba687d163e8a5511a6d` | GitHub Pages，`e198004`、`7c72929` | Windows 包在 macOS 交叉导出，未在 Windows 实测 |
| v0.2.0 | 2026-10-04 | 正式（旧线） | `3c14401` | `Siege-windows-x64.zip` / `Siege-macos.zip` | 73573041 / 126191796 | Win `af2105f361c6260eda1b5feee009860900f9c04d95238d401d125ee52660e897` / Mac `bfedb6b7dcf267eb11a528a67ea166eb2bb06c3eb54ea828ead637b2152a3da9` | GitHub Pages，`3c14401` | 同上 |
| v0.1.0 | 2026-10-04 | 正式（旧线） | `7e1036f` | `Siege-windows-x64.zip` / `Siege-macos.zip` | 73568094 / 126180670 | Win `9220734fe6712bd8cd779c9bf9161a668f998e3ec43fedb4e186c47e902dad0d` / Mac `c9de85f26023a2f72588beedec35e1d687efc9f00e8c01d02355a8afd2e64e4d` | GitHub Pages，`7e1036f` | 同上 |

v0.1.0–v0.3.0 的 SHA256 取自 GitHub 记录的附件摘要。

## 各版本说明

### v0.6.0 战斗流程 MVP（2026-10-26）

基础版：与标签 `base-v1` 同一提交。开放地图版与旧共享棋盘线的独有功能合并后，一局可以从插旗选盘、征募暂放、预演确认、结算揭示、回合摘要一路打到终局排名。

- 阵型（计分 V2）：同色连通棋串达到 3 / 5 / 8 / 12 枚时依次升为 1–4 阶；旧存档沿用计分 V1。
- 军势逐步揭示：结算时逐条棋串揭示算式，数值分档放大并配音效。
- 镜头跟随（`F`）与对手回合摘要。
- 信息面板重排、三级界面主题、棋串军势标签、坐标标签避让。
- 地图、AI 与美术同 v0.5.0；官网截图沿用 v0.5.0 实拍。
- 已知情况：计分 V2 下领先方势力增长很快（验收局 seed=5 第一名约 1125 万，v0.5.0 同种子约 20 万），数值平衡仍在观察；暂无 macOS 包。

### v0.5.0 开放地图版·美术版（2026-10-23）

浮空国度美术基础版（标签 `art-base-v1`）：浮岛天空、砂岩与苔绿棋盘、十种棋子石雕造型、石墨蓝界面。规则与 AI 同 v0.4.0。

### v0.4.0 开放地图版（2026-10-23）

开放地图首次发版：五块主题出生棋盘拼成一张图，前 3 大回合限自己的棋盘，第 4 大回合起全图交战；第 100 大回合强制结算；AI 停滞停手、整批候选与搭桥候选。当时缺少旧线的阵型、镜头跟随、军势揭示与回合摘要。

### v0.1.0–v0.3.0（2026-10-04 — 10-05）

旧共享棋盘线（标签 `legacy-shared-board`），提供 Windows 与 macOS 包，发布页在 GitHub Pages。v0.3.0 加入阵型、军势揭示与界面主题。

## 其他标签

| 标签 | 提交 | 含义 |
| --- | --- | --- |
| `base-v1` | `52a32e5` | 最基础版本，后续开发的对照基线；对外版本为 v0.6.0 |
| `art-base-v1` | `c490828` | 浮空国度美术基础版（v0.5.0） |
| `legacy-shared-board` | `7589022` | 旧共享棋盘线的最后一个提交 |
