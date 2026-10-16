# -*- coding: utf-8 -*-
"""retire-legacy-rules 1.1：脚本扫描结果（scan_legacy.py 输出）与 migration.md 第 1 / 3 / 4 节逐文件、逐名比对。

用法：python compare_list.py <scan 输出> <migration.md>
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

scan_path, md_path = sys.argv[1], sys.argv[2]

scan = {}
cur = None
for line in open(scan_path, encoding="utf-8"):
    m = re.match(r"## (\S+)\s+total=(\d+) theory=(\d+) rows=(\d+)\s+L=(\d+) D=(\d+) M=(\d+) K=(\d+)", line)
    if m:
        cur = m.group(1)
        scan[cur] = {"total": int(m.group(2)), "theory": int(m.group(3)), "rows": int(m.group(4)),
                     "L": int(m.group(5)), "D": int(m.group(6)), "M": int(m.group(7)), "K": int(m.group(8)), "methods": {}}
        continue
    m = re.match(r"  ([LDMK]) (\S+)", line)
    if m and cur:
        scan[cur]["methods"][m.group(2)] = m.group(1)

md = open(md_path, encoding="utf-8").read()
sec1 = md.split("## 1. 逐文件清单")[1].split("## 2. 汇总")[0]
listed = {}
section = None
for line in sec1.splitlines():
    h = re.match(r"### (\S+)", line)
    if h:
        section = h.group(1)
        continue
    m = re.match(r"\| `([^`]+)` \| (\w+) \| [^|]* \| ([^|]*) \| ([^|]*) \|", line)
    if not m:
        continue
    fname, src, counts, cls = m.group(1), m.group(2), m.group(3).strip(), m.group(4).strip()
    if src == "FIX":
        continue
    path = fname if section == "夹具" else f"{section}/{fname}"
    cm = re.match(r"(\d+)(?:（Theory (\d+)，(\d+) 行）)?\s*/\s*(\d+|—)", counts)
    total = int(cm.group(1)) if cm else None
    leg = None if not cm or cm.group(4) == "—" else int(cm.group(4))
    cls_counts = {k: int(v) for k, v in re.findall(r"([abcjdem])×(\d+)", cls)}
    listed[path] = {"total": total, "legacy": leg, "src": src, "cls": cls_counts,
                    "theory": int(cm.group(2)) if cm and cm.group(2) else 0, "rows": int(cm.group(3)) if cm and cm.group(3) else 0}

print("# 逐文件比对（清单 第 1 节 vs 脚本）\n")
print("| 文件 | 来源 | 清单 总/Legacy | 脚本 总/L/D/M/K | 清单分类 | 结论 |")
print("|---|---|---|---|---|---|")
issues = 0
for path in sorted(set(listed) | set(scan)):
    l = listed.get(path)
    s = scan.get(path)
    if l is None:
        affected = s["L"] + s["M"] + s["K"]
        if path.endswith("Fixtures.cs"):
            print(f"| {path} | FIX | （夹具，清单「夹具」小节） | {s['total']}/{s['L']}/{s['D']}/{s['M']}/{s['K']} | — | 夹具 |")
            continue
        print(f"| {path} | — | 未列 | {s['total']}/{s['L']}/{s['D']}/{s['M']}/{s['K']} | — | 脚本命中而清单未列 |")
        issues += 1
        continue
    if s is None and l["src"] == "EXT":
        print(f"| {path} | EXT | {l['total']}/— | 未命中 | — | 一致（不迁，只读旧日志夹具） |")
        continue
    if s is None:
        print(f"| {path} | {l['src']} | {l['total']}/{l['legacy']} | 未命中 | {l['cls']} | 清单列出而脚本未命中 |")
        issues += 1
        continue
    notes = []
    if l["total"] != s["total"]:
        notes.append(f"总数 {l['total']}≠{s['total']}")
    if l["theory"] and (l["theory"] != s["theory"] or l["rows"] != s["rows"]):
        notes.append(f"Theory {l['theory']}/{l['rows']}≠{s['theory']}/{s['rows']}")
    if l["legacy"] is not None and l["legacy"] != s["L"] + s["D"]:
        notes.append(f"Legacy {l['legacy']}≠L+D {s['L'] + s['D']}")
    # 分类口径（人工判读）：a / b / c / j / d / m（删反面段或改引手工信物记录）共同覆盖脚本的 L + D；类别之和只作提示，不判出入。
    covered = sum(l["cls"].get(k, 0) for k in "abcjdm")
    if covered < s["L"] + s["D"]:
        notes.append(f"分类合计 {covered} < L+D {s['L'] + s['D']}")
    concl = "一致" if not notes else "；".join(notes)
    if notes:
        issues += 1
    print(f"| {path} | {l['src']} | {l['total']}/{l['legacy'] if l['legacy'] is not None else '—'} | "
          f"{s['total']}/{s['L']}/{s['D']}/{s['M']}/{s['K']} | {' '.join(f'{k}×{v}' for k, v in l['cls'].items())} | {concl} |")

print(f"\n有出入文件数：{issues}\n")

print("# 第 3 节（(c) / (j) / L18）逐名核对\n")
sec3 = md.split("## 3. (c) 删除与 (j) 合并逐名表")[1].split("## 4.")[0]
bad = 0
n = 0
for m in re.finditer(r"\| (\d+) \| `([^`]+)` \| `([^`]+)` \| ([^|]+) \|", sec3):
    n += 1
    path, method, kind = m.group(2), m.group(3), m.group(4).strip()
    s = scan.get(path)
    got = s["methods"].get(method) if s else None
    if kind.startswith("c（L18）"):
        status = "L18（与 Legacy 无关，第 4 组）"
    elif got is None:
        status = "**脚本未判为受影响**"
        bad += 1
    else:
        status = f"脚本 {got}"
    print(f"- #{m.group(1)} {path}.{method} [{kind}] → {status}")
print(f"\n第 3 节条目 {n}，未命中 {bad}\n")

print("# 第 4 节（改名 / 迁出）源方法存在性\n")
sec4 = md.split("## 4. 改名与迁出表")[1].split("## 5.")[0]
for m in re.finditer(r"\| `([^`]+)` \| `([^`]+)` \|", sec4):
    src = m.group(1)
    mm = re.match(r"(.+Tests)\.(\S+)$", src)
    if not mm:
        print(f"- {src} → （文件级改名）")
        continue
    cls, meth = mm.groups()
    hits = [p for p in scan if p.endswith("/" + cls + ".cs") and meth in scan[p]["methods"]]
    print(f"- {src} → {'脚本命中 ' + scan[hits[0]]['methods'][meth] if hits else '**脚本未命中**'}")
