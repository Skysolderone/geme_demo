# 专家诊断补充指标（只读日志）：两层加分量级、模拟回应命中率、改选的分数构成。
# 用法：python analyze2.py <sim-out/expert-diagnosis 目录>
import json, sys, glob, os, statistics
from collections import Counter

ROOT = sys.argv[1]
ME = 0


def cells(key):
    return frozenset(p.split(":")[0] for p in key.split(",")) if key else frozenset()


def turns_of(path):
    with open(path, encoding="utf-8") as f:
        return [o for o in map(json.loads, f) if o.get("Kind") == "turn"]


def med(xs):
    return statistics.median(xs) if xs else None


for arm in ("E0", "E8"):
    s = Counter()
    spread_before, spread_two, two_over_before = [], [], []
    jacc, exact, resp_pass_actual_place = [], 0, 0
    sup_better = 0
    deficit_vs_bonus = []
    for path in sorted(glob.glob(os.path.join(ROOT, arm, "match-*.jsonl"))):
        ts = turns_of(path)
        for i, t in enumerate(ts):
            e = t.get("Lookahead")
            if t["Player"] != ME or not e or e["Status"] != "Applied":
                continue
            c = e["Candidates"]
            b = [int(x["Before"]) for x in c]
            tp = [int(x.get("TwoPlyBonus", 0)) for x in c]
            spread_before.append(max(b) - min(b))
            if arm == "E8":
                spread_two.append(max(tp) - min(tp))
                if b[0] > 0:
                    two_over_before.append(max(tp) / b[0])
            ch = c[e["Chosen"]]
            if e["Changed"]:
                c0 = c[0]
                if ch.get("Source") == "Supplement" and int(ch["Before"]) > int(c0["Before"]):
                    sup_better += 1
                one = lambda x: int(x["After"]) - int(x.get("TwoPlyBonus", 0))
                deficit_vs_bonus.append((one(c0) - one(ch), int(ch.get("TwoPlyBonus", 0)) - int(c0.get("TwoPlyBonus", 0))))
            # 模拟回应 vs 实际：被选候选的下一名对手在其行动大回合的实际落子
            r, rr = ch.get("Responder"), ch.get("ResponderRound")
            if r is None:
                continue
            actual = next((u for u in ts[i + 1:] if u["Player"] == r and u["MajorRound"] == rr), None)
            if actual is None:
                s["no_actual"] += 1
                continue
            # 只在中间没有别人行动时才算"同一局面"（下一名对手紧接专家行动）
            adjacent = ts[i + 1]["Player"] == r
            sim = cells(ch.get("Response") or "")
            act = frozenset(p.split(":")[0] for p in actual["Placements"])
            s["resp_pairs"] += 1
            s["resp_adjacent"] += adjacent
            if not sim and not act:
                exact += 1; jacc.append(1.0); continue
            if not sim and act:
                s["sim_pass_actual_play"] += 1
            if sim and not act:
                s["sim_play_actual_pass"] += 1
            u = sim | act
            jv = len(sim & act) / len(u) if u else 1.0
            jacc.append(jv)
            exact += sim == act
    print("=====", arm)
    print(" counts", dict(s))
    print(" 前瞻集自身总分极差 中位", med(spread_before))
    if arm == "E8":
        print(" 两层加分极差 中位", med(spread_two), "；最大两层加分 / 首候选自身总分 中位", round(med(two_over_before), 3))
        print(" 改选到自身总分更高的多样补充（高难贪心次优）", sup_better)
    dv = deficit_vs_bonus
    print(" 改选次数", len(dv), "；一层分数让步（首候选 − 被选）中位", med([d for d, _ in dv]),
          "；一层分数被选者更低的次数", sum(d > 0 for d, _ in dv),
          "；两层加分差（被选 − 首候选）中位", med([g for _, g in dv]))
    print(" 模拟回应与实际：落点格完全相同", exact, "/", s["resp_pairs"], "；Jaccard 中位", med(jacc),
          "；均值", round(statistics.mean(jacc), 3) if jacc else None)
