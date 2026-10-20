import json
LET = "ABCDEFGHJKLMNOPQRSTUVWXYZ"
def col_name(x):
    # 第 26 列起双字母（与 Coord 一致：假定 A..Z 去 I 共 25 个，超出时双字母）
    if x < 25: return LET[x]
    return LET[x // 25 - 1] + LET[x % 25]
def parse(n):
    i = 0
    while not n[i].isdigit(): i += 1
    letters, num = n[:i], int(n[i:])
    if len(letters) == 1: x = LET.index(letters)
    else: x = (LET.index(letters[0]) + 1) * 25 + LET.index(letters[1])
    return (x, num - 1)
def name(c): return f"{col_name(c[0])}{c[1]+1}"
def load(path):
    m = json.load(open(path, encoding='utf-8'))
    H = m['Height']
    hs = m['Heights']; ss = m['Surfaces']
    # 自检行序：桥必在 W 上
    b = [parse(x) for x in m['Bridges']]
    def cellrow(rows, y, flip): return rows[H - 1 - y] if flip else rows[y]
    flip = None
    for f in (False, True):
        if all(cellrow(ss, y, f)[x] == 'W' for x, y in b): flip = f; break
    assert flip is not None, "无法判定行序"
    m['_flip'] = flip
    m['_h'] = lambda c: int(cellrow(hs, c[1], flip)[c[0]])
    m['_s'] = lambda c: cellrow(ss, c[1], flip)[c[0]]
    m['_obs'] = set(parse(x) for x in m['Obstacles'])
    m['_relic'] = set(parse(x) for x in m['RelicCells'])
    m['_fences'] = [tuple(parse(p) for p in e.split('-')) for e in m['Fences']]
    m['_bridges'] = set(b)
    return m
def boards(m):
    out = []
    for bd in m['Boards']:
        ox, oy = parse(bd['Origin']); w, h = bd['Width'], bd['Height']
        cx, cy = ox + (w - 3) // 2, oy + (h - 3) // 2
        cells = [(x, y) for y in range(oy, oy + h) for x in range(ox, ox + w)]
        core = set((x, y) for x in range(cx, cx + 3) for y in range(cy, cy + 3))
        rim = set(c for c in cells if c[0] in (ox, ox + w - 1) or c[1] in (oy, oy + h - 1))
        middle = [c for c in cells if c not in core and c not in rim]
        out.append(dict(theme=bd['Theme'], w=w, h=h, cells=cells, core=core, rim=rim, middle=middle))
    return out
def adj(a, b): return abs(a[0]-b[0]) + abs(a[1]-b[1]) == 1
def nb(c): x, y = c; return [(x+1,y),(x-1,y),(x,y+1),(x,y-1)]
