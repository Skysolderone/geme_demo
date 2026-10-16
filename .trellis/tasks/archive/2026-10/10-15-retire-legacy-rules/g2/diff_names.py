# -*- coding: utf-8 -*-
"""retire-legacy-rules 2.1：删除前后测试名清单做差集，与 migration.md 第 3 节 (c)/(j) 名单（L18 两条除外）逐名比对。

用法：python diff_names.py <before 清单> <after 清单> <migration.md>
"""
import re
import sys
from collections import Counter

sys.stdout.reconfigure(encoding="utf-8")
before_p, after_p, md_p = sys.argv[1:4]


def load(p):
    return Counter(l.strip() for l in open(p, encoding="utf-8") if l.startswith("    Siege."))


before, after = load(before_p), load(after_p)
# 清单第 4 节改名：MatchSetup/对局模式Tests → MatchSetup/建局约束Tests（保留两条）。改名后的名字先映射回旧名再做差集。
RENAME = {"Siege.Core.Tests.MatchSetup.建局约束Tests.": "Siege.Core.Tests.MatchSetup.对局模式Tests."}
renamed = []
for new, old in RENAME.items():
    for x in list(after):
        if x.startswith(new):
            renamed.append((old + x[len(new):], x))
            after[old + x[len(new):]] += after.pop(x)
removed = before - after
added = after - before
md = open(md_p, encoding="utf-8").read()
sec = md.split("## 3.")[1].split("## 4.")[0]
rows = re.findall(r"\| (\d+) \| `([^`]+)` \| `([^`]+)` \| (c|j) \|", sec)   # L18 两条为 "c（L18）"，属第 4 组
expected = {}
for n, f, m, c in rows:
    d, cls = f.split("/")[0], f.split("/")[-1][:-3]
    expected[f"Siege.Core.Tests.{d}.{cls}.{m}"] = (n, c)

def base(name):
    return name.split("(")[0]

removed_methods = Counter(base(x) for x in removed.elements())
print(f"删除前 {sum(before.values())} 行，删除后 {sum(after.values())} 行，减少 {sum(before.values()) - sum(after.values())}")
print(f"差集（删除前有、删除后无）{sum(removed.values())} 行 / {len(removed_methods)} 个方法；新增 {sum(added.values())} 行")
print(f"清单第 3 节 (c)/(j)（不含 L18）{len(expected)} 条："
      f"(c) {sum(1 for v in expected.values() if v[1] == 'c')}、(j) {sum(1 for v in expected.values() if v[1] == 'j')}")
print()
for name, (n, c) in sorted(expected.items(), key=lambda kv: int(kv[1][0])):
    rows_ = removed_methods.get(name, 0)
    print(f"#{n:>2} {c} {'OK ' if rows_ else 'MISS'} 行数 {rows_}  {name}")
print()
extra = sorted(set(removed_methods) - set(expected))
missing = sorted(set(expected) - set(removed_methods))
print("差集中不在清单的:", extra)
print("清单中未出现在差集的:", missing)
print("删除后新增的名字（改名映射后）:", sorted(added))
print("按第 4 节改名映射的:", renamed)
theory = {k: v for k, v in removed_methods.items() if v > 1 or any("(" in x for x in removed if base(x) == k)}
print("Theory 行（差集中带参数的）:", theory)
print("结论:", "逐名一致" if not extra and not missing and not added else "有出入")
