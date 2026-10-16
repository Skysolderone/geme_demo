# -*- coding: utf-8 -*-
"""把指定行（注释）里的 9×9 坐标记法按与 migrate.py 相同的映射改写，并在每行末尾不加任何标注（调用方另写一句载体说明）。

用法：python shift_comments.py <文件> <行号,行号,...> [--map=...]
只改形如 A1–J9 的独立记号（前面不是字母 / 数字 / '-'，后面不是字母 / 数字），逐行打印改前改后供人工核对。
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, __file__.rsplit("\\", 1)[0] if "\\" in __file__ else __file__.rsplit("/", 1)[0])
import migrate  # noqa: E402

path, lines = sys.argv[1], [int(x) for x in sys.argv[2].split(",")]
for a in sys.argv[3:]:
    if a.startswith("--map="):
        v = a[len("--map="):]
        migrate.MODE[:] = ["split", "split"] if v == "split" else v.split(",")

raw = open(path, "rb").read()
bom = raw.startswith(b"\xef\xbb\xbf")
text = raw.decode("utf-8-sig")
eol = "\r\n" if "\r\n" in text else "\n"
rows = text.split(eol)
pat = re.compile(r"(?<![A-Za-z0-9\-])([A-HJ][1-9])(?![A-Za-z0-9])")
COLS = migrate.COLS


def diagram(line):
    """棋形图：行首的行号（"//  5 O X ..." / "///  5 ..."）按 y 映射；纯列标行（"//    A B C D"）按 x 映射。"""
    m = re.match(r"^(\s*///?\s+)(\d{1,2})( [.OX!|*?#~^xo-][ .OX!|*?#~^xo-]*.*)$", line)
    if m:
        y = int(m.group(2)) - 1
        ny = migrate.axis(y, migrate.MODE[1])
        label = str(ny + 1)
        width = len(m.group(2))
        prefix = m.group(1)
        if len(label) > width and prefix.endswith(" "):
            prefix = prefix[:-1]
        elif len(label) < width:
            prefix += " "
        return prefix + label + m.group(3)
    m = re.match(r"^(\s*///?\s+)((?:[A-HJ] )*[A-HJ])(\s*)$", line)
    if m:
        letters = m.group(2).split(" ")
        return m.group(1) + " ".join(COLS[migrate.axis(COLS.index(c), migrate.MODE[0])] for c in letters) + m.group(3)
    return None


for n in lines:
    old = rows[n - 1]
    d = diagram(old) if "--diagram" in sys.argv else None
    new = d if d is not None else pat.sub(lambda m: migrate.shift(m.group(1)), old)
    rows[n - 1] = new
    print(f"{n}: {old.strip()}\n  → {new.strip()}")
open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + eol.join(rows).encode("utf-8"))
