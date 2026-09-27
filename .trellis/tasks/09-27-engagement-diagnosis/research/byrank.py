# Q2 按名次分组（第 5 大回合起，与 HANDOFF 旧口径同窗）。用法：python byrank.py <批次目录> [起始大回合，缺省 5]
# 主口径：决策时（本小回合部署前）的势力名次（并列跳号）；附：终局名次（截断局无名次，不计）。
import json, sys, os, glob, statistics as st
from collections import defaultdict
sys.stdout.reconfigure(encoding='utf-8')
d = sys.argv[1]
start = int(sys.argv[2]) if len(sys.argv) > 2 else 5
final_rank = {}
for f in glob.glob(os.path.join(d, 'match-*.jsonl')):
    lines = open(f, encoding='utf-8').read().splitlines()
    h = json.loads(lines[0]); r = json.loads(lines[-1])
    seed = int(h['Seed'], 16)
    if r.get('Kind') == 'result' and not r.get('Truncated'):
        for s in r['Standings']:
            final_rank[(seed, s['Player'])] = s['Rank']
D = []
for f in glob.glob(os.path.join(d, 'diag-*.jsonl')):
    D += [json.loads(l) for l in open(f, encoding='utf-8')]
D = [x for x in D if x['round'] >= start]


def row(xs):
    n = len(xs)
    placed = [len(x['chosen']) for x in xs]
    gains = [int(x['max_gain']) for x in xs if x['max_gain'] is not None]
    ev = sum(x['evaluated'] for x in xs)
    return dict(
        n=n,
        deploy_limit=round(st.mean(x['deploy_limit'] for x in xs), 2),
        placed=round(st.mean(placed), 2),
        pass_rate=round(100 * sum(1 for p in placed if p == 0) / n, 1),
        legal_empty=round(st.mean(x['legal_empty'] for x in xs), 1),
        forbidden_me=round(st.mean(x['forbidden_me'] for x in xs), 1),
        empty_playable=round(st.mean(x['empty_playable'] for x in xs), 1),
        protected_own=round(st.mean(x['protected_own'] for x in xs), 1),
        life_elim_share=round(100 * sum(x['life_elim'] for x in xs) / max(1, sum(x['life_elim'] + x['evaluated'] for x in xs)), 1),
        over_thr_share=round(100 * sum(x['over_thr'] for x in xs) / max(1, ev), 1),
        no_point_over_thr=round(100 * sum(1 for x in xs if x['over_thr'] == 0) / n, 1),
        no_positive_point=round(100 * sum(1 for x in xs if x['positive'] == 0) / n, 1),
        pass_but_positive_exists=sum(1 for x in xs if not x['chosen'] and x['positive'] > 0),
        pass_total=sum(1 for x in xs if not x['chosen']),
        greedy_kept=round(st.mean(x['g_kept'] for x in xs), 2),
        greedy_wd_thr_pos=round(st.mean(x['g_wd_thr_pos'] for x in xs), 2),
        max_gain_median=st.median(gains) if gains else None,
        my_weak_stones=round(st.mean(x.get('my_weak_stones', 0) for x in xs), 1),
        cap_point_decisions=round(100 * sum(1 for x in xs if x.get('cap_points', 0) > 0) / n, 1),
    )


def table(title, key):
    groups = defaultdict(list)
    for x in D:
        k = key(x)
        if k is not None:
            groups[k].append(x)
    print(f'### {title}（第 {start} 大回合起）')
    keys = ['n', 'deploy_limit', 'placed', 'pass_rate', 'legal_empty', 'forbidden_me', 'empty_playable', 'protected_own', 'life_elim_share',
            'over_thr_share', 'no_point_over_thr', 'no_positive_point', 'pass_but_positive_exists', 'pass_total', 'greedy_kept',
            'greedy_wd_thr_pos', 'max_gain_median', 'my_weak_stones', 'cap_point_decisions']
    print('| 名次 | ' + ' | '.join(keys) + ' |')
    print('|' + '---|' * (len(keys) + 1))
    out = {}
    for k in sorted(groups):
        r = row(groups[k]); out[k] = r
        print(f'| {k} | ' + ' | '.join(str(r[c]) for c in keys) + ' |')
    return out


res = dict(dir=d, start=start,
           by_current_rank=table('按决策时名次', lambda x: x['rank']),
           by_final_rank=table('按终局名次', lambda x: final_rank.get((x['seed'], x['player']))))
json.dump(res, open(os.path.join(d, f'byrank-r{start}.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
