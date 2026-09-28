# v2-recalibration 段 B（2.5–2.7）：2 人图扫档的 config.json 核对、逐批统计与 D9 选档。
# 用法：python sweep2p.py <批次目录>...   目录名须为 2p-eye<E>-el<L>（期望权重由目录名给出，逐玩家逐维核对）。
# 口径：
# - 截断、整局无提子、已终局局平均结束大回合（截断局不计）、总提子、Pass 率与诊断 summarize.py 相同；
# - 第 3 大回合领先者（D9，段 A 后裁决）：第 3 大回合最后一个小回合快照里总势力的**唯一**最高者；并列局与截断局不计入分母，分别报告剔除局数。
#   诊断 summarize.py 的 round3_leader_win 把并列局计进分母（任一并列领先者获胜即算胜），与 D9 不符，本脚本不用它。
import json, sys, glob, os, math, re
sys.stdout.reconfigure(encoding='utf-8')
W = {"PowerGain": 10, "EnemyLoss": 8, "Relic": 6, "Safety": 35, "Growth": 4, "Initiative": 20, "Supply": 2, "Eye": 200, "Threat": 25}
BASE = (200, 8)


def parse_name(d):
    m = re.search(r'2p-eye(\d+)-el(\d+)$', d.rstrip('/\\'))
    return int(m.group(1)), int(m.group(2))


def check(d, eye, el):
    c = json.load(open(os.path.join(d, 'config.json'), encoding='utf-8'))
    w = dict(W); w.update(Eye=eye, EnemyLoss=el)
    ok = dict(map=c['MapId'] == 'siege-2p-base-v1', content=c['ContentSet'] == 'V2', thr=c['PassThreshold'] == 20, carry=c['CarryIn'] == 0,
              turnlimit=c['TurnLimit'] == 600, seed=c['SeedStart'] == 1, count=c['Count'] == 20, players=len(c['Players']) == 2,
              diff=all(p['Difficulty'] == 'Standard' for p in c['Players']), weights=all(p['Weights'] == w for p in c['Players']),
              search=all(p.get('Search') is None for p in c['Players']))
    return ok


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
        games.append(dict(seed=int(hdr['Seed'], 16), hdr=hdr, turns=turns, res=res))
    return games


def stats(d):
    eye, el = parse_name(d)
    games = load(d)
    n = len(games)
    fail = sum(1 for g in games if not g['res'])
    trunc = [g for g in games if g['res'] and g['res'].get('Truncated')]
    caps = [sum(len(t['Captures']) for t in g['turns']) for g in games]
    ends = [g['res']['MajorRound'] for g in games if g['res'] and not g['res'].get('Truncated')]
    lw = ln = ties = no_r3 = 0
    for g in games:
        if not g['res'] or g['res'].get('Truncated'):
            continue
        r3 = [t for t in g['turns'] if t['MajorRound'] == 3]
        if not r3:
            no_r3 += 1
            continue
        ps = r3[-1]['PlayersState']
        top = max(p['Total'] for p in ps)
        leaders = [p['Player'] for p in ps if p['Total'] == top]
        if len(leaders) != 1:
            ties += 1
            continue
        ln += 1
        lw += leaders[0] in g['res']['Winners']
    allt = [t for g in games for t in g['turns']]
    post = [t for t in allt if t['MajorRound'] >= 4]
    return dict(dir=os.path.basename(d.rstrip('/\\')), eye=eye, el=el, n=n, fail=fail, trunc=len(trunc), trunc_seeds=[g['seed'] for g in trunc],
                nocap=sum(1 for c in caps if c == 0), caps=sum(caps), end_mean=round(sum(ends) / len(ends), 2) if ends else None,
                end_range=[min(ends), max(ends)] if ends else None, r3=(lw, ln), r3_ties=ties, r3_none=no_r3,
                pass_all=round(100 * sum(t['Passed'] for t in allt) / len(allt), 1) if allt else None,
                pass_post=round(100 * sum(t['Passed'] for t in post) / len(post), 1) if post else None)


def dist710(x):
    return 0.0 if x is None or 7 <= x <= 10 else (7 - x if x < 7 else x - 10)


def tiebreak(s):
    # D9 第 5 条：与基线差异维数最少 → 离基线最近（先比 Eye 的相对变化，再比 EnemyLoss）。
    dims = (s['eye'] != BASE[0]) + (s['el'] != BASE[1])
    return (dims, abs(s['eye'] - BASE[0]) / BASE[0], abs(s['el'] - BASE[1]) / BASE[1])


def select(rows, log=print):
    """D9 五步。返回 (选中行, 是否用到第 5 条)；候选耗尽返回 (None, False)。"""
    cands = [s for s in rows if s['trunc'] <= 1 and s['n'] == 20 and s['fail'] == 0]
    log(f"  第 1 步 截断 ≤ 1/20：{[s['dir'] for s in cands]}（出局 {[s['dir'] for s in rows if s not in cands]}）")
    vetoed = set()
    while True:
        pool = [s for s in cands if s['dir'] not in vetoed]
        if not pool:
            log('  候选耗尽')
            return None, False
        pmin = min(s['nocap'] for s in pool) / 20
        se = math.sqrt(pmin * (1 - pmin) / 20)
        band = [s for s in pool if s['nocap'] / 20 <= pmin + se + 1e-12]
        log(f"  第 2 步 无提子带：p_min = {pmin:.2f}，SE = {se:.4f}，进带条件 ≤ {pmin + se:.4f}（≤ {math.floor((pmin + se) * 20 + 1e-9)} 局）→ {[s['dir'] for s in band]}")
        full = [s for s in band if s['r3'][1] == 20 and s['r3'][0] == 20]
        kept = [s for s in band if s not in full]
        log(f"  第 3 步 剔除 R3 领先者 20/20：剔除 {[s['dir'] for s in full]} → {[s['dir'] for s in kept]}")
        if kept:
            break
        vetoed.update(s['dir'] for s in band)
        log('  带内全部被剔除，在剩余档里重新取带')
    best = min(dist710(s['end_mean']) for s in kept)
    step4 = [s for s in kept if dist710(s['end_mean']) == best]
    log(f"  第 4 步 平均结束大回合离 7–10：{[(s['dir'], s['end_mean'], dist710(s['end_mean'])) for s in kept]} → {[s['dir'] for s in step4]}")
    if len(step4) == 1:
        return step4[0], False
    step5 = sorted(step4, key=tiebreak)
    log(f"  第 5 步 并列打破（差异维数 → Eye 相对变化 → EnemyLoss 相对变化）：{[(s['dir'], tiebreak(s)) for s in step5]} → {step5[0]['dir']}")
    return step5[0], True


def row(s):
    return (f"| ({s['eye']}, {s['el']}) | {s['trunc']}/20 | {s['nocap']}/20 | {s['end_mean']}（{s['end_range'][0]}–{s['end_range'][1]}） | "
            f"{s['r3'][0]}/{s['r3'][1]}（剔除并列 {s['r3_ties']}、截断 {s['trunc']}） | {s['caps']} | {s['pass_all']}% / {s['pass_post']}% |")


if __name__ == '__main__':
    dirs = sys.argv[1:]
    rows = []
    allok = True
    for d in dirs:
        eye, el = parse_name(d)
        ok = check(d, eye, el)
        s = stats(d)
        good = all(ok.values()) and s['n'] == 20 and s['fail'] == 0
        allok &= good
        print(f"{s['dir']}: " + ' '.join(f"{k}={'OK' if v else 'NG'}" for k, v in ok.items()) + f" games={s['n']} fail={s['fail']}")
        rows.append(s)
    print('CONFIG ALL OK' if allok else 'CONFIG MISMATCH')
    print('\n| (Eye, EnemyLoss) | 截断 | 整局无提子 | 已终局局平均结束 R（范围） | R3 领先者胜（D9：唯一领先者；分母剔除并列与截断） | 总提子 | Pass 率（全程 / R4+） |')
    print('|---|---|---|---|---|---|---|')
    for s in rows:
        print(row(s))
    print('\n选档（D9）：')
    chosen, used5 = select(rows)
    print(f"选中：{chosen['dir'] if chosen else '无'}；第 5 条{'用到' if used5 else '未用到'}")
