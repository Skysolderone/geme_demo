import sys, re, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding='utf-8')
from mapjson import *
old_dir, new_dir, after_json, out = sys.argv[1:5]
m = load(after_json); b = load(after_json.replace('after', 'before'))
special = {}
for y in range(m['Height']):
    for x in range(m['Width']):
        if m['_s']((x, y)) in 'DMPS' and b['_s']((x, y)) == 'G': special[(x, y)] = m['_s']((x, y))
near = set(n for c in special for n in nb(c))
KIND = {'D': '荒漠', 'M': '沼泽', 'P': '岩台', 'S': '浅滩'}
lines = [f"link:1 新地表格：{' '.join(name(c) + KIND[k] for c, k in sorted(special.items(), key=lambda kv: (kv[0][1], kv[0][0])))}", '']
coord = re.compile(r'(?<![A-Z])([A-HJ-Z]{1,2}\d{1,2})(?=[:,\s-]|$)')
def tag(cs):
    res = []
    for n in cs:
        c = parse(n)
        res.append(n + ('[新地表]' if c in special else '[四邻]' if c in near else ''))
    return res
for f in ['decisions-link1-seed1.txt', 'decisions-link1-seed2.txt', 'decisions-link1-seed3.txt', 'decisions-link1-easy-seed1.txt', 'decisions-link1-hard-seed1.txt']:
    o = [l for l in open(f'{old_dir}/{f}', encoding='utf-8').read().replace('\r\n', '\n').split('\n')]
    n = [l for l in open(f'{new_dir}/{f}', encoding='utf-8').read().replace('\r\n', '\n').split('\n')]
    od = [l for l in o if l and not l.startswith('#')]
    nd = [l for l in n if l and not l.startswith('#')]
    i = next((k for k in range(min(len(od), len(nd))) if od[k] != nd[k]), None)
    lines.append(f'## {f}')
    lines.append(f'  首部：{[l for l in n if l.startswith("# map")][0]}')
    lines.append(f'  旧结尾：{o[-2] if o[-1] == "" else o[-1]}')
    lines.append(f'  新结尾：{n[-2] if n[-1] == "" else n[-1]}')
    if i is None:
        lines.append('  决策行逐字节相同'); continue
    lines.append(f'  首个分歧：第 {i + 1} 个小回合（大回合 {nd[i].split(chr(9))[0]}，{nd[i].split(chr(9))[1]}）；此前 {i} 个小回合逐字相同；分歧小回合总数 {sum(1 for a, c in zip(od, nd) if a != c)}/{max(len(od), len(nd))}')
    lines.append(f'    旧：{od[i]}')
    lines.append(f'    新：{nd[i]}')
    oc = coord.findall(od[i].split('\t')[2]); nc = coord.findall(nd[i].split('\t')[2])
    lines.append(f'    旧落点：{" ".join(tag(oc))}')
    lines.append(f'    新落点：{" ".join(tag(nc))}')
    hit = any(parse(x) in special or parse(x) in near for x in set(oc) ^ set(nc)) or any(parse(x) in special or parse(x) in near for x in oc + nc)
    lines.append(f'    分歧落点在新地表格或其四邻：{"是" if hit else "否"}')
    lines.append('')
open(out, 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('\n'.join(lines))
