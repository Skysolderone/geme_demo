"""route-value-diagnosis 第 1 / 3 件事：汇总探针导出的 decisions.csv / options.csv（只读）。

口径（详见 report.md）：
- 决策局面 = AI 部署前、手里有匠人且部署上限 > 0；"有搭桥机会" = 合法落子范围内某空格对某深水格的搭桥是规则合法目标（TerrainEditRules.LegalTargets 的唯一实现）。
- 可达性增量 d* = 带搭桥的匠人单点 − 同格不改造的匠人单点（两者都在预演后的盘面上算，含提子）；c* = 相对当前盘面（不落子）。
  U = 无界：己方棋子沿气边经空的可落子格可达的空格数；URelic = 其中信物格；B3 = 深度 ≤ 3；B3Pub = 深度 ≤ 3 的公共信物格；
  Dist = 全部空信物格到己方最近距离（上限 15）之和的下降；DistPub 只算公共信物。
- 评价分差 = 最佳搭桥单点收益（或以它为起点续贪心的批次收益）− AI 实际批次收益；收益 = 加权总分 − Pass 总分。
"""
import csv
import os
import statistics
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))


def load(name):
    with open(os.path.join(HERE, "data", name), encoding="utf-8") as f:
        return list(csv.DictReader(f))


def num(x):
    return None if x in ("", None) else int(x)


def dist(vals):
    vals = sorted(v for v in vals if v is not None)
    if not vals:
        return "n=0"
    p90 = vals[min(len(vals) - 1, int(0.9 * len(vals)))]
    nz = sum(1 for v in vals if v != 0)
    return f"n={len(vals)} 中位 {statistics.median(vals):g} p90 {p90} 最大 {vals[-1]} 最小 {vals[0]} 非零 {nz}"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    dec = load("decisions.csv")
    opt = load("options.csv")
    # 阶段按大回合（≤ 3 构筑期）：批次上下文的 EditScope 在全图期也非 null，不能用来分阶段。
    for r in dec + opt:
        r["build"] = "1" if int(r["round"]) <= 3 else "0"
    print(f"决策局面（匠人在手且上限>0）: {len(dec)}；局数 {len(set(r['seed'] for r in dec))}")
    has = [r for r in dec if int(r["bridgeOptions"]) > 0]
    legal = [r for r in has if int(r["bridgeLegal"]) > 0]
    print(f"有搭桥规则合法目标: {len(has)}；其中至少一个搭桥单点预演合法+过活形硬约束: {len(legal)}")
    print("按阶段:", Counter("build" if r["build"] == "1" else "open" for r in has))
    print("实际选择(有机会的局面):", Counter(r["actualChoice"] for r in has))
    print("实际搭桥(全部局面):", sum(1 for r in dec if "Bridge" in r["actualChoice"]))
    print("诊断处境 StatusOf(Bridge):", Counter(r["status"] for r in has))
    print("最佳搭桥目标主题:", Counter(r["bestBridgeTheme"] for r in legal), " 自己棋盘:", Counter(r["bestBridgeOwnBoard"] for r in legal), " 侧:", Counter(r["bestBridgeSide"] for r in legal))

    # ---------- 第 1 件事：可达性增量 ----------
    print("\n## 1. 可达性增量（每个合法搭桥选项，相对同格不改造）")
    lo = [r for r in opt if r["legal"] == "1" and r["gainNoEdit"] != ""]
    print(f"合法搭桥选项 {sum(1 for r in opt if r['legal']=='1')} / 全部 {len(opt)}；同格不改造也合法 {len(lo)}")
    for k in ["dU", "dURelic", "dB3", "dB3Relic", "dB3Pub", "dDist", "dDistPub"]:
        print(f"  {k}: {dist([num(r[k]) for r in lo])}")
    print("  按主题 / 阶段:")
    groups = defaultdict(list)
    for r in lo:
        t = r["theme"] if r["theme"] in ("River", "Plateau") else "Other"
        groups[(t, "build" if r["build"] == "1" else "open")].append(r)
    for key in sorted(groups):
        g = groups[key]
        print(f"   {key}: dU[{dist([num(r['dU']) for r in g])}] | dB3[{dist([num(r['dB3']) for r in g])}] | dB3Relic[{dist([num(r['dB3Relic']) for r in g])}] | dDist[{dist([num(r['dDist']) for r in g])}]")
    print("  按目标侧(river line / 其他):", Counter(r["side"] for r in lo))
    for side in sorted(set(r["side"] for r in lo)):
        g = [r for r in lo if r["side"] == side]
        print(f"   side={side}: dU[{dist([num(r['dU']) for r in g])}] | dB3[{dist([num(r['dB3']) for r in g])}] | dDist[{dist([num(r['dDist']) for r in g])}]")

    # 每个局面取可达性最大的选项（dB3 优先，其次 dDist）与评价最佳的选项
    by_pos = defaultdict(list)
    for r in lo:
        by_pos[(r["seed"], r["turn"])].append(r)
    best_reach = {k: max(v, key=lambda r: (int(r["dB3"]), int(r["dDist"]), int(r["dU"]))) for k, v in by_pos.items()}
    best_eval = {k: max(v, key=lambda r: int(r["gain"])) for k, v in by_pos.items()}
    print(f"\n  每局面（{len(by_pos)} 个）可达性最大的搭桥: dU[{dist([num(r['dU']) for r in best_reach.values()])}] dB3[{dist([num(r['dB3']) for r in best_reach.values()])}] dB3Relic[{dist([num(r['dB3Relic']) for r in best_reach.values()])}] dDist[{dist([num(r['dDist']) for r in best_reach.values()])}] dDistPub[{dist([num(r['dDistPub']) for r in best_reach.values()])}]")
    print(f"  每局面评价最佳的搭桥: dU[{dist([num(r['dU']) for r in best_eval.values()])}] dB3[{dist([num(r['dB3']) for r in best_eval.values()])}] dDist[{dist([num(r['dDist']) for r in best_eval.values()])}]")
    print(f"  相对当前盘面（不落子）的评价最佳搭桥: cU[{dist([num(r['cU']) for r in best_eval.values()])}] cB3[{dist([num(r['cB3']) for r in best_eval.values()])}] cDist[{dist([num(r['cDist']) for r in best_eval.values()])}]")

    # ---------- 第 3 件事：评价分差 ----------
    print("\n## 3. 评价分差（最佳搭桥 − 实际批次）")
    rows = [r for r in legal if r["actualGain"] not in ("", "{ACTUAL}")]
    single = [num(r["bestBridgeGain"]) - num(r["actualGain"]) for r in rows]
    forced = [num(r["forcedBatchGain"]) - num(r["actualGain"]) for r in rows if r["forcedBatchGain"] != ""]
    print(f"  单点差: {dist(single)}")
    print(f"  含它的批次差: {dist(forced)}")
    for name, vals in (("单点", single), ("批次", forced)):
        print(f"   {name}: ≥0 {sum(v >= 0 for v in vals)}；(-20,0) {sum(-20 < v < 0 for v in vals)}；[-100,-20] {sum(-100 <= v <= -20 for v in vals)}；<-100 {sum(v < -100 for v in vals)}；|差|≤20 {sum(abs(v) <= 20 for v in vals)}；|差|≤100 {sum(abs(v) <= 100 for v in vals)} / {len(vals)}")
    nb = [r for r in rows if "Bridge" not in r["actualChoice"]]
    nbf = [num(r["forcedBatchGain"]) - num(r["actualGain"]) for r in nb if r["forcedBatchGain"] != ""]
    print(f"  未搭桥的局面（{len(nb)}）批次差: {dist(nbf)}；|差|≤20 {sum(abs(v) <= 20 for v in nbf)}；≤100 {sum(abs(v) <= 100 for v in nbf)}")
    print(f"  最佳搭桥单点收益本身: {dist([num(r['bestBridgeGain']) for r in rows])}；Top 收益: {dist([num(r['topGain']) for r in rows if r['topGain']])}；实际收益: {dist([num(r['actualGain']) for r in rows])}")
    print(f"  最佳搭桥单点收益 > 停手阈值 的局面: {sum(1 for r in rows if num(r['bestBridgeGain']) > num(r['passThreshold']))} / {len(rows)}")
    # 编辑效果（改造本身，九维）
    dims = ["PowerGain", "EnemyLoss", "Relic", "Safety", "Growth", "Initiative", "Supply", "Eye", "Threat"]
    eff = [list(map(int, r["editEffect"].split("|"))) for r in has if r["editEffect"]]
    if eff:
        print("  诊断里'最佳搭桥 − 同格不改造'的九维贡献差（中位 / 最小 / 最大）: " + "; ".join(
            f"{d} {statistics.median(c[i] for c in eff):g}/{min(c[i] for c in eff)}/{max(c[i] for c in eff)}" for i, d in enumerate(dims)))

    print("  批次差按阶段 / 诊断处境:")
    for key, fn in (("phase", lambda r: "build" if r["build"] == "1" else "open"), ("status", lambda r: r["status"]), ("actual", lambda r: "bridge" if "Bridge" in r["actualChoice"] else ("pass" if r["actualChoice"] == "Pass" else "other"))):
        g = defaultdict(list)
        for r in rows:
            g[fn(r)].append(num(r["forcedBatchGain"]) - num(r["actualGain"]))
        for k in sorted(g):
            v = g[k]
            print(f"   {key}={k}: {dist(v)}；>20 {sum(x > 20 for x in v)}；|差|≤20 {sum(abs(x) <= 20 for x in v)}；|差|≤100 {sum(abs(x) <= 100 for x in v)}")
    sv = [num(r["bestBridgeGain"]) - num(r["topGain"]) for r in rows if r["topGain"]]
    print(f"  同口径单点差（最佳搭桥单点 − 单点第一名）: {dist(sv)}；=0 {sum(v == 0 for v in sv)}；≥-20 {sum(v >= -20 for v in sv)}；≥-100 {sum(v >= -100 for v in sv)}")
    pw = [r for r in rows if r["actualChoice"] == "Pass"]
    print(f"  实际 Pass 的局面 {len(pw)}：含最佳搭桥的批次收益 > 停手阈值 {sum(1 for r in pw if num(r['forcedBatchGain']) > num(r['passThreshold']))}")

    # 可达性 × 分差
    print("\n## 3b. 可达性 × 分差（局面级：可达性最大的搭桥选项，其单点收益 − 单点第一名收益）")
    act = {(r["seed"], r["turn"]): r for r in rows}
    pairs = []
    for k, r in best_reach.items():
        if k in act:
            top = num(act[k]["topGain"])
            pairs.append((int(r["dB3"]), int(r["dDist"]), int(r["dU"]), int(r["gain"]) - top, r))
    big = [p for p in pairs if p[0] >= 3 or p[1] >= 5]
    print(f"  局面 {len(pairs)}；可达性大增（dB3≥3 或 dDist≥5）{len(big)}；其中分差 < -100: {sum(1 for p in big if p[3] < -100)}；[-100,-20]: {sum(1 for p in big if -100 <= p[3] < -20)}；≥-20: {sum(1 for p in big if p[3] >= -20)}")
    print("  dB3 分桶 → 分差中位:")
    buckets = defaultdict(list)
    for p in pairs:
        buckets[min(p[0], 5)].append(p[3])
    for b in sorted(buckets):
        print(f"   dB3={b}{'+' if b == 5 else ''}: n={len(buckets[b])} 分差中位 {statistics.median(buckets[b]):g}")
    print("  典型局面（可达性大增、分差最负 / 最接近）:")
    for p in sorted(big, key=lambda p: (-p[0] - p[1], p[3]))[:6]:
        r = p[4]
        print(f"   seed {r['seed']} turn {r['turn']} round {r['round']} P{r['player']} {r['cell']}+{r['target']} theme {r['theme']} side {r['side']} own {r['ownBoard']} dU {r['dU']} dB3 {r['dB3']} dB3Relic {r['dB3Relic']} dDist {r['dDist']} gain {r['gain']} noEdit {r['gainNoEdit']} top {act[(r['seed'], r['turn'])]['topGain']} actual {act[(r['seed'], r['turn'])]['actualGain']} forcedBest {act[(r['seed'], r['turn'])]['forcedBatchGain']} actualChoice {act[(r['seed'], r['turn'])]['actualChoice']}")

    # 实际搭桥的局面
    print("\n## 实际搭桥的局面")
    for r in [r for r in dec if "Bridge" in r["actualChoice"]]:
        o = [x for x in opt if x["seed"] == r["seed"] and x["turn"] == r["turn"] and f"{x['cell']}>{x['target']}" in r["actualTargets"]]
        extra = "; ".join(f"dU {x['dU']} dB3 {x['dB3']} dDist {x['dDist']} theme {x['theme']} side {x['side']} own {x['ownBoard']} gain {x['gain']}" for x in o)
        print(f"   seed {r['seed']} turn {r['turn']} round {r['round']} P{r['player']} {r['actualTargets']} actualGain {r['actualGain']} bestBridge {r['bestBridgeGain']} status {r['status']} | {extra}")


if __name__ == "__main__":
    main()
