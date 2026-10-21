# 专家诊断（10-21-expert-diagnosis）：只读日志，成对比较座位 1（P0）在 H / E0 / E8 三臂的表现。
# 用法：python analyze.py <sim-out/expert-diagnosis 目录>  → 打印汇总，并写 <目录>/summary.json
import json, sys, glob, os, math, statistics
from collections import Counter, defaultdict

ROOT = sys.argv[1]
ARMS = ["H", "E0", "E8"]
ME = 0


def load(path):
    header, turns, result = None, [], None
    with open(path, encoding="utf-8") as f:
        for line in f:
            o = json.loads(line)
            k = o.get("Kind")
            if k == "header":
                header = o
            elif k == "turn":
                turns.append(o)
            elif k == "result":
                result = o
    return header, turns, result


def stones(key):
    return 0 if not key else key.count(",") + 1


def cells(key):
    return frozenset(p.split(":")[0] for p in key.split(",")) if key else frozenset()


def seed_of(path):
    return int(os.path.basename(path)[6:-6], 16)


def rank_before(prev, player):
    if prev is None:
        return None
    for p in prev["PlayersState"]:
        if p["Player"] == player:
            return p.get("Rank")
    return None


def power_at_round_end(turns, player):
    out = {}
    for t in turns:
        for p in t["PlayersState"]:
            if p["Player"] == player:
                out[t["MajorRound"]] = int(p["Total"])
    return out


def per_game(path):
    header, turns, result = load(path)
    g = {"seed": seed_of(path), "reason": result["Reason"], "rounds": result["MajorRound"], "turns": result["TurnCount"]}
    st = {s["Player"]: s for s in result["Standings"]}
    g["rank"] = st[ME]["Rank"] if ME in st else None
    g["win"] = ME in result["Winners"]
    g["final_power"] = int(st[ME]["Power"]) if ME in st else None
    g["final_stones"] = st[ME]["Stones"] if ME in st else None
    acted = place = passes = stalls = caps = 0
    pass_by_rank = Counter()
    act_by_rank = Counter()
    by_phase = defaultdict(lambda: [0, 0, 0])  # 构筑期 / 全图：小回合数、落子枚数、Pass 数
    prev = None
    la = []
    for t in turns:
        if t["Player"] == ME:
            acted += 1
            r = rank_before(prev, ME)
            act_by_rank[r] += 1
            phase = "build" if t["MajorRound"] <= 3 else "open"
            by_phase[phase][0] += 1
            n = len(t["Placements"])
            place += n
            by_phase[phase][1] += n
            caps += len(t["Captures"])
            if t["Passed"]:
                if t.get("Stalled"):
                    stalls += 1
                else:
                    passes += 1
                    pass_by_rank[r] += 1
                by_phase[phase][2] += 1
            if t.get("Lookahead"):
                la.append((t["Turn"], t["MajorRound"], t["Lookahead"]))
        prev = t
    g.update(acted=acted, placements=place, passes=passes, stalls=stalls, captures=caps,
             pass_by_rank={str(k): v for k, v in pass_by_rank.items()},
             act_by_rank={str(k): v for k, v in act_by_rank.items()},
             by_phase={k: v for k, v in by_phase.items()})
    g["power_by_round"] = power_at_round_end(turns, ME)
    # 每小回合的可比签名：用于找与 H 臂首个分歧
    g["_sig"] = [(t["Player"], tuple(t["Placements"]), t["Passed"]) for t in turns]
    g["_la"] = la
    return g


def lookahead_stats(games):
    s = Counter()
    deltas_stones, deltas_before, firsts = [], [], []
    two_ply_driven = 0
    for g in games:
        first_changed = None
        for turn, rnd, e in g["_la"]:
            s["records"] += 1
            s["status:" + e["Status"]] += 1
            if e["Status"] != "Applied":
                continue
            c = e["Candidates"]
            s["applied"] += 1
            s["cands"] += len(c)
            s["uniq_cellsets"] += len({cells(x["Batch"]) for x in c})
            if len({x.get("Response") for x in c}) == 1:
                s["same_response"] += 1
            if all((x.get("Response") or "") == "" for x in c):
                s["all_response_pass"] += 1
            if any(x.get("Source") == "Supplement" for x in c):
                s["has_supplement"] += 1
            if e["Changed"]:
                s["changed"] += 1
                ch, c0 = c[e["Chosen"]], c[0]
                if first_changed is None:
                    first_changed = turn
                ds = stones(ch["Batch"]) - stones(c0["Batch"])
                deltas_stones.append(ds)
                deltas_before.append(int(c0["Before"]) - int(ch["Before"]))
                if ch.get("Source") == "Supplement":
                    s["changed_to_supplement"] += 1
                one_ch = int(ch["After"]) - int(ch.get("TwoPlyBonus", 0))
                one_c0 = int(c0["After"]) - int(c0.get("TwoPlyBonus", 0))
                if one_ch < one_c0:
                    two_ply_driven += 1
                s["changed_build" if rnd <= 3 else "changed_open"] += 1
                s["changed_fewer" if ds < 0 else ("changed_more" if ds > 0 else "changed_equal")] += 1
        firsts.append((g["seed"], first_changed))
    s["two_ply_driven"] = two_ply_driven
    return dict(s), deltas_stones, deltas_before, firsts


def first_divergence(a, b):
    for i, (x, y) in enumerate(zip(a, b)):
        if x != y:
            return i + 1
    return None if len(a) == len(b) else min(len(a), len(b)) + 1


def sign_p(good, bad):
    n = good + bad
    if n == 0:
        return 1.0
    k = min(good, bad)
    p = sum(math.comb(n, i) for i in range(k + 1)) / 2 ** n
    return min(1.0, 2 * p)


data = {}
for arm in ARMS:
    files = sorted(glob.glob(os.path.join(ROOT, arm, "match-*.jsonl")))
    data[arm] = {g["seed"]: g for g in map(per_game, files)}

summary = {}
H = data["H"]
for arm in ARMS:
    gs = list(data[arm].values())
    ranks = [g["rank"] for g in gs if g["rank"] is not None]
    row = {
        "games": len(gs),
        "wins": sum(g["win"] for g in gs),
        "mean_rank": round(statistics.mean(ranks), 3) if ranks else None,
        "reasons": dict(Counter(g["reason"] for g in gs)),
        "rounds_median": statistics.median(g["rounds"] for g in gs),
        "acted": sum(g["acted"] for g in gs),
        "placements": sum(g["placements"] for g in gs),
        "passes": sum(g["passes"] for g in gs),
        "stalls": sum(g["stalls"] for g in gs),
        "captures": sum(g["captures"] for g in gs),
        "final_stones": sum(g["final_stones"] or 0 for g in gs),
    }
    pr, ar = Counter(), Counter()
    ph = defaultdict(lambda: [0, 0, 0])
    for g in gs:
        pr.update(g["pass_by_rank"]); ar.update(g["act_by_rank"])
        for k, v in g["by_phase"].items():
            for i in range(3):
                ph[k][i] += v[i]
    row["pass_by_rank_before"] = {k: f"{pr[k]}/{ar[k]}" for k in sorted(ar)}
    row["by_phase(turns,stones,passes)"] = dict(ph)
    if arm != "H":
        good = same = bad = 0
        pairs = []
        for seed, g in sorted(data[arm].items()):
            h = H.get(seed)
            if h is None or g["rank"] is None or h["rank"] is None:
                continue
            d = h["rank"] - g["rank"]  # >0：专家名次更好
            good += d > 0; same += d == 0; bad += d < 0
            div = first_divergence(g["_sig"], h["_sig"])
            pairs.append({"seed": seed, "rank_E": g["rank"], "rank_H": h["rank"], "first_div_turn": div,
                          "rounds_E": g["rounds"], "rounds_H": h["rounds"],
                          "stones_E": g["placements"], "stones_H": h["placements"],
                          "pass_E": g["passes"] + g["stalls"], "pass_H": h["passes"] + h["stalls"]})
        row["paired(good/same/bad)"] = f"{good}/{same}/{bad}"
        row["sign_p"] = round(sign_p(good, bad), 4)
        la, dst, dbf, firsts = lookahead_stats(gs)
        row["lookahead"] = la
        row["changed_stone_delta"] = dict(Counter(dst))
        row["changed_before_loss_median"] = statistics.median(dbf) if dbf else None
        fc = dict(firsts)
        for p in pairs:
            p["first_changed_turn"] = fc.get(p["seed"])
            p["div_matches_change"] = p["first_div_turn"] == p["first_changed_turn"]
        row["pairs"] = pairs
        # 每大回合末势力：专家 − 同种子 H 臂 P0（中位数），看从哪一段开始落后
        gap = defaultdict(list)
        for seed, g in data[arm].items():
            h = H.get(seed)
            if not h:
                continue
            for r in (3, 4, 6, 8, 12, 16, 20):
                if r in g["power_by_round"] and r in h["power_by_round"]:
                    gap[r].append(g["power_by_round"][r] - h["power_by_round"][r])
        row["power_gap_median_by_round"] = {r: statistics.median(v) for r, v in sorted(gap.items())}
        row["power_gap_neg_count_by_round"] = {r: f"{sum(x < 0 for x in v)}-/{sum(x > 0 for x in v)}+/{len(v)}" for r, v in sorted(gap.items())}
    summary[arm] = row

with open(os.path.join(ROOT, "summary.json"), "w", encoding="utf-8") as f:
    json.dump(summary, f, ensure_ascii=False, indent=1, default=str)
print(json.dumps(summary, ensure_ascii=False, indent=1, default=str))
