# awesome-gamedev-agent-skills（第三方内容）

本项目从该上游仓库挑了三个 Godot 相关的 agent skill，原样放进 `.claude/skills/`。

- 上游地址：<https://github.com/gamedev-skills/awesome-gamedev-agent-skills>
- 固定提交：`d4b0e35550c55ae70bdfcab4ef5a0e94610438a9`（上游提交日期 2026-09-27）
- 取得日期：2026-10-05
- 取得方式：`git clone` 后 `git checkout` 到上面的提交，再用 `cp` 复制。没有用 `npx skills add` 或插件市场，没有安装 router 与上游其余 skill。
- 许可证：Apache-2.0，上游的 `LICENSE` 与 `NOTICE` 原样放在本目录。

## 装了哪些

| skill | 上游目录 | 本项目目录 |
|---|---|---|
| `godot-shaders` | `skills/godot/godot-shaders/` | `.claude/skills/godot-shaders/` |
| `godot-csharp` | `skills/godot/godot-csharp/` | `.claude/skills/godot-csharp/` |
| `godot-ui-control` | `skills/godot/godot-ui-control/` | `.claude/skills/godot-ui-control/` |

每个目录只有 `SKILL.md` 和 `references/` 下的一份文档。

## 内容未修改

上面三个目录里的六个文件，以及本目录的 `LICENSE` 与 `NOTICE`，都是上游固定提交的逐字节副本，没有做任何修改。`SKILL.md` 末尾 "Related skills" 提到的其他 skill 没有安装，原文保留。

## 校验和（SHA-256）

| 文件 | SHA-256 |
|---|---|
| `.claude/skills/godot-shaders/SKILL.md` | `15bf46f7b0af586f026c1bb58beaeba2579009b70398f43859b7ab286a45e63e` |
| `.claude/skills/godot-shaders/references/shading-language.md` | `582b146d8d47b61d80eb8def8e48923d87ef1c7964542f0a12e9e05645c517e9` |
| `.claude/skills/godot-csharp/SKILL.md` | `c6d30a274621c63ab20ce2153807fd173987d1eccd06acfbe6a8a9f26a28c819` |
| `.claude/skills/godot-csharp/references/csharp-setup-and-interop.md` | `086f7bc8549d61ae96190643e9a574f666fd7ed3b04eb18ec10430f89984211d` |
| `.claude/skills/godot-ui-control/SKILL.md` | `9ac89920e74bbf8d939b4d565e350d53b07c9afbe0970345c97d4122f590cfd5` |
| `.claude/skills/godot-ui-control/references/layout-and-theming.md` | `2b5a7d64f00709640385adda215aa3f9a839c2d461c53c886ba9c320723a9f3d` |
| `.claude/third-party/awesome-gamedev-agent-skills/LICENSE` | `6f82dcfeb95a1c0a0452180a3b16f21bf82111780018fed59d95a052b5b75f6c` |
| `.claude/third-party/awesome-gamedev-agent-skills/NOTICE` | `2dcb65b1c5be24e5ba11e937f3996100f4e980d8cf8ac5b30ba1d70dae6c40f8` |

## 升级步骤

1. 重新取上游：把上游仓库克隆到本仓库之外的目录，`git checkout` 到要升级到的提交。
2. 对照校验和：先用 `shasum -a 256` 确认本项目现有的八个文件与上表相同（不同说明本地被改过，先查清原因）；再通读新提交里这三个 skill 的改动，确认没有新增脚本、URL 或要求代理执行外部动作的文字。
3. 用 `cp` 把新版本的文件复制到上面的目录，连同 `LICENSE` 与 `NOTICE`；上游目录里文件有增减时，本项目目录跟着增减。
4. 更新本文件：固定提交、取得日期、校验和表，以及有变化的文件清单。
