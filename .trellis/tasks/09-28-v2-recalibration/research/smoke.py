# 1.5 冒烟判据：截断数、最长结束大回合、"第 1 大回合全员一子不落即终局"局数（第 1 大回合全部小回合 Passed 且终局大回合为 1）。用法：python smoke.py <目录>...
import json, sys, glob, os
sys.stdout.reconfigure(encoding='utf-8')
for d in sys.argv[1:]:
    tr = r1 = caps = 0; ends = []; trs = []
    for f in sorted(glob.glob(os.path.join(d, 'match-*.jsonl'))):
        turns = []; res = hdr = None
        for line in open(f, encoding='utf-8'):
            o = json.loads(line); k = o.get('Kind')
            if k == 'header': hdr = o
            elif k == 'turn': turns.append(o)
            elif k == 'result': res = o
        seed = int(hdr['Seed'], 16)
        if res.get('Truncated'): tr += 1; trs.append(seed)
        else: ends.append(res['MajorRound'])
        t1 = [t for t in turns if t['MajorRound'] == 1]
        if res['MajorRound'] == 1 and t1 and all(t['Passed'] for t in t1): r1 += 1
        caps += sum(len(t['Captures']) for t in turns)
    print(f"{os.path.basename(d.rstrip('/'))}: 局 {len(ends) + tr} 截断 {tr} {trs} 最长结束R {max(ends) if ends else None} 平均结束R {round(sum(ends) / len(ends), 2) if ends else None} 第1大回合全员不落即终局 {r1} 总提子 {caps}")
