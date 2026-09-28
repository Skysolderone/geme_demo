# 生成本任务全部跑局配置（每配置 20 局、种子 1–20、内容集 V2、带入 0、截断 600）。用法：python mkcfg.py
import json, os
W = {"PowerGain": 10, "EnemyLoss": 8, "Relic": 6, "Safety": 35, "Growth": 4, "Initiative": 20, "Supply": 2, "Eye": 200, "Threat": 25}
def cfg(name, mp, n, diff, thr=80, w=None, content='V2'):
    ww = dict(W); ww.update(w or {})
    c = {"MapId": mp, "Players": [{"Difficulty": diff, "Weights": ww} for _ in range(n)],
         "SeedStart": 1, "Count": 20, "Parallelism": 0, "TurnLimit": 600, "PassThreshold": thr,
         "ContentSet": content, "CarryIn": 0, "EventRetention": "SnapshotsOnly"}
    json.dump(c, open(os.path.join(os.path.dirname(__file__), 'configs', name + '.json'), 'w', encoding='utf-8'), indent=1)
cfg('b01-2p-std', 'siege-2p-base-v1', 2, 'Standard')
cfg('b02-2p-std-thr0', 'siege-2p-base-v1', 2, 'Standard', thr=0)
cfg('b03-2p-std-thr40', 'siege-2p-base-v1', 2, 'Standard', thr=40)
cfg('b04-2p-std-threat50', 'siege-2p-base-v1', 2, 'Standard', w={"Threat": 50})
cfg('b05-3p-std', 'siege-3p-base-v1', 3, 'Standard')
cfg('b06-v5-std', 'siege-4p-base-v5', 4, 'Standard')
cfg('b07-v5-std-thr0', 'siege-4p-base-v5', 4, 'Standard', thr=0)
cfg('b08-v5-easy', 'siege-4p-base-v5', 4, 'Easy')
cfg('b09-2p-std-eye50', 'siege-2p-base-v1', 2, 'Standard', w={"Eye": 50})
cfg('b10-v5-easy-v1content', 'siege-4p-base-v5', 4, 'Easy', content='V1')
