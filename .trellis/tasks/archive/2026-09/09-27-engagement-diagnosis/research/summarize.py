# engagement-diagnosis 汇总：读一批官方日志（match-*.jsonl）+ 诊断记录（diag-*.jsonl），只输出汇总。
# 用法：python summarize.py <批次目录> [--json 输出文件] [--quiet]
import json, sys, glob, os, statistics as st
from collections import Counter, defaultdict
sys.stdout.reconfigure(encoding='utf-8')
d = sys.argv[1]
jout = sys.argv[sys.argv.index('--json') + 1] if '--json' in sys.argv else os.path.join(d, 'engagement-summary.json')


def pct(a, b):
    return None if not b else round(100.0 * a / b, 1)


def bucket(r):
    return '1-3' if r <= 3 else ('4-6' if r <= 6 else '7+')


def load(d):
    games = []
    for f in sorted(glob.glob(os.path.join(d, 'match-*.jsonl'))):
        hdr = res = None
        turns = []
        for line in open(f, encoding='utf-8'):
            o = json.loads(line)
            k = o.get('Kind')
            if k == 'header':
                hdr = o
            elif k == 'turn':
                turns.append(o)
            elif k == 'result':
                res = o
        seed = int(hdr['Seed'], 16)
        diag = []
        df = os.path.join(d, 'diag-%016X.jsonl' % seed)
        if os.path.exists(df):
            diag = [json.loads(l) for l in open(df, encoding='utf-8')]
        games.append(dict(seed=seed, hdr=hdr, turns=turns, res=res, diag=diag))
    return games


def summarize(games):
    n = len(games)
    cfg = games[0]['hdr']['Config']
    out = dict(n=n, map=cfg['MapId'], players=len(cfg['Players']), difficulty=cfg['Players'][0]['Difficulty'],
               threshold=cfg.get('PassThreshold'), content=cfg.get('ContentSet'), weights=cfg['Players'][0].get('Weights'))
    reasons = Counter(g['res']['Reason'] if g['res'] else 'FAIL' for g in games)
    trunc = [g for g in games if g['res'] and g['res'].get('Truncated')]
    caps = [sum(len(t['Captures']) for t in g['turns']) for g in games]
    ends = [g['res']['MajorRound'] for g in games if g['res'] and not g['res'].get('Truncated')]
    first_cap = [next((t['MajorRound'] for t in g['turns'] if t['Captures']), None) for g in games]
    fc = [x for x in first_cap if x]
    out.update(reasons=dict(reasons), truncated=len(trunc), no_capture_games=sum(1 for c in caps if c == 0),
               captured_stones_total=sum(caps), end_round_mean=round(st.mean(ends), 2) if ends else None,
               end_round_range=[min(ends), max(ends)] if ends else None,
               first_capture_round_mean=round(st.mean(fc), 2) if fc else None)
    lead_n = lead_w = 0
    for g in games:
        if not g['res'] or g['res'].get('Truncated'):
            continue
        r3 = [t for t in g['turns'] if t['MajorRound'] == 3]
        if not r3:
            continue
        ps = r3[-1]['PlayersState']
        top = max(p['Total'] for p in ps)
        leaders = {p['Player'] for p in ps if p['Total'] == top}
        lead_n += 1
        lead_w += bool(leaders & set(g['res']['Winners']))
    out['round3_leader_win'] = [lead_w, lead_n, pct(lead_w, lead_n)]
    allt = [t for g in games for t in g['turns']]
    post = [t for t in allt if t['MajorRound'] >= 4]
    out['pass_rate_all'] = [sum(t['Passed'] for t in allt), len(allt), pct(sum(t['Passed'] for t in allt), len(allt))]
    out['pass_rate_post'] = [sum(t['Passed'] for t in post), len(post), pct(sum(t['Passed'] for t in post), len(post))]
    sat = []
    for g in games:
        last = g['turns'][-1]
        life = last.get('Life') or {}
        stones = sum(len(gr['Stones']) for p in last['PlayersState'] for gr in p['Groups'])
        pc = life.get('PlayableCells') or 0
        if pc:
            prot = life.get('ProtectedCells') or 0
            sat.append((stones / pc, prot / pc, (stones + prot) / pc))
    if sat:
        out['end_board'] = dict(stone_density=round(100 * st.mean(s[0] for s in sat), 1),
                                protected_share=round(100 * st.mean(s[1] for s in sat), 1),
                                stones_plus_protected=round(100 * st.mean(s[2] for s in sat), 1))
    per_round = defaultdict(list)
    for g in games:
        byr = {}
        for t in g['turns']:
            byr[t['MajorRound']] = t
        for r, t in byr.items():
            life = t.get('Life') or {}
            pc = life.get('PlayableCells') or 0
            if not pc:
                continue
            stones = sum(len(gr['Stones']) for p in t['PlayersState'] for gr in p['Groups'])
            per_round[r].append((stones / pc, (life.get('ProtectedCells') or 0) / pc))
    out['per_round_density'] = {r: dict(n=len(v), stones=round(100 * st.mean(x[0] for x in v), 1),
                                        protected=round(100 * st.mean(x[1] for x in v), 1))
                                for r, v in sorted(per_round.items()) if r <= 12}
    D = [x for g in games for x in g['diag']]
    if D:
        out['rank_match'] = [sum(1 for x in D if x['rank_match'] is True), sum(1 for x in D if x['rank_match'] is not None)]
        by = defaultdict(list)
        for x in D:
            by[bucket(x['round'])].append(x)
        out['by_phase'] = {b: phase_row(xs) for b, xs in sorted(by.items())}
        fo, fopp, fcon = [], [], []
        for g in games:
            xs = g['diag']
            fo.append(next((x['round'] for x in xs if any(a != 'own' for a in x['chosen_at'])), None))
            fopp.append(next((x['round'] for x in xs if 'opp' in x['chosen_at']), None))
            fcon.append(next((x['round'] for x in xs if x['contact'] or x['chosen_touch_enemy']), None))

        def s(v):
            vv = [x for x in v if x is not None]
            return dict(mean=round(st.mean(vv), 2) if vv else None, never=sum(1 for x in v if x is None),
                        dist=dict(sorted(Counter(vv).items())))
        out['first_outside_own_zone'] = s(fo)
        out['first_into_opp_zone'] = s(fopp)
        out['first_contact'] = s(fcon)
    return out


def phase_row(xs):
    chosen = Counter(a for x in xs for a in x['chosen_at'])
    nch = sum(chosen.values())
    passes = sum(1 for x in xs if not x['chosen'])
    gains = [int(x['max_gain']) for x in xs if x['max_gain'] is not None]
    return dict(
        decisions=len(xs), pass_rate=pct(passes, len(xs)),
        placed_per_decision=round(nch / len(xs), 2), deploy_limit_mean=round(st.mean(x['deploy_limit'] for x in xs), 2),
        chosen_share={k: pct(v, nch) for k, v in sorted(chosen.items())},
        legal_empty_mean=round(st.mean(x['legal_empty'] for x in xs), 1),
        forbidden_me_mean=round(st.mean(x['forbidden_me'] for x in xs), 1),
        empty_playable_mean=round(st.mean(x['empty_playable'] for x in xs), 1),
        evaluated=sum(x['evaluated'] for x in xs), life_elim=sum(x['life_elim'] for x in xs),
        life_elim_share=pct(sum(x['life_elim'] for x in xs), sum(x['life_elim'] + x['evaluated'] for x in xs)),
        life_elim_by=dict(sum((Counter(x['life_elim_by']) for x in xs), Counter())),
        positive=sum(x['positive'] for x in xs), over_thr=sum(x['over_thr'] for x in xs),
        over_thr_share_of_evaluated=pct(sum(x['over_thr'] for x in xs), sum(x['evaluated'] for x in xs)),
        over_thr_by=dict(sum((Counter(x['over_by']) for x in xs), Counter())),
        decisions_no_point_over_thr=sum(1 for x in xs if x['over_thr'] == 0),
        decisions_no_point_over_thr_but_positive=sum(1 for x in xs if x['over_thr'] == 0 and x['positive'] > 0),
        max_gain_at=dict(Counter(x['max_gain_at'] for x in xs if x['max_gain_at'])),
        max_gain_median=st.median(gains) if gains else None,
        greedy_kept=sum(x['g_kept'] for x in xs), greedy_wd_thr=sum(x['g_wd_thr'] for x in xs),
        greedy_wd_thr_positive=sum(x['g_wd_thr_pos'] for x in xs), greedy_wd_life=sum(x['g_wd_life'] for x in xs),
        greedy_wd_illegal=sum(x['g_wd_illegal'] for x in xs),
        contact_decisions=pct(sum(1 for x in xs if x['contact']), len(xs)),
        chosen_touch_enemy=sum(x['chosen_touch_enemy'] for x in xs),
        enemy_weak_stones_mean=round(st.mean(x.get('enemy_weak_stones', 0) for x in xs), 2),
        enemy_alive_share=pct(sum(x.get('enemy_alive_stones', 0) for x in xs),
                              sum(x.get('enemy_alive_stones', 0) + x.get('enemy_weak_stones', 0) for x in xs)),
        my_alive_share=pct(sum(x.get('my_alive_stones', 0) for x in xs),
                           sum(x.get('my_alive_stones', 0) + x.get('my_weak_stones', 0) for x in xs)),
        enemy_weak_le2_decisions=sum(1 for x in xs if x.get('enemy_weak_le2', 0) > 0),
        cap_point_decisions=sum(1 for x in xs if x.get('cap_points', 0) > 0),
        cap_points_total=sum(x.get('cap_points', 0) for x in xs),
    )


if __name__ == '__main__':
    out = summarize(load(d))
    out['dir'] = d
    json.dump(out, open(jout, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    if '--quiet' not in sys.argv:
        print(json.dumps(out, ensure_ascii=False, indent=1))
