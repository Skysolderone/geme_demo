# Q3 截断局分类（只报汇总）。用法：python truncation.py <批次目录>...
# 对每个 turn_limit 截断局，看末 60 个小回合（尾部）：
#   coords   尾部落点坐标集合大小；caprate 尾部有提子的小回合占比；passrate 尾部 Pass 占比；
#   krep     末 100 个落子小回合里"只看归属 + 改造"盘面键的重复次数（同形修正后应为 0）；
#   stones   尾部首末盘面棋子数（持平 = 来回互提，递增 = 仍在填盘）。
# 分类（写死，先定后看）：
#   循环 = 尾部有提子小回合 ≥ 30% 且尾部首末棋子数相差 ≤ 3（盘面总量持平的来回互提；同形修正后盘面不会严格重复，krep 应为 0）；
#          另列 ai-eye cycles.py 的窄判据 narrow（尾部坐标集合 ≤ 10 且有提子 ≥ 30%）作参照——先用窄判据时 easy20 的 4 局被拆成 2+2，
#          而 4 局的尾部形态（提子率 100%、棋子数持平、8–12 格）并无差别，故改用上面的判据；
#   拖长 = 非循环且尾部有提子小回合 < 10%（没有互提，只是一直有人落子、整轮 Pass 迟迟不出现）；
#   其他 = 其余（有较多提子但落点分散：多点混战）。
import json, sys, os, glob
from collections import Counter
sys.stdout.reconfigure(encoding='utf-8')
TAIL = 60
for d in sys.argv[1:]:
    rows = []
    total = 0
    for f in sorted(glob.glob(os.path.join(d, 'match-*.jsonl'))):
        total += 1
        turns = []; res = None
        for line in open(f, encoding='utf-8'):
            o = json.loads(line)
            if o['Kind'] == 'turn': turns.append(o)
            elif o['Kind'] == 'result': res = o
        if not res or not res.get('Truncated'):
            continue
        tail = turns[-TAIL:]
        coords = {p.split(':')[0] for t in tail for p in t['Placements']}
        caprate = sum(1 for t in tail if t['Captures']) / len(tail)
        passrate = sum(1 for t in tail if t['Passed']) / len(tail)
        board = {}; edits = set(); keys = []
        stones_series = []
        for t in turns:
            for e in t.get('Edits') or []:
                edits.add(e['Target'])
            for c in t['Captures']:
                board.pop(c.split(':')[0], None)
            for p in t['Placements']:
                board[p.split(':')[0]] = t['Player']
            stones_series.append(len(board))
            if not t['Passed']:
                keys.append((tuple(sorted(board.items())), frozenset(edits)))
        k100 = keys[-100:]
        krep = len(k100) - len(set(k100))
        capcells = Counter(c.split(':')[0] for t in tail for c in t['Captures'])
        narrow = len(coords) <= 10 and caprate >= 0.30
        cyc = caprate >= 0.30 and abs(stones_series[-1] - stones_series[-TAIL]) <= 3
        kind = '循环' if cyc else ('拖长' if caprate < 0.10 else '其他')
        players_in_tail = sorted({t['Player'] for t in tail if t['Placements']})
        rows.append(dict(seed=int(os.path.basename(f)[6:-6], 16), kind=kind, narrow=narrow, end_round=res['MajorRound'], coords=len(coords),
                         caprate=round(caprate, 2), passrate=round(passrate, 2), krep=krep,
                         stones_tail=[stones_series[-TAIL], stones_series[-1]], top_capcells=capcells.most_common(3),
                         active_players_tail=players_in_tail))
    print(f'== {os.path.basename(os.path.normpath(d))}: 截断 {len(rows)}/{total}；分类', dict(Counter(r["kind"] for r in rows)))
    for r in rows:
        print('  ', json.dumps(r, ensure_ascii=False))
