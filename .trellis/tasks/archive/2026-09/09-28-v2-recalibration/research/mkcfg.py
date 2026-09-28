# v2-recalibration 段 A：停手阈值 V2 复核配置（v5、4 × Standard、权重逐玩家写死为当前 EvaluationWeights.Default、种子 1–20、每批 20 局、V2、带入 0、截断 600）。
# 字段集与诊断 mkcfg.py 相同（pass-80 / pass-0 与 b06 / b07 同配置）。用法：python mkcfg.py
import json, os
W = {"PowerGain": 10, "EnemyLoss": 8, "Relic": 6, "Safety": 35, "Growth": 4, "Initiative": 20, "Supply": 2, "Eye": 200, "Threat": 25}
def cfg(name, players, thr):
    c = {"MapId": "siege-4p-base-v5", "Players": [{"Difficulty": d, "Weights": dict(W)} for d in players],
         "SeedStart": 1, "Count": 20, "Parallelism": 0, "TurnLimit": 600, "PassThreshold": thr,
         "ContentSet": "V2", "CarryIn": 0, "EventRetention": "SnapshotsOnly"}
    json.dump(c, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'configs', name + '.json'), 'w', encoding='utf-8'), indent=1)
for t in (0, 20, 40, 80):
    cfg(f'pass-{t}', ['Standard'] * 4, t)
# 1.5 条件冒烟（选定值 ≠ 80 时才跑；段 A 选定 20）：简单 × 4、专家 + 标准 × 3。
cfg('easy20-pass20', ['Easy'] * 4, 20)
cfg('expert20-pass20', ['Expert'] + ['Standard'] * 3, 20)
