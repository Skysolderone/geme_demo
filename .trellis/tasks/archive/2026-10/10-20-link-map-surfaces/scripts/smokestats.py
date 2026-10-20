import sys, json, glob, os, statistics, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding='utf-8')
from mapjson import *
root, mapdir = sys.argv[1], sys.argv[2]
KIND = {'D': '荒漠', 'M': '沼泽', 'P': '岩台', 'S': '浅滩'}
def stats(dirs):
    rounds, reasons, capped, trunc, ms = [], collections.Counter(), 0, 0, []
    theme_ranks = collections.defaultdict(list)
    place = collections.Counter(); cells = collections.Counter(); occ_sum = collections.Counter(); occ_n = collections.Counter(); end_occ = collections.Counter()
    all_place = 0; all_cells = 0; games = 0
    for d in dirs:
        for f in sorted(glob.glob(f'{root}/{d}/match-*.jsonl')):
            rows = [json.loads(l) for l in open(f, encoding='utf-8').read().splitlines() if l.strip()]
            h = rows[0]; res = rows[-1]; turns = [r for r in rows if r['Kind'] == 'turn']
            assert res['Kind'] == 'result'
            games += 1
            m = load(f"{mapdir}/{h['MapId'].replace(':', '')}.json")
            sp = {}
            for y in range(m['Height']):
                for x in range(m['Width']):
                    k = m['_s']((x, y))
                    if k in KIND: sp[(x, y)] = k
            for k in sp.values(): cells[k] += 1
            all_cells += h['PlayableCells']
            rounds.append(res['MajorRound']); reasons[res['Reason']] += 1
            trunc += 1 if res.get('Truncated') else 0
            ms += [t['ElapsedMs'] for t in turns]
            # 上限结算：结算原因记"上限"——从事件 / 结果里找 RoundCap
            capped += 1 if any(r.get('Kind') == 'event' and 'RoundCap' in json.dumps(r, ensure_ascii=False) for r in rows) else 0
            themes = [b['Theme'] for b in m['Boards']]
            if not res.get('Truncated'):
                for s in res['Standings']:
                    theme_ranks[themes[h['Zones'][s['Player']]]].append(s['Rank'])
            for t in turns:
                for p in t['Placements']:
                    all_place += 1
                    c = parse(p.split(':')[0])
                    if c in sp: place[sp[c]] += 1
                occupied = set(parse(s) for ps in t['PlayersState'] for g in ps['Groups'] for s in g['Stones'])
                for c, k in sp.items():
                    occ_sum[k] += 1 if c in occupied else 0
                    occ_n[k] += 1
            last = set(parse(s) for ps in turns[-1]['PlayersState'] for g in ps['Groups'] for s in g['Stones'])
            for c, k in sp.items(): end_occ[k] += 1 if c in last else 0
    out = []
    out.append(f"局数 {games}；大回合 中位 {statistics.median(rounds)} / 最小 {min(rounds)} / 最大 {max(rounds)}；分布 {sorted(rounds)}")
    out.append(f"终局原因 {dict(reasons)}；turn_limit 截断 {trunc}/{games}；上限结算（日志含 RoundCap 事件的局） {capped}")
    out.append(f"单小回合耗时 中位 {statistics.median(ms)} ms（样本 {len(ms)}，最大 {max(ms)}）")
    out.append("五主题平均名次（截断局不计；括号为锁定人次）：" + "  ".join(f"{k} {sum(v)/len(v):.2f}（{len(v)}）" for k, v in sorted(theme_ranks.items())))
    base = all_place / all_cells
    out.append(f"全图口径：落子 {all_place} 次 / 可落子格 {all_cells}（各局累加）= 每格 {base:.2f} 次")
    for k in 'DMPS':
        if cells[k] == 0: continue
        out.append(f"  {KIND[k]}：{cells[k]} 格（各局累加），落子 {place[k]} 次 = 每格 {place[k]/cells[k]:.2f} 次（全图每格 {base:.2f}）；被占比例 逐小回合平均 {occ_sum[k]*100/occ_n[k]:.1f}% ，终局 {end_occ[k]*100/cells[k]:.1f}%")
    return out
res = []
for name, dirs in [('link1', ['link1']), ('per-map', ['per-map']), ('all', ['link1', 'per-map'])]:
    res.append(f'## {name}'); res.append('```'); res += stats(dirs); res.append('```')
print('\n'.join(res))
