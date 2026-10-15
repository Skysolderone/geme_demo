# -*- coding: utf-8 -*-
"""retire-legacy-rules G4：对 migration.md 第 1 节每行，统计对应测试文件（第 1 组迁移后、第 2 组删除后）实际使用的链接载体。

用法：python carriers.py <tests 根目录> <migration.md>
"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
root, md_p = Path(sys.argv[1]), sys.argv[2]
md = open(md_p, encoding="utf-8").read()
sec1 = md.split("## 1. 逐文件清单")[1].split("## 2. 汇总")[0]
PATS = {
    "Plain": r"\bPlain(Started|Create|Map)\b|\bRound5\(|StartedWithZonedRelics",
    "StartedFlat": r"\bStartedFlat\(|\bFlatMap\b",
    "Started": r"LinkModeFixtures\.Started\(|\bStarted\(\[",
    "Build": r"LinkMapFixtures\.Build\(",
    "link:1": r"\bLinkModeFixtures\.Map\b|\bLinkModeFixtures\.Create\(|\"link:1\"",
    "NineByNine": r"TestMaps\.NineByNine\(",
}
section = None
for line in sec1.splitlines():
    h = re.match(r"### (\S+)", line)
    if h:
        section = h.group(1)
        continue
    m = re.match(r"\| `([^`]+)` \| (\w+) \|", line)
    if not m or m.group(2) == "FIX":
        continue
    f = m.group(1)
    hits = list(root.glob(f"{section}/{f}")) or list(root.rglob(f))
    if not hits:
        print(f"{section}/{f}\t<文件不存在>")
        continue
    t = hits[0].read_text(encoding="utf-8")
    used = [k for k, p in PATS.items() if re.search(p, t)]
    ids = sorted(set(re.findall(r"link:900\d+", t)))
    cell = line.split("|")[6]
    flags = ("SF" if "StartedFlat" in cell or "FlatMap" in cell else "") + ("B" if "LinkMapFixtures.Build" in cell else "")
    print(f"{section}/{f}\tlisted={flags or '-'}\tused={','.join(used) or '-'}\tids={','.join(ids) or '-'}")
