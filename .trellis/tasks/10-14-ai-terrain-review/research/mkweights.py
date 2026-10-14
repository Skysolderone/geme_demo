# ai-terrain-review 1.6：由 1.5 主题批次实际生效的 config.json 派生 6 档权重对照配置（安全 / 眼位 / 威胁 × 减半 / 加倍；整数权重，减半向下取整），
# 4 名 AI 全员同改，其余字段与 1.5 相同，只把局数改为 10（link:1–link:10、种子 1–10）。
import json, os, sys

ROOT = r'E:\wws\geme_demo'
base_path = os.path.join(ROOT, 'sim-out', 'ai-terrain-review', 'themes', 'config.json')
out_dir = sys.argv[1]
base = json.load(open(base_path, encoding='utf-8'))
for key in ('PlayerCount', 'EffectiveParallelism'):
    base.pop(key, None)
assert base['MapId'] == 'link:1' and base['MapPerMatch'] is True and base['SeedStart'] == 1, base
default = base['Players'][0]['Weights']
assert all(p['Weights'] == default for p in base['Players']) and len(base['Players']) == 4
print('基准权重', default)
os.makedirs(out_dir, exist_ok=True)
for dim, key in (('safety', 'Safety'), ('eye', 'Eye'), ('threat', 'Threat')):
    for level, f in (('half', lambda v: v // 2), ('double', lambda v: v * 2)):
        cfg = json.loads(json.dumps(base))
        cfg['Count'] = 10
        for p in cfg['Players']:
            p['Weights'][key] = f(default[key])
        path = os.path.join(out_dir, f'weights-{dim}-{level}.json')
        json.dump(cfg, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
        print(path, key, cfg['Players'][0]['Weights'][key])
