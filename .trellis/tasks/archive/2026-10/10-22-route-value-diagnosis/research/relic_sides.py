"""route-value-diagnosis 第 2 件事：隔断线两侧公共信物的终局归属（只读统计日志）。

用法：python relic_sides.py <geometry.csv 或 -> <标签=日志目录> ...
- 新跑日志（当前生成器）用探针导出的 geometry.csv 给出每格相对隔断线的一侧（core / line / far / none）；
- 旧冒烟日志（生成器较旧，不回放）按生成器构造口径归侧：河流公共信物恒在"不含核心区的一侧"（far），
  高台公共信物恒在山脊线上（line），出生信物恒在核心区（core）。
"""
import csv
import glob
import json
import os
import sys
from collections import Counter, defaultdict

LETTERS = "ABCDEFGHJKLMNOPQRSTUVWXYZ"


def parse(n):
    i = 0
    while i < len(n) and n[i].isalpha():
        i += 1
    col, row = n[:i].upper(), int(n[i:])
    x = LETTERS.index(col[0]) if len(col) == 1 else len(LETTERS) + LETTERS.index(col[0]) * len(LETTERS) + LETTERS.index(col[1])
    return x, row - 1


def board_of(boards, cell):
    x, y = parse(cell)
    for i, b in enumerate(boards):
        ox, oy = parse(b["Origin"])
        if ox <= x < ox + b["Width"] and oy <= y < oy + b["Height"]:
            return i
    return None


def load_geometry(path):
    geo = {}
    if path == "-" or not os.path.exists(path):
        return geo
    with open(path, encoding="utf-8") as f:
        for r in csv.DictReader(f):
            geo[(int(r["seed"]), r["cell"])] = r["side"]
    return geo


def side_by_construction(theme, zone):
    if zone == "BirthZone":
        return "core"
    return {"River": "far", "Plateau": "line"}.get(theme, "none")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    geo = load_geometry(sys.argv[1])
    rows = []
    for spec in sys.argv[2:]:
        label, d = spec.split("=", 1)
        for path in sorted(glob.glob(os.path.join(d, "match-*.jsonl"))):
            recs = [json.loads(l) for l in open(path, encoding="utf-8")]
            h = recs[0]
            turns = [r for r in recs if r["Kind"] == "turn"]
            if not turns:
                continue
            seed = int(h["Seed"], 16)
            map_seed = int(h["MapId"].split(":")[1])
            boards = h["Boards"]
            zones = h["Zones"]
            lockers = defaultdict(set)
            for p, z in enumerate(zones):
                lockers[z].add(p)
            last = turns[-1]
            final = {r["Coord"]: r for r in last["Relics"]}
            stones = {}
            for ps in last["PlayersState"]:
                stones[ps["Player"]] = {s for g in ps.get("Groups", []) for s in g["Stones"]}
            bridges = Counter()
            for t in turns:
                for e in t.get("Edits") or []:
                    if e["Action"] == "Bridge":
                        tgt = e["Target"].split(":", 1)[1]
                        b = board_of(boards, tgt)
                        side = geo.get((map_seed, tgt), "?") if geo and label == "new" else "?"
                        bridges[(b, side)] += 1
            for rel in h["Relics"]:
                cell = rel["Coord"]
                b = board_of(boards, cell)
                if b is None:
                    continue
                theme = boards[b]["Theme"]
                side = geo.get((map_seed, cell)) if (geo and label == "new") else side_by_construction(theme, rel["Zone"])
                fr = final.get(cell, {})
                ctl, holder = fr.get("Control"), fr.get("Holder")
                lk = lockers.get(b, set())
                if holder is None:
                    owner = "none"
                elif holder in lk:
                    owner = "locker"
                else:
                    owner = "other"
                # 控制史：快照里由锁定者控制（Controlled）的小回合占比
                held_locker = held_other = 0
                first = None
                for t in turns:
                    r = next((x for x in t["Relics"] if x["Coord"] == cell), None)
                    if r and r.get("Control") == "Controlled":
                        if r.get("Holder") in lk:
                            held_locker += 1
                        else:
                            held_other += 1
                        if first is None:
                            first = "locker" if r.get("Holder") in lk else "other"
                far_stones = None
                if geo and label == "new" and lk:
                    far_cells = {c for (s, c), sd in geo.items() if s == map_seed and sd == "far" and board_of(boards, c) == b}
                    far_stones = sum(len(stones.get(p, set()) & far_cells) for p in lk)
                rows.append({
                    "set": label, "seed": seed, "map": h["MapId"], "board": b, "theme": theme, "relicZone": rel["Zone"],
                    "cell": cell, "side": side, "lockers": len(lk), "final": ctl, "owner": owner,
                    "heldLocker": held_locker, "heldOther": held_other, "turns": len(turns), "first": first or "never",
                    "lockerFarStones": far_stones,
                    "bridgesOnBoard": sum(v for (bb, _), v in bridges.items() if bb == b),
                    "bridgesFarOrLine": sum(v for (bb, sd), v in bridges.items() if bb == b and sd in ("far", "line")),
                })
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data", "relic_sides.csv")
    with open(out, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)

    def table(filter_fn, title):
        print(f"\n## {title}")
        groups = defaultdict(list)
        for r in rows:
            if filter_fn(r):
                key = (r["set"], r["theme"], r["relicZone"], r["side"])
                groups[key].append(r)
        print("set | theme | zone | side | n | 无锁定者(终局有人/无人) | 终局:锁定者/他人/无人 | 其中Blocked | 先控:锁定者/他人/从未 | 锁定者控制小回合占比 | 他人控制占比")
        for key in sorted(groups):
            g = groups[key]
            locked = [r for r in g if r["lockers"] > 0]
            c = Counter(r["owner"] for r in locked)
            blocked = sum(1 for r in locked if r["final"] == "Blocked")
            first = Counter(r["first"] for r in locked)
            tl = sum(r["heldLocker"] for r in locked)
            to = sum(r["heldOther"] for r in locked)
            tt = sum(r["turns"] for r in locked) or 1
            un = [r for r in g if r["lockers"] == 0]
            uh = sum(1 for r in un if r["owner"] != "none")
            print(f"{key[0]} | {key[1]} | {key[2]} | {key[3]} | {len(g)} | {len(un)}({uh}/{len(un) - uh}) | {c['locker']}/{c['other']}/{c['none']} | {blocked} | "
                  f"{first['locker']}/{first['other']}/{first['never']} | {100 * tl / tt:.0f}% | {100 * to / tt:.0f}%")

    table(lambda r: r["theme"] in ("River", "Plateau"), "河流 / 高台棋盘上的信物（出生=核心侧，公共=远侧/山脊）")
    table(lambda r: r["relicZone"] == "Contested" and r["theme"] not in ("River", "Plateau"), "其他主题的公共信物（无隔断线）")
    new_far = [r for r in rows if r["set"] == "new" and r["theme"] == "River" and r["relicZone"] == "Contested" and r["lockers"] > 0]
    if new_far:
        print("\n## 新跑：河流棋盘锁定者终局在远侧的棋子数 / 该棋盘搭桥次数（远侧或线上目标）")
        for r in new_far:
            print(f"seed {r['seed']} {r['map']} board {r['board']} relic {r['cell']}({r['side']}) owner={r['owner']} final={r['final']} lockerFarStones={r['lockerFarStones']} bridges={r['bridgesOnBoard']} (line/far {r['bridgesFarOrLine']})")


if __name__ == "__main__":
    main()
