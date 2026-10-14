import re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'E:\wws\geme_demo\sim-out\ai-terrain-review'
DIRS = ['themes-seed1-10', 'weights-safety-half', 'weights-safety-double', 'weights-eye-half', 'weights-eye-double', 'weights-threat-half', 'weights-threat-double', 'themes']
THEMES = ['高台', '河流', '林地', '栅栏阵', '混合']


def grab(pattern, text, cast=str, default='?'):
    m = re.search(pattern, text)
    return cast(m.group(1)) if m else default


rows = []
for d in DIRS:
    t = open(f'{ROOT}\\{d}\\report.txt', encoding='utf-8').read()
    r = {'档': d}
    r['长度中位'] = grab(r'终局局的结束大回合：中位 ([\d.]+)', t)
    r['长度均值'] = grab(r'结束大回合分布：[^；]+；平均 [^实]+实测 ([\d.]+) 大回合', t)
    r['首次提子大回合'] = grab(r'- 平均：偏离：[^实]+实测 ([\d.]+) 大回合', t)
    r['首轮交战'] = grab(r'首轮交战 ([\d.]+)', t)
    r['决赛'] = grab(r'决赛 ([\d.]+)（到达决赛', t)
    r['战场落败'] = grab(r'战场落败 (\d+) 人次', t)
    r['决胜一手'] = grab(r'触发 (\d+) 次（单点', t)
    r['提子'] = grab(r'已确认批次 \d+，提子 (\d+)', t)
    r['R3领先者胜'] = grab(r'口径 A（并列组内任一人获胜）：([\d.]+%)', t)
    for k in ('搭桥', '立栅', '烧林'):
        r[k] = grab(rf'  - {k}：(\d+) 次', t)
    r['匠人时刻'] = grab(r'匠人可改造时刻 (\d+) 次', t)
    r['第一名类别'] = grab(r'单点排序第一名的类别：([^；]+)', t)
    perf = t[t.index('## 主题表现'):]
    for th in THEMES:
        m = re.search(rf'- {th}：锁定 (\d+) 人次；首轮战场胜 (\d+)/(\d+)（([^；]+)；.*?平均名次 ([\d.]+|无样本).*?胜 (\d+)；第 3 大回合结束时势力 均值 ([\d.]+)', perf)
        r[th] = f'{m.group(2)}/{m.group(3)}' if m else '?'
        r[th + '名次'] = m.group(5) if m else '?'
        r[th + '胜'] = m.group(6) if m else '?'
        r[th + 'R3'] = m.group(7) if m else '?'
    rows.append(r)

keys = list(rows[0].keys())
print('| ' + ' | '.join(keys) + ' |')
print('|' + '---|' * len(keys))
for r in rows:
    print('| ' + ' | '.join(str(r[k]) for k in keys) + ' |')
