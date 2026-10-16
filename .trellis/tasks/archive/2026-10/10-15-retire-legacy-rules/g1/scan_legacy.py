# -*- coding: utf-8 -*-
"""retire-legacy-rules 1.1：按方法名重列受影响测试方法（口径同 migration.md 第 0 节）。

用法：python scan_legacy.py <tests 根目录> [--compare migration.md] [--residual]
- 按 [Fact]/[Theory]（含 SlowFact / PerfTheory 等派生特性）切测试方法；
- 文件内非测试成员（私有助手）若含标记，调用它的测试方法同样记为受影响（同文件内传递闭包）；
- 标记分三档：L = Legacy 建局 / 地图 / 信物助手（MatchFixtures.Started|Create|Map|Relics|TwoPlayer、Round5(、
  StartedWithZonedRelics、Create/RestoreUnvalidated（不含 GameBoard.RestoreUnvalidated）、MatchMode.Legacy、CarryFixtures.Result）；
  M = 只因 MatchMode 写法 / ComputeLink；K = 只引常量（MatchFixtures.P0..P3 / All / Seed）。
RestoreUnvalidated 不单独计（恢复走哪条路径由存档的模式字段决定，建局侧已计）。
--residual：只列仍含 L 标记的方法（第 1 组收尾核对用）。
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

TEST_ATTR = re.compile(r"\[(?:[A-Za-z]*Fact|[A-Za-z]*Theory)\b[^\]]*\]")
MEMBER_SIG = re.compile(
    r"(?:(?:public|private|internal|protected|static|async|override|virtual|sealed|unsafe|new|partial|readonly)\s+)+"
    r"[\w<>\[\],.?() ]+?\s+(\w+)\s*(?:<[^>]*>)?\s*\(")

L_PATTERNS = [
    ("MF.build", re.compile(r"MatchFixtures\s*\.\s*(?:Started|Create|TwoPlayer)\b")),
    ("Standings.Legacy", re.compile(r"FinalStandings\s*\.\s*Compute\s*\(")),
    ("Mode.Legacy", re.compile(r"MatchMode\.Legacy\b")),
]
D_PATTERNS = [
    ("MF.map", re.compile(r"MatchFixtures\s*\.\s*(?:Map|Relics)\b")),
]
UNVALIDATED = re.compile(r"\bCreateUnvalidated\s*\(")


def legacy_unvalidated(body):
    """CreateUnvalidated 调用的实参里没有链接选项（Link / Options）即按 Legacy 建局。"""
    for m in UNVALIDATED.finditer(body):
        depth, j = 1, m.end()
        while j < len(body) and depth:
            depth += {"(": 1, ")": -1}.get(body[j], 0)
            j += 1
        args = body[m.end():j - 1]
        if not re.search(r"Link|PlainOptions|\bOptions\b", args):
            return True
    return False


def l_tags(body):
    t = tags(body, L_PATTERNS)
    if legacy_unvalidated(body):
        t.append("CreateUnvalidated.Legacy")
    return t


M_PATTERNS = [
    ("MatchMode", re.compile(r"\bMatchMode\b")),
    ("ComputeLink", re.compile(r"\bComputeLink\b")),
]
K_PATTERNS = [
    ("MF.const", re.compile(r"MatchFixtures\.(?:P0|P1|P2|P3|All|Seed)\b")),
]


def strip_code(text):
    """去掉注释与字符串字面量内容（保留引号与换行），供括号匹配与标记识别。"""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            i = j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("\n" * text.count("\n", i, j))
            i = j
            continue
        m = re.match(r'(\$*)(@?)("""+)', text[i:])
        if m and (m.group(1) or not m.group(2)) and len(m.group(3)) >= 3:
            q = m.group(3)
            j = text.find(q, i + len(m.group(0)))
            j = n if j < 0 else j + len(q)
            out.append('""' + "\n" * text.count("\n", i, j))
            i = j
            continue
        m = re.match(r'(\$?@|@\$|\$)?"', text[i:])
        if m:
            verbatim = "@" in (m.group(1) or "")
            interp = "$" in (m.group(1) or "")
            j = i + len(m.group(0))
            depth = 0
            buf = []
            while j < n:
                ch = text[j]
                if interp and ch == "{":
                    if text.startswith("{{", j) and depth == 0:
                        j += 2
                        continue
                    depth += 1
                    buf.append(ch)
                elif interp and ch == "}" and depth > 0:
                    depth -= 1
                    buf.append(ch)
                elif depth > 0:
                    buf.append(ch)
                elif verbatim and ch == '"':
                    if text.startswith('""', j):
                        j += 2
                        continue
                    break
                elif not verbatim and ch == "\\":
                    j += 2
                    continue
                elif ch == '"':
                    break
                elif ch == "\n" and not verbatim:
                    break
                j += 1
            # 插值表达式里的代码保留（括号成对），其余字面量丢弃
            out.append('"' + "".join(buf) + '"')
            i = j + 1
            continue
        if c == "'":
            m = re.match(r"'(?:\\.|[^'\\])'", text[i:])
            if m:
                out.append("' '")
                i += len(m.group(0))
                continue
        out.append(c)
        i += 1
    return "".join(out)


def members(code):
    """返回 [(名称, 起点, 终点, 是否测试)]：按特性 + 签名 + 括号匹配切出的成员。"""
    result = []
    pos = 0
    while True:
        m = MEMBER_SIG.search(code, pos)
        if not m:
            break
        name = m.group(1)
        if name in ("if", "for", "foreach", "while", "switch", "using", "return", "lock", "catch", "nameof", "typeof", "new")                 or re.search(r"(?:^|\s)(?:class|record|struct|interface)\s", m.group(0)):
            pos = m.end()
            continue
        # 找签名结束：第一个 '{' 或 '=>'（括号配平后）
        j = m.end() - 1
        depth = 0
        while j < len(code):
            if code[j] == "(":
                depth += 1
            elif code[j] == ")":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        k = j + 1
        rest = code[k:k + 400]
        body_start = None
        mm = re.match(r"\s*(?:where[^{=;]*)?(\{|=>|;)", rest)
        if not mm:
            pos = m.end()
            continue
        tok = mm.group(1)
        if tok == ";":
            pos = k + mm.end()
            continue
        body_start = k + mm.start(1)
        if tok == "{":
            d = 0
            e = body_start
            while e < len(code):
                if code[e] == "{":
                    d += 1
                elif code[e] == "}":
                    d -= 1
                    if d == 0:
                        break
                e += 1
            end = e + 1
        else:
            d = 0
            e = body_start
            while e < len(code):
                ch = code[e]
                if ch in "({[":
                    d += 1
                elif ch in ")}]":
                    d -= 1
                elif ch == ";" and d == 0:
                    break
                e += 1
            end = e + 1
        # 特性区：签名前到上一个成员 / 分号 / 大括号之间
        head_start = max(code.rfind(";", 0, m.start()), code.rfind("}", 0, m.start()), code.rfind("{", 0, m.start())) + 1
        head = code[head_start:m.start()]
        is_test = bool(TEST_ATTR.search(head))
        result.append((name, m.start(), end, is_test, head))
        pos = end
    return result


def tags(text, patterns):
    return sorted({t for t, p in patterns if p.search(text)})


def class_at(code, pos):
    """pos 处最内层的类型声明名（按大括号栈判定，嵌套类型不会误归属）。"""
    stack = []
    pending = None
    decl = re.compile(r"\b(?:class|record|struct|interface)\s+(\w+)")
    i = 0
    last = 0
    for m in re.finditer(r"[{};]", code[:pos]):
        seg = code[last:m.start()]
        dm = list(decl.finditer(seg))
        if dm:
            pending = dm[-1].group(1)
        ch = m.group(0)
        if ch == "{":
            stack.append(pending)
            pending = None
        elif ch == "}":
            if stack:
                stack.pop()
            pending = None
        else:
            pending = None if not dm else pending
        last = m.end()
    for name in reversed(stack):
        if name:
            return name
    return None


def parse_file(path):
    raw = open(path, "rb").read().decode("utf-8-sig")
    code = strip_code(raw)
    usings = re.findall(r"using static Siege\.Core\.Tests\.(?:\w+\.)*(\w+)\s*;", code)
    entries = []
    for name, s, e, is_test, head in members(code):
        entries.append({"name": name, "cls_name": class_at(code, s), "test": is_test, "body": code[s:e],
                        "theory": "Theory" in head, "rows": len(re.findall(r"\[InlineData\b", head))})
    return {"code": code, "usings": usings, "entries": entries}


def calls(body, own_class, usings, helpers):
    """body 里对 L 助手的调用：同类 / using static 类的裸调用，或 类名.助手( 的限定调用。"""
    hits = []
    for (cls, h) in helpers:
        if cls is None:
            continue
        if re.search(r"(?<![\w])" + re.escape(cls) + r"\s*\.\s*" + re.escape(h) + r"\s*\(", body):
            hits.append(f"helper:{cls}.{h}" if cls != own_class else "helper:" + h)
        elif (cls == own_class or cls in usings) and re.search(r"(?<![\w.])" + re.escape(h) + r"\s*\(", body):
            hits.append("helper:" + (h if cls == own_class else f"{cls}.{h}"))
    return hits


def global_helpers(parsed, tagger=None):
    """全部测试源文件里含 L 标记（或调用 L 助手）的非测试成员：{(类名, 成员名)} 的传递闭包。"""
    tagger = tagger or l_tags
    helpers = set()
    changed = True
    while changed:
        changed = False
        for pf in parsed.values():
            for ent in pf["entries"]:
                key = (ent["cls_name"], ent["name"])
                if ent["test"] or key in helpers:
                    continue
                if tagger(ent["body"]) or calls(ent["body"], ent["cls_name"], pf["usings"],
                                                          [k for k in helpers if k != key]):
                    helpers.add(key)
                    changed = True
    return helpers


def scan_parsed(pf, helpers, dhelpers=frozenset()):
    code = pf["code"]
    tests = []
    for ent in pf["entries"]:
        if not ent["test"]:
            continue
        body = ent["body"]
        lt = l_tags(body) + calls(body, ent["cls_name"], pf["usings"], helpers)
        dt = tags(body, D_PATTERNS) + calls(body, ent["cls_name"], pf["usings"], dhelpers)
        mt = tags(body, M_PATTERNS)
        kt = tags(body, K_PATTERNS)
        cls = "L" if lt else ("D" if dt else ("M" if mt else ("K" if kt else "-")))
        tests.append({"name": ent["name"], "cls": cls, "L": lt + dt, "M": mt, "K": kt,
                      "theory": ent["theory"], "rows": ent["rows"]})
    file_level_k = bool(re.search(r"MatchFixtures\.(?:P0|P1|P2|P3|All|Seed)\b", code))
    file_level_m = bool(re.search(r"\bMatchMode\b|\bComputeLink\b", code))
    return tests, file_level_k, file_level_m


_CACHE = {}


def scan_file(path):
    """单文件查看用（全局助手按同目录树计算一次）。"""
    root = os.path.dirname(os.path.dirname(os.path.abspath(path))) if os.path.basename(os.path.dirname(path)) != "Siege.Core.Tests" else os.path.dirname(os.path.abspath(path))
    if root not in _CACHE:
        parsed = {p: parse_file(p) for p in all_files(root)}
        _CACHE[root] = (parsed, global_helpers(parsed), global_helpers(parsed, lambda b: tags(b, D_PATTERNS)))
    parsed, helpers, dhelpers = _CACHE[root]
    return scan_parsed(parsed[os.path.abspath(path)] if os.path.abspath(path) in parsed else parse_file(path), helpers, dhelpers)


def all_files(root):
    files = []
    for d, dirs, fs in os.walk(root):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
        for f in fs:
            if f.endswith(".cs"):
                files.append(os.path.abspath(os.path.join(d, f)))
    files.sort()
    return files


def main():
    root = sys.argv[1]
    residual = "--residual" in sys.argv
    files = all_files(root)
    parsed = {p: parse_file(p) for p in files}
    helpers = global_helpers(parsed)
    dhelpers = global_helpers(parsed, lambda b: tags(b, D_PATTERNS))
    total_l = total_m = total_k = total_d = 0
    nfiles = 0
    for p in files:
        rel = os.path.relpath(p, root).replace("\\", "/")
        tests, fk, fm = scan_parsed(parsed[p], helpers, dhelpers)
        aff = [t for t in tests if t["cls"] != "-"]
        if residual:
            aff = [t for t in tests if t["cls"] == "L"]
            if not aff:
                continue
        elif not aff and not fk and not fm:
            continue
        nfiles += 1
        nl = sum(1 for t in tests if t["cls"] == "L")
        nm = sum(1 for t in tests if t["cls"] == "M")
        nk = sum(1 for t in tests if t["cls"] == "K")
        nd = sum(1 for t in tests if t["cls"] == "D")
        total_d += nd
        total_l += nl
        total_m += nm
        total_k += nk
        theories = sum(1 for t in tests if t["theory"])
        rows = sum(t["rows"] for t in tests)
        print(f"## {rel}  total={len(tests)} theory={theories} rows={rows}  L={nl} D={nd} M={nm} K={nk}"
              + ("  [file:K]" if fk else "") + ("  [file:M]" if fm else ""))
        for t in sorted(aff, key=lambda x: x["name"]):
            extra = ",".join(t["L"] + t["M"] + t["K"])
            print(f"  {t['cls']} {t['name']}  ({extra})")
    print(f"\n# files={nfiles}  L-methods={total_l}  D-methods={total_d}  M-methods={total_m}  K-methods={total_k}")


if __name__ == "__main__":
    main()
