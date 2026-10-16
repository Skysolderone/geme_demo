# -*- coding: utf-8 -*-
"""retire-legacy-rules 2.1：按 migration.md 第 3 节 (c)/(j) 名单删除测试方法（整文件删除的 4 个文件除外，由调用方 git rm）。

方法块 = 紧邻方法之上的连续 /// 注释、[Fact]/[Theory]/[InlineData] 等特性行 + 方法签名 + 配对花括号的方法体，连同其前一空行。
用法：python delete_methods.py <tests 根目录> <migration.md> [--dry]
"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
root, md_path = Path(sys.argv[1]), sys.argv[2]
dry = "--dry" in sys.argv
WHOLE = {"EliminationEndgame/三类终局条件Tests.cs", "MatchSetup/匿名同时插旗Tests.cs",
         "TurnSequence/构筑保护期的落子限制Tests.cs", "TurnSequence/第4大回合的全图解禁Tests.cs"}

md = open(md_path, encoding="utf-8").read()
sec = md.split("## 3.")[1].split("## 4.")[0]
rows = re.findall(r"\| (\d+) \| `([^`]+)` \| `([^`]+)` \| (c|j) \|", sec)   # L18 两条（"c（L18）"）不匹配，留第 4 组
targets = {}
for _, f, m, _ in rows:
    if f in WHOLE:
        continue
    targets.setdefault(f, []).append(m)

total = 0
for f, methods in targets.items():
    p = root / f
    lines = p.read_text(encoding="utf-8").split("\n")
    for m in methods:
        sig = [i for i, l in enumerate(lines) if re.match(rf"\s*public (?:async Task|void) {re.escape(m)}\(", l)]
        assert len(sig) == 1, (f, m, sig)
        s = sig[0]
        start = s
        while start > 0 and re.match(r"\s*(///|\[)", lines[start - 1]):
            start -= 1
        depth, end, opened = 0, s, False
        for j in range(s, len(lines)):
            depth += lines[j].count("{") - lines[j].count("}")
            if "{" in lines[j]:
                opened = True
            if opened and depth == 0:
                end = j
                break
        if start > 0 and lines[start - 1].strip() == "":
            start -= 1
        print(f"{f}: {m}  删 {start + 1}-{end + 1} 行（{end - start + 1} 行）")
        del lines[start:end + 1]
        total += 1
    if not dry:
        p.write_text("\n".join(lines), encoding="utf-8")
print("删除方法数", total)
