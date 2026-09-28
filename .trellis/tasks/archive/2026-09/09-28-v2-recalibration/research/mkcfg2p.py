# v2-recalibration 段 B（2.5 / 2.6）：2 人图权重扫档配置。siege-2p-base-v1、2 × Standard、种子 1–20、每批 20 局、V2、带入 0、截断 600、
# 停手阈值写死为段 A 选定值 20；九维权重逐玩家写死，只改 Eye / EnemyLoss，其余七维等于 EvaluationWeights.Default。字段集与段 A mkcfg.py 相同。
# 用法：python mkcfg2p.py [<Eye>:<EnemyLoss> ...]   不给参数时写第 1–5 批；条件批次（2.6）按参数追加。
import json, os, sys
W = {"PowerGain": 10, "EnemyLoss": 8, "Relic": 6, "Safety": 35, "Growth": 4, "Initiative": 20, "Supply": 2, "Eye": 200, "Threat": 25}
THRESHOLD = 20
HERE = os.path.dirname(os.path.abspath(__file__))


def name(eye, el):
    return f'2p-eye{eye}-el{el}'


def cfg(eye, el):
    w = dict(W); w.update(Eye=eye, EnemyLoss=el)
    c = {"MapId": "siege-2p-base-v1", "Players": [{"Difficulty": "Standard", "Weights": dict(w)} for _ in range(2)],
         "SeedStart": 1, "Count": 20, "Parallelism": 0, "TurnLimit": 600, "PassThreshold": THRESHOLD,
         "ContentSet": "V2", "CarryIn": 0, "EventRetention": "SnapshotsOnly"}
    path = os.path.join(HERE, 'configs', name(eye, el) + '.json')
    json.dump(c, open(path, 'w', encoding='utf-8'), indent=1)
    print(path)


if __name__ == '__main__':
    points = [tuple(int(x) for x in a.split(':')) for a in sys.argv[1:]] or [(200, 8), (100, 8), (50, 8), (200, 16), (200, 24)]
    for e, l in points:
        cfg(e, l)
