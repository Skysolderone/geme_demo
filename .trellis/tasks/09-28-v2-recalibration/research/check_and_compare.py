# v2-recalibration 段 A：① 逐批核对 config.json 实际生效值；② pass-80 / pass-0 与诊断 b06 / b07 逐种子比对（结束大回合、终局原因、总提子、胜者）。
# 用法：python check_and_compare.py <批次目录>...   比对对：通过 --pair <A> <B> 追加
import json, sys, glob, os
sys.stdout.reconfigure(encoding='utf-8')
W = {"PowerGain": 10, "EnemyLoss": 8, "Relic": 6, "Safety": 35, "Growth": 4, "Initiative": 20, "Supply": 2, "Eye": 200, "Threat": 25}
args = sys.argv[1:]
pairs = []
while '--pair' in args:
    i = args.index('--pair'); pairs.append((args[i + 1], args[i + 2])); del args[i:i + 3]
def check(d, thr, diffs):
    c = json.load(open(os.path.join(d, 'config.json'), encoding='utf-8'))
    ok = dict(map=c['MapId'] == 'siege-4p-base-v5', content=c['ContentSet'] == 'V2', thr=c['PassThreshold'] == thr, carry=c['CarryIn'] == 0,
              turnlimit=c['TurnLimit'] == 600, seed=c['SeedStart'] == 1, count=c['Count'] == 20, players=len(c['Players']) == len(diffs),
              diff=[p['Difficulty'] for p in c['Players']] == diffs, weights=all(p['Weights'] == W for p in c['Players']))
    n = len(glob.glob(os.path.join(d, 'match-*.jsonl')))
    print(f"{os.path.basename(d)}: " + ' '.join(f"{k}={'OK' if v else 'NG'}" for k, v in ok.items()) + f" games={n} thr={c['PassThreshold']} diffs={','.join(diffs)}")
    return all(ok.values()) and n == 20
def load(d):
    out = {}
    for f in glob.glob(os.path.join(d, 'match-*.jsonl')):
        caps = 0; res = hdr = None
        for line in open(f, encoding='utf-8'):
            o = json.loads(line); k = o.get('Kind')
            if k == 'header': hdr = o
            elif k == 'turn': caps += len(o['Captures'])
            elif k == 'result': res = o
        out[int(hdr['Seed'], 16)] = (res['MajorRound'], res['Reason'] + ('/T' if res.get('Truncated') else ''), caps, tuple(res['Winners']))
    return out
if __name__ == '__main__':
    allok = True
    for spec in args:
        d, thr, diffs = spec.split('|'); allok &= check(d, int(thr), diffs.split(','))
    print('CONFIG ALL OK' if allok else 'CONFIG MISMATCH')
    for a, b in pairs:
        x, y = load(a), load(b)
        print(f"\n| 种子 | {os.path.basename(a)} 结束R/原因/提子/胜者 | {os.path.basename(b.rstrip('/'))} | 一致 |\n|---|---|---|---|")
        same = 0
        for s in sorted(set(x) | set(y)):
            e = x.get(s) == y.get(s); same += e
            f = lambda t: f"R{t[0]} {t[1]} {t[2]} {'+'.join(map(str, t[3]))}" if t else '缺'
            print(f"| {s} | {f(x.get(s))} | {f(y.get(s))} | {'是' if e else '否'} |")
        nc = lambda z: sum(1 for t in z.values() if t[2] == 0)
        print(f"一致 {same}/{len(set(x) | set(y))}；无提子 {nc(x)}/{len(x)} vs {nc(y)}/{len(y)}；总提子 {sum(t[2] for t in x.values())} vs {sum(t[2] for t in y.values())}")
