# 10-04-release-site

GitHub 上的游戏发布网站与可下载的游戏包（负责人 2026-10-04：介绍页 + 下载包；游戏包本机手动导出，不用 CI）。

## 要求

- GitHub Pages 发布页（`docs/`，从 `main` 分支的 `/docs` 发布）：玩法介绍、截图、下载按钮、安装与操作说明、系统要求、更新记录。纯静态，不引外部资源，手机宽度可读，深浅色都可读。
- 下载按钮指向 GitHub Releases 的固定文件名：`Siege-macos.zip`、`Siege-windows-x64.zip`（`releases/latest/download/…`），以后发新版不用改网页。
- 本机导出脚本：一条命令导出 macOS 与 Windows 两个包到 `build/`（已在 `.gitignore`）。导出预设 `export_presets.cfg` 按仓库约定不入库，由脚本生成。
- Godot 4 的 C# 工程不能导出网页版，网站不提供在线试玩。

## 验收

- [x] 导出的 macOS 包在本机能启动并跑完一次无人值守演示（退出码 0，部件全部来自资源）。
- [x] Windows 包导出成功、结构完整（exe + pck + .NET 运行时目录）；本机无法运行，需在 Windows 机上试。
- [x] 网页本地打开正常：图片都能加载，页面内链接有效，390 宽度下不横向滚动。
- [x] 不改任何游戏代码与规则；`dotnet test` 失败集合与本机基线相同。

## 负责人要手动做的两步（没有 GitHub 令牌，做不了）

1. 仓库 Settings → Pages → Deploy from a branch → `main` / `/docs`。
2. 在 GitHub 网页新建 Release（tag `v0.1.0`），上传 `build/` 里的两个 zip。

约束：同一时间只跑一个 dotnet / Godot；提交用 `git commit -F`，不加 Co-Authored-By。

## 结果（2026-10-04）

- `tools/export-release.sh 0.1.0` → `build/Siege-macos.zip`（126 MB，通用二进制）与 `build/Siege-windows-x64.zip`（74 MB）。
- macOS 包：从 zip 解出后无头跑完自动演示，退出码 0、部件资源 682 件 / 程序生成 0 件；开窗截图正常，绘制调用 728 与开发环境相同。
- 两个坑都写在脚本注释里：Godot 内置临时签名的包在 Apple 芯片上一启动就被杀（改用系统 codesign）；导出失败时 Godot 仍以 0 退出（脚本查 ERROR）。导出 Apple 芯片包要求工程打开 ETC2 / ASTC 导入，`project.godot` 加了一行（工程没有贴图，不影响画面）。
- 网页：本地资源与锚点全部有效；390 宽度下 scrollWidth = 390，不横向滚动。
- `dotnet test` 失败集合与本机基线相同（11 条，与本任务无关）。
- Windows 包只验了结构（exe + pck + .NET 运行时目录），没在 Windows 上运行过。
