# 上游来源与审读记录

2026-10-05 由主会话核对。

## 来源

- 仓库：<https://github.com/gamedev-skills/awesome-gamedev-agent-skills>
- 许可证：Apache-2.0，仓库根有 `LICENSE` 与 `NOTICE`
- 固定提交：`d4b0e35550c55ae70bdfcab4ef5a0e94610438a9`（提交日期 2026-09-27）
- 版本基线：Godot 4.7 + .NET 8（上游 `docs/VERSION-SUPPORT.md`），本项目为 Godot 4.7.2 .NET

取得方式：`git clone` 后 `git checkout d4b0e35550c55ae70bdfcab4ef5a0e94610438a9`，不要用 `npx skills add` 或插件市场（那两种会装 router 与全部 74 个 skill）。

## 要复制的文件与校验和（SHA-256）

| 上游路径 | 目标路径 | SHA-256 |
|---|---|---|
| `skills/godot/godot-shaders/SKILL.md` | `.claude/skills/godot-shaders/SKILL.md` | `15bf46f7b0af586f026c1bb58beaeba2579009b70398f43859b7ab286a45e63e` |
| `skills/godot/godot-shaders/references/shading-language.md` | `.claude/skills/godot-shaders/references/shading-language.md` | `582b146d8d47b61d80eb8def8e48923d87ef1c7964542f0a12e9e05645c517e9` |
| `skills/godot/godot-csharp/SKILL.md` | `.claude/skills/godot-csharp/SKILL.md` | `c6d30a274621c63ab20ce2153807fd173987d1eccd06acfbe6a8a9f26a28c819` |
| `skills/godot/godot-csharp/references/csharp-setup-and-interop.md` | `.claude/skills/godot-csharp/references/csharp-setup-and-interop.md` | `086f7bc8549d61ae96190643e9a574f666fd7ed3b04eb18ec10430f89984211d` |
| `skills/godot/godot-ui-control/SKILL.md` | `.claude/skills/godot-ui-control/SKILL.md` | `9ac89920e74bbf8d939b4d565e350d53b07c9afbe0970345c97d4122f590cfd5` |
| `skills/godot/godot-ui-control/references/layout-and-theming.md` | `.claude/skills/godot-ui-control/references/layout-and-theming.md` | `2b5a7d64f00709640385adda215aa3f9a839c2d461c53c886ba9c320723a9f3d` |
| `LICENSE` | `.claude/third-party/awesome-gamedev-agent-skills/LICENSE` | `6f82dcfeb95a1c0a0452180a3b16f21bf82111780018fed59d95a052b5b75f6c` |
| `NOTICE` | `.claude/third-party/awesome-gamedev-agent-skills/NOTICE` | `2dcb65b1c5be24e5ba11e937f3996100f4e980d8cf8ac5b30ba1d70dae6c40f8` |

三个 skill 目录各只有这两个文件，没有脚本或其他资源。

## 审读结果

- 三个 `SKILL.md` 的 frontmatter 只有 `name` 与 `description`，`name` 与目录名一致。
- 六个文件里没有 URL、没有 shell 下载命令、没有要求代理执行外部动作的文字（`curl` / `wget` / `http` / `token` / `api key` / `rm -rf` 全部零命中）。
- 内容是入门到中级的引擎用法，示例以 GDScript 为主（`godot-csharp` 除外）。
- `SKILL.md` 末尾的 "Related skills" 会提到没有安装的 skill（如 `godot-gdscript`、`godot-animation`），保留原文不改。

## 为什么放在 `.claude/third-party/` 而不是 `docs/`

`docs/` 是发布页的静态资源目录（Cloudflare 部署），放进去会被发布到线上。本仓库是公开仓库，转发 Apache-2.0 内容需要随附许可证与 `NOTICE`，所以放在 `.claude/` 下、skill 扫描目录之外。
