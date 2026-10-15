# -*- coding: utf-8 -*-
"""retire-legacy-rules G4：更正 migration.md §0 (b) 定义与 §1 "迁移去向"栏中已过时的 StartedFlat / FlatMap / LinkMapFixtures.Build 写法，依据 g2/carriers.txt。"""
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
p = Path(sys.argv[1])
raw = p.read_bytes().decode("utf-8")
nl = "\r\n" if "\r\n" in raw else "\n"
md = raw.replace("\r\n", "\n")
head, rest = md.split("## 1. 逐文件清单", 1)
sec1, tail = rest.split("## 2. 汇总", 1)

SF = "同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）"
SF_NEW = "同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）"
B = "手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）"
B_NEW = "`LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）"


def row_sub(text, fname, old, new, count=1):
    lines = text.split("\n")
    idx = [i for i, l in enumerate(lines) if l.startswith(f"| `{fname}` |")]
    assert len(idx) == 1, (fname, idx)
    l = lines[idx[0]]
    assert l.count(old) == count, (fname, old, l.count(old))
    lines[idx[0]] = l.replace(old, new)
    return "\n".join(lines)


# 1) 逐行特例（先于通用替换）
sec1 = row_sub(sec1, "终端对局Tests.cs", SF,
               "12×12 全平地手写链接档图（标识 `link:90012`，整张图登记为一块 12×12 出生棋盘、四人同锁，经 `CreateUnvalidated`；不用 `PlainStarted`）")
sec1 = row_sub(sec1, "对局日志的记录内容Tests.cs", SF,
               SF_NEW + "；`状态栏大数势力缩写` 同类用例用 12×12 整图一块出生棋盘 `link:90012`（同 `终端对局Tests`）")
sec1 = row_sub(sec1, "候选格上限Tests.cs", "手写小链接档图（5 块 5×5，可落子 125 ≤ 150，`CreateUnvalidated`）",
               "手写小链接档图（`LinkMapFixtures.Build(\"link:90005\", …)`，5 块 5×5，可落子 125 ≤ 150，`CreateUnvalidated`）")
sec1 = row_sub(sec1, "前瞻中的下一名对手Tests.cs", SF,
               SF_NEW + "；跨战场名册用例用 `LinkModeFixtures.Started([0, 0, 3, 4])` / `Started([0, 1, 3, 4])`（`link:1`）")
sec1 = row_sub(sec1, "盘面层视图模型带地形Tests.cs", B + "：把 13×13 夹具的地形逐格搬进棋盘 0（坐标平移）",
               "13×13 手写地形夹具（`TestMaps.Synthetic(13)`）原样保留，只把标识改为 `link:90013`、以链接选项经 `CreateUnvalidated` 建局并只取插旗阶段的公开世界（不经 `LinkMapFixtures.Build`、坐标未平移，地形与断言不变）")
sec1 = row_sub(sec1, "出生区编号显示Tests.cs", "手写 6 块棋盘链接档图（`CreateUnvalidated`）",
               "手写 6 块棋盘链接档图（标识 `link:90006`，六个平台登记为六块 5×5 出生棋盘，`CreateUnvalidated`）")
sec1 = row_sub(sec1, "两位数行号贯通Tests.cs", "手写链接档高画布图（出生棋盘放到第 25–32 行，4 人，`CreateUnvalidated`）",
               "手写链接档高画布图（原竖长图换标识 `link:90032`，出生棋盘放到第 25–32 行，4 人，`CreateUnvalidated`）")
sec1 = row_sub(sec1, "棋盘清单Tests.cs", "**删反面段**：`默认棋盘视图模型透传棋盘清单`",
               "**删反面段**：`默认棋盘视图模型透传棋盘清单`（助手 `Started` 改为链接 4 人局，`LinkMapFixtures.Initial()` 换标识 `link:90046`）")
sec1 = row_sub(sec1, "开局信息公开范围Tests.cs", "同区四人 FlatMap 变体、手工信物",
               "`LinkModeFixtures.PlainCreate`（`link:90008`）+ 手工信物（文件中既有的 `link:90001` 为 link-map-terrain 夹具，非本 change 引入）")
sec1 = row_sub(sec1, "默认棋盘上的未发现信物Tests.cs", "`PresentationFixtures.StartedWithZonedRelics` 改为 FlatMap 上按分区写信物",
               "`PresentationFixtures.StartedWithZonedRelics` 改为 `LinkModeFixtures.PlainMap()`（`link:90008`）上按分区写信物")
sec1 = row_sub(sec1, "PresentationFixtures.cs", "改为 FlatMap 上按分区写信物的链接局",
               "改为 `LinkModeFixtures.PlainMap()`（`link:90008`）上按分区写信物的链接局（`CreateUnvalidated` + `RequireLink`）")
sec1 = row_sub(sec1, "MatchFixtures.cs", "`TwoPlayer` 无调用者，直接删",
               "`TwoPlayer` 无调用者，直接删<br>**第 2 组 2.2 已删**：`RandomTurnController` 原样迁出为独立文件 `RandomTurnController.cs`；`tests/` 内 `MatchFixtures` 引用归零（5 个文件的注释措辞改为\"原 9×9 流程夹具\"）")

sec1 = row_sub(sec1, "对局持久化Tests.cs", " + `RestoreUnvalidated` 换成 `MatchFlow.Restore`（FlatMap 过校验）",
               " + 恢复仍走测试口 `RestoreUnvalidated` 的链接路径（`link:90008` 不经校验，不能换公开 `MatchFlow.Restore`）")

# 2) 通用替换
n_sf = sec1.count(SF)
n_b = sec1.count(B)
sec1 = sec1.replace(SF, SF_NEW).replace(B, B_NEW)
assert "StartedFlat" not in sec1 and "FlatMap" not in sec1 and "LinkMapFixtures.Build` +" not in sec1, "残留"

# 3) §1 首注
note = ("\n> **G4 更正（2026-10-15，第 2 组）**：本节\"迁移去向\"栏原写的缺省载体 `StartedFlat([0,0,0,0])` / FlatMap（`link:90003`）与地形类的"
        "`LinkMapFixtures.Build` 为规划口径；第 1 组实际（逐文件扫描见 `g2/carriers.txt`）：\n"
        "> - **主载体** `LinkModeFixtures.PlainStarted` / `PlainCreate` / `PlainMap`：手写、不经校验的平坦链接档图 `link:90008`——棋盘 0 为 8×8（左下角 (2, 2)），其余四块 6×6，"
        "四人同锁棋盘 0、K = 0；原 9×9 局面平移 (+2, +2) 等落在棋盘 0 上（FlatMap 的 6×6 棋盘容不下 9×9 局面）。地形类用例把 `TerrainData` 写在棋盘 0 上（`PlainMap(terrain)`）。"
        "`AiFixtures.Round5`、`PresentationFixtures.StartedWithZonedRelics` 同建在其上。\n"
        "> - **临时标识**（R6 = A 的辅载体，均经 `CreateUnvalidated`）：`link:90005` `候选格上限Tests.小图不限制` 的 5 块 5×5 小图；`link:90006` `出生区编号显示Tests` 六块 5×5 出生棋盘；"
        "`link:90012` `终端对局Tests` / `对局日志的记录内容Tests` 的 12×12 整图一块出生棋盘、四人同锁；`link:90013` `盘面层视图模型带地形Tests` 的 13×13 地形夹具；"
        "`link:90032` `两位数行号贯通Tests` 的竖长图；`link:90046` `棋盘清单Tests.默认棋盘视图模型透传棋盘清单` 的 `LinkMapFixtures.Initial()` 换标识。\n"
        "> - FlatMap（`link:90003`）/ `StartedFlat` 是 link-map-terrain 的既有夹具，迁移测试未用它，仍由既有链接测试使用（如 `链接模式下的决胜一手Tests`），不是死代码。\n"
        f"> - 本次改写：通用短语替换 {n_sf} 处（同区四人缺省载体）+ {n_b} 处（地形类），逐行特例 13 处；§0 (b) 定义同步改。§3 / §4 / §6 未动。\n")
sec1 = note + sec1

# 4) §0 (b) 定义
OLD0 = ("缺省载体见 R6：FlatMap（`link:90003`，K = 0）上**四人同区** `StartedFlat([0,0,0,0])`——单一战场、无配对无链接、整轮 Pass 结算即终局，是旧 9×9\"第 4 大回合起全图\"阶段最近的等价物；"
        "需要新地表 / 特殊地形 / 特殊形状时用手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`）。")
NEW0 = ("缺省载体见 R6（G4 更正为第 1 组实际）：平坦 8×8 棋盘 0 的手写未校验链接档图 `link:90008` 上**四人同区** `LinkModeFixtures.PlainStarted()`（K = 0）——单一战场、无配对无链接、整轮 Pass 结算即终局，是旧 9×9\"第 4 大回合起全图\"阶段最近的等价物；"
        "需要新地表 / 特殊地形时在同图棋盘 0 上写地形（`PlainMap(terrain)`），特殊形状用临时 `link:900xx` 手写图（见第 1 节首注）。")
assert head.count(OLD0) == 1
head = head.replace(OLD0, NEW0)

out = head + "## 1. 逐文件清单" + sec1 + "## 2. 汇总" + tail
p.write_bytes(out.replace("\n", nl).encode("utf-8"))
print("SF", n_sf, "B", n_b)
