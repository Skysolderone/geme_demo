# 2 人图落点按区域细分（保护期后）：出生区 / 近侧侧翼（与本方出生区经缓坡相连）/ 远侧侧翼 / 中央岛与桥。用法：python flank2p.py <批次目录>
import json, glob, sys, os
from collections import Counter
sys.stdout.reconfigure(encoding='utf-8')
m = json.load(open('maps/siege-2p-base-v1.json', encoding='utf-8'))
def area(c):
    col = 'ABCDEFGHJ'.index(c[0]); row = int(c[1:])
    if c in ('E3', 'E4', 'E5', 'E6', 'E7', 'D5', 'F5'): return 'center'
    if col <= 3 and row >= 5: return 'NW'   # 西北侧翼（含 A5/B5 缓坡，出生区 1 的近侧）
    if col >= 5 and row <= 5: return 'SE'   # 东南侧翼（含 H5/J5 缓坡，出生区 2 的近侧）
    return 'other'
zones = m.get('BirthZones') or m.get('birthZones')
cnt = Counter()
for f in glob.glob(os.path.join(sys.argv[1], 'match-*.jsonl')):
    h = None
    for line in open(f, encoding='utf-8'):
        o = json.loads(line)
        if o['Kind'] == 'header': h = o
        elif o['Kind'] == 'turn' and o['MajorRound'] >= 4:
            z = h['Zones'][o['Player']]
            for p in o['Placements']:
                c = p.split(':')[0]; a = area(c)
                if a == 'other': a = 'zone'
                near = {0: 'NW', 1: 'SE'}[z]
                lab = 'near-flank' if a == near else ('far-flank' if a in ('NW', 'SE') else a)
                cnt[lab] += 1
tot = sum(cnt.values())
print({k: f'{v} ({100*v/tot:.1f}%)' for k, v in cnt.most_common()})
