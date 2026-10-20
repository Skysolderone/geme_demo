import sys
sys.path.insert(0, '.')
from mapjson import *
d = sys.argv[1]
tot = {}
for s in range(1, 21):
    m = load(f"{d}/link{s}.json")
    row = []
    for b in boards(m):
        def base(c, s1=True):
            if m['_h'](c) != 0 or m['_s'](c) != 'G' or c in m['_obs'] or c in m['_relic']: return False
            if s1 and any(n in b['core'] for n in nb(c)): return False
            return True
        bc = [c for c in b['middle'] if base(c)]
        bc0 = [c for c in b['middle'] if base(c, False)]
        row.append(f"{b['theme'][:4]}{b['w']}x{b['h']}:{len(bc)}/{len(bc0)}")
        key = (min(b['w'], b['h']), max(b['w'], b['h']))
        tot.setdefault(key, []).append(len(bc))
    print(s, m['_flip'], ' '.join(row))
for k, v in sorted(tot.items()):
    print(k, 'S1 基础候选', sorted(v))
