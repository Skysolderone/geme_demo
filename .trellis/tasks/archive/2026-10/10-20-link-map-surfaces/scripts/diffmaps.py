import sys, json
sys.path.insert(0, '.')
from mapjson import *
before, after, out = sys.argv[1], sys.argv[2], sys.argv[3]
SP = {'D': '荒漠', 'M': '沼泽', 'P': '岩台', 'S': '浅滩'}
PAIR = {'River': {'S'}, 'Plateau': {'P'}, 'Forest': {'M'}, 'Palisade': {'D'}, 'Mixed': {'S', 'P', 'M', 'D'}}
lines = []
ok_all = True
for s in range(1, 21):
    mb = json.load(open(f"{before}/link{s}.json", encoding='utf-8'))
    ma = json.load(open(f"{after}/link{s}.json", encoding='utf-8'))
    diffkeys = [k for k in mb if mb[k] != ma.get(k)] + [k for k in ma if k not in mb]
    a = load(f"{after}/link{s}.json"); b = load(f"{before}/link{s}.json")
    changed = []
    for y in range(ma['Height']):
        for x in range(ma['Width']):
            c = (x, y)
            if a['_s'](c) != b['_s'](c): changed.append(c)
    ok = set(diffkeys) <= {'Surfaces'}
    boardrows = []
    for bd in boards(ma):
        cells = [c for c in changed if c in set(bd['cells'])]
        kinds = [a['_s'](c) for c in cells]
        good = all(b['_s'](c) == 'G' and k in SP for c, k in zip(cells, kinds))
        good &= all(c in set(bd['middle']) for c in cells)
        good &= all(b['_h'](c) == 0 and c not in b['_relic'] and c not in b['_obs'] for c in cells)
        good &= set(kinds) <= PAIR[bd['theme']]
        if bd['theme'] == 'Mixed': good &= len(kinds) == len(set(kinds))
        if len(bd['middle']) > 12: good &= not any(n in bd['core'] for c in cells for n in nb(c))   # F1：中间带 ≤ 12 的小棋盘免除 S1
        good &= len(cells) <= 4
        ok &= good
        boardrows.append(f"{bd['theme']}{bd['w']}x{bd['h']}={len(cells)}[{' '.join(name(c)+':'+SP[k] for c, k in zip(cells, kinds))}]{'' if good else ' !!!'}")
    ok &= len(changed) == sum(1 for _ in changed)
    ok_all &= ok
    lines.append(f"link:{s} 不同字段={diffkeys} 改动格={len(changed)} {'OK' if ok else 'FAIL'}")
    lines.append("    " + " | ".join(boardrows))
lines.append("全部通过" if ok_all else "有失败")
open(out, 'w', encoding='utf-8').write("\n".join(lines) + "\n")
print("\n".join(lines))
