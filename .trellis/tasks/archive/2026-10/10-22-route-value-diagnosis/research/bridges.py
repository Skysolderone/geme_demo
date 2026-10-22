"""route-value-diagnosis：日志里的实际搭桥快照（只读）——次数、目标所在棋盘主题、阶段、是否自己棋盘、是否直接致提子；新跑另按 geometry.csv 标目标在隔断线上 / 湖泊。

用法：python bridges.py <geometry.csv 或 -> <标签=日志目录> ...
"""
import csv
import glob
import json
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from relic_sides import board_of, load_geometry  # noqa: E402


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    geo = load_geometry(sys.argv[1])
    for spec in sys.argv[2:]:
        label, d = spec.split("=", 1)
        c = Counter()
        games = 0
        edits = 0
        for path in sorted(glob.glob(os.path.join(d, "match-*.jsonl"))):
            recs = [json.loads(l) for l in open(path, encoding="utf-8")]
            h = recs[0]
            games += 1
            map_seed = int(h["MapId"].split(":")[1])
            boards = h["Boards"]
            zones = h["Zones"]
            for t in (r for r in recs if r["Kind"] == "turn"):
                for e in t.get("Edits") or []:
                    edits += 1
                    if e["Action"] != "Bridge":
                        continue
                    tgt = e["Target"].split(":", 1)[1]
                    b = board_of(boards, tgt)
                    theme = boards[b]["Theme"] if b is not None else "Public"
                    own = b is not None and zones[e["Player"]] == b
                    phase = "build" if t["MajorRound"] <= 3 else "open"
                    side = geo.get((map_seed, tgt), "?") if label == "new" else "?"
                    c[("all",)] += 1
                    c[("theme", theme)] += 1
                    c[("phase", phase)] += 1
                    c[("own", own)] += 1
                    c[("side", side)] += 1
                    c[("capture", e.get("CausedCapture"))] += 1
        print(f"{label}: 局 {games}，改造 {edits}，搭桥 {c[('all',)]}；" + "，".join(f"{k[0]}={k[1]}:{v}" for k, v in sorted(c.items(), key=str) if k != ("all",)))


if __name__ == "__main__":
    main()
