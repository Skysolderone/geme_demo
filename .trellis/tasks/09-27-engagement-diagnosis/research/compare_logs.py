# 忠实性检查：两批日志逐种子比对（终局原因、结束大回合、小回合数、总提子数、各小回合落子序列、名次）。用法：python compare_logs.py <dirA> <dirB>
import json, sys, glob, os
sys.stdout.reconfigure(encoding='utf-8')
def load(d):
    out = {}
    for f in glob.glob(os.path.join(d, 'match-*.jsonl')):
        turns = []; res = None; hdr = None
        for line in open(f, encoding='utf-8'):
            o = json.loads(line)
            k = o.get('Kind')
            if k == 'header': hdr = o
            elif k == 'turn': turns.append((o['Player'], tuple(o['Placements']), tuple(o['Captures'])))
            elif k == 'result': res = o
        out[hdr['Seed']] = dict(turns=turns, reason=res['Reason'] if res else 'FAIL', end=res['MajorRound'] if res else None,
                                ranks=json.dumps(res.get('Ranks') if res else None, sort_keys=True))
    return out
a, b = load(sys.argv[1]), load(sys.argv[2])
same = diff = 0
for s in sorted(set(a) & set(b)):
    x, y = a[s], b[s]
    if x['turns'] == y['turns'] and x['reason'] == y['reason'] and x['end'] == y['end'] and x['ranks'] == y['ranks']:
        same += 1
    else:
        diff += 1
        n = next((i for i, (p, q) in enumerate(zip(x['turns'], y['turns'])) if p != q), min(len(x['turns']), len(y['turns'])))
        print('DIFF', s, x['reason'], x['end'], len(x['turns']), '|', y['reason'], y['end'], len(y['turns']), 'first diverge turn', n)
print(f'common {len(set(a) & set(b))} identical {same} differ {diff}')
