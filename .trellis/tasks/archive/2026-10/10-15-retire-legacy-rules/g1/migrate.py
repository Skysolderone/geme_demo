# -*- coding: utf-8 -*-
"""retire-legacy-rules 第 1 组：把指定测试方法（及指定助手）从 9×9 Legacy 夹具机械改写到平坦 8×8 链接载体。

用法：python migrate.py <测试文件> <成员名>[,<成员名>...] [--dry]
在这些成员体内：
- MatchFixtures.Started( / Create( → LinkModeFixtures.PlainStarted( / PlainCreate(
- MatchFixtures.P0..P3 / All / Seed → TestMaps.*；MatchFixtures.Map( / Relics( → TestMaps.NineByNine( / TestMaps.Relics(（地图数据层）
- 恰为两字符的坐标字面量 "E5" → 平移 (+2, +2) 后的 "G7"；J 列 / 第 9 行（平移后出棋盘 0）原样保留并列出，需人工处理
- 列出含坐标的长字符串（文本快照、决策串等），需人工处理
只做机械替换，不改任何断言含义；输出逐成员的替换计数，供 migration.md 第 6 节记表。
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

COLS = "ABCDEFGHJKLMNOPQRSTUVWXYZ"


def mask(text):
    """与原文等长：注释与字符串内容替换为空格（保留引号与换行），供括号匹配与成员定位。"""
    out = list(text)
    i, n = 0, len(text)

    def blank(a, b):
        for k in range(a, b):
            if out[k] != "\n":
                out[k] = " "

    while i < n:
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            blank(i, j)
            i = j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            blank(i, j)
            i = j
            continue
        m = re.match(r'\$*"""', text[i:])
        if m:
            j = text.find('"""', i + len(m.group(0)))
            j = n if j < 0 else j + 3
            blank(i + len(m.group(0)), j - 3)
            i = j
            continue
        m = re.match(r'(\$?@|@\$|\$)?"', text[i:])
        if m:
            verbatim = "@" in (m.group(1) or "")
            interp = "$" in (m.group(1) or "")
            j = i + len(m.group(0))
            depth = 0
            while j < n:
                ch = text[j]
                if interp and ch == "{":
                    if text.startswith("{{", j) and depth == 0:
                        j += 2
                        continue
                    depth += 1
                elif interp and ch == "}" and depth > 0:
                    depth -= 1
                elif depth > 0:
                    pass
                elif verbatim and ch == '"':
                    if text.startswith('""', j):
                        j += 2
                        continue
                    break
                elif not verbatim and ch == "\\":
                    j += 2
                    continue
                elif ch == '"' or (ch == "\n" and not verbatim):
                    break
                j += 1
            if not interp:
                blank(i + len(m.group(0)), j)
            i = j + 1
            continue
        if text[i] == "'":
            m = re.match(r"'(?:\\.|[^'\\])'", text[i:])
            if m:
                blank(i + 1, i + len(m.group(0)) - 1)
                i += len(m.group(0))
                continue
        i += 1
    return "".join(out)


SIG = re.compile(
    r"(?:(?:public|private|internal|protected|static|async|override|virtual|sealed|unsafe|new|partial|readonly)\s+)+"
    r"[\w<>\[\],.?() ]+?\s+(\w+)\s*(?:<[^>]*>)?\s*\(")


def member_spans(masked):
    spans = {}
    pos = 0
    while True:
        m = SIG.search(masked, pos)
        if not m:
            break
        if re.search(r"(?:^|\s)(?:class|record|struct|interface)\s", m.group(0)):
            pos = m.end()
            continue
        j = m.end() - 1
        depth = 0
        while j < len(masked):
            if masked[j] == "(":
                depth += 1
            elif masked[j] == ")":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        mm = re.match(r"\s*(?:where[^{=;]*)?(\{|=>|;)", masked[j + 1:j + 400])
        if not mm or mm.group(1) == ";":
            pos = m.end()
            continue
        b = j + 1 + mm.start(1)
        if mm.group(1) == "{":
            d, e = 0, b
            while e < len(masked):
                if masked[e] == "{":
                    d += 1
                elif masked[e] == "}":
                    d -= 1
                    if d == 0:
                        break
                e += 1
        else:
            d, e = 0, b
            while e < len(masked):
                ch = masked[e]
                if ch in "({[":
                    d += 1
                elif ch in ")}]":
                    d -= 1
                elif ch == ";" and d == 0:
                    break
                e += 1
        spans.setdefault(m.group(1), []).append((m.start(), e + 1))
        pos = e + 1
    return spans


MODE = ["2", "2"]
UNTIL = []   # --until=<锚点>：只改写到锚点为止（之后是规则层段落时用）
FROM = []   # --from=<锚点>：只改写成员体内锚点之后的部分（同一方法里前半段是规则层、后半段才建局时用）


def axis(v, mode):
    """一个坐标轴的平移：数字为整体平移量；split = 西 / 南半（0–3）+2、东 / 北半（5–8）+1（四角分别贴到棋盘 0 的四角），4 不允许。"""
    if mode == "split":
        if v == 4:
            raise ValueError("split 映射不接受中线坐标（第 5 行 / E 列）")
        return v + 2 if v <= 3 else v + 1
    if mode.startswith("o:"):
        # 逐值平移量：o:<9×9 坐标 0 的平移>/<1 的平移>/…/<8 的平移>（用 / 分隔，用例自己保证用到的值不相撞）
        return v + int(mode[2:].split("/")[v])
    return v + int(mode)


def shift(cell):
    col, row = cell[0], int(cell[1:])
    x = COLS.index(col)
    nx, ny = axis(x, MODE[0]), axis(row - 1, MODE[1])
    if not (2 <= nx <= 9 and 2 <= ny <= 9):
        raise ValueError(f"{cell} 映射到 ({nx},{ny}) 落在棋盘 0 之外")
    return f"{COLS[nx]}{ny + 1}"


def rewrite(body, report):
    reps = [
        (r"MatchFixtures\s*\.\s*Started\(", "LinkModeFixtures.PlainStarted("),
        (r"MatchFixtures\s*\.\s*Create\(", "LinkModeFixtures.PlainCreate("),
        (r"MatchFixtures\.(P0|P1|P2|P3|All|Seed)\b", r"TestMaps.\1"),
        (r"\bRound5Legacy\(", "Round5("),
        (r"\bRingE5G5\b", "RingF7H7"),
    ]
    for pat, rep in reps:
        body, k = re.subn(pat, rep, body)
        if k:
            report.append(f"{pat} ×{k}")
    outside = []

    def sub(m):
        cell = m.group(1)
        try:
            return f'"{shift(cell)}"'
        except ValueError:
            outside.append(cell)
            return m.group(0)

    body, k = re.subn(r'"([A-HJ][1-9])"', sub, body)
    if k:
        report.append(f"坐标 ×{k - len(outside)}")

    # 候选键 / 决策串里的"坐标:类型"（如 "G8:Basic"、"C3:Basic,D4:Basic"）
    def key_sub(m):
        lit = m.group(0)
        try:
            return re.sub(r"(?<![A-Za-z0-9])([A-HJ][1-9])(?=:[A-Z])", lambda t: shift(t.group(1)), lit)
        except ValueError:
            outside.append(lit)
            return lit

    body, kk = re.subn(r'"(?:[A-HJ][1-9]:[A-Za-z]+[,;| ]?)+"', key_sub, body)
    if kk:
        report.append(f"候选键 ×{kk}")
    if outside:
        report.append(f"**映射失败（未改）**：{', '.join(outside)}")
    long = sorted(set(re.findall(r'"[^"\n]*\b[A-HJ][1-9]\b[^"\n]*"', body)) - {f'"{c}"' for c in re.findall(r'"([A-HJ][1-9])"', body)})
    long = [s for s in long if len(s) > 4]
    if long:
        report.append("**长字符串含坐标（人工）**：" + " ".join(long[:8]))
    return body


def auto_axis(values, label):
    """一轴的自动映射：最大值 ≤ 6 → +2；最小值 ≥ 2 → +1；否则在最长的一段未用值中间切开（以下 +2、以上 +1），未用段短于 3 时提示。"""
    if not values:
        return "2"
    if max(values) <= 6:
        return "2"
    if min(values) >= 2:
        return "1"
    unused = [v for v in range(1, 8) if v not in values]
    runs, run = [], []
    for v in unused:
        if run and v != run[-1] + 1:
            runs.append(run)
            run = []
        run.append(v)
    if run:
        runs.append(run)
    if not runs:
        raise SystemExit(f"{label} 轴 0–8 几乎全用，无法自动映射：{values}")
    best = max(runs, key=len)
    cut = best[len(best) // 2]
    if len(best) < 3:
        print(f"!! {label} 轴未用段只有 {len(best)} 格（{best}），切开后两侧棋子会靠近，需人工核对")
    return "o:" + "/".join("2" if v < cut else "1" for v in range(9))


def main():
    path, names = sys.argv[1], sys.argv[2].split(",")
    dry = "--dry" in sys.argv
    for a in sys.argv[3:]:
        if a.startswith("--from="):
            FROM.append(a[len("--from="):])
        if a.startswith("--until="):
            UNTIL.append(a[len("--until="):])
        if a.startswith("--map="):
            v = a[len("--map="):]
            MODE[:] = ["split", "split"] if v == "split" else v.split(",")
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    bom = raw.startswith(b"\xef\xbb\xbf")
    masked = mask(text)
    spans = member_spans(masked)
    if "auto" in MODE:
        # 自动选映射：按全部所选成员里出现的坐标逐轴挑"保住盘边 / 内部身份"的平移（9×9 的 0 / 8 是边，平移后棋盘 0 的边是 2 / 9）。
        used = []
        for name in names:
            for (a, b) in spans.get(name, []):
                body = text[a:b]
                cut = body.find(FROM[0]) if FROM else 0
                stop = body.find(UNTIL[0], max(cut, 0)) if UNTIL else -1
                used += re.findall(r'"([A-HJ][1-9])"', body[max(cut, 0):(len(body) if stop < 0 else stop)])
        xs = sorted({COLS.index(c[0]) for c in used})
        ys = sorted({int(c[1:]) - 1 for c in used})
        MODE[:] = [auto_axis(xs, "x"), auto_axis(ys, "y")]
    print(f"映射：x {MODE[0]}，y {MODE[1]}")
    edits = []
    for name in names:
        if name not in spans:
            print(f"!! 找不到成员 {name}")
            continue
        for (a, b) in spans[name]:
            report = []
            body = text[a:b]
            cut = body.find(FROM[0]) if FROM else 0
            if cut < 0:
                print(f"!! {name} 里找不到起点 {FROM[0]!r}")
                continue
            stop = body.find(UNTIL[0], cut) if UNTIL else -1
            stop = len(body) if stop < 0 else stop
            new = body[:cut] + rewrite(body[cut:stop], report) + body[stop:]
            print(f"- {name}: {'；'.join(report) if report else '无替换'}")
            edits.append((a, b, new))
    for a, b, new in sorted(edits, reverse=True):
        text = text[:a] + new + text[b:]
    if not dry:
        open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))


if __name__ == "__main__":
    main()
