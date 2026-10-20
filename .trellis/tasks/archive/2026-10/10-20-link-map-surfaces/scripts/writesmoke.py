import sys
stats=open(sys.argv[1],encoding='utf-8').read()
an=open('.trellis/tasks/10-20-link-map-surfaces/analyze-all.txt',encoding='utf-8').read().splitlines()
pick=[l for l in an if l.startswith('- 终局局的结束大回合') or '上限结算' in l or l.startswith('- 规则级终局')]
i=next(k for k,l in enumerate(an) if l.startswith('## 主题表现'))
theme=an[i:i+7]
nl=chr(10)
doc=f'''# link-map-surfaces 冒烟（tasks 3.1 / 裁决 E8，只报数不调参）

> 命令（单进程 `--serial`，Release exe，标准 ×4）：
> - `run --out sim-out/link-map-surfaces/link1 --map link:1 --difficulty Standard --seed 1 --count 10 --turn-limit 600 --serial`
> - `run --out sim-out/link-map-surfaces/per-map --map link:100 --map-per-match --difficulty Standard --seed 11 --count 10 --turn-limit 600 --serial`（link:100–109；`--difficulty Standard` 与 open-map 段 A 冒烟同口径，派发原文未写，缺省也是标准）
> - `analyze --dir sim-out/link-map-surfaces/all`（两批 20 份日志复制到同一目录；完整报告 `analyze-all.txt`）
>
> 统计（大回合、终局原因、主题名次、新地表落子与被占）由脚本（scratch `smokestats.py`）按日志 header / turn / result 行独立算出；地图取 `export/smoke-maps/`（`map --out` 导出）。
> 新地表"落子次数" = 各小回合 `Placements` 里落在该地表格上的次数；"被占比例" = 每条小回合快照（`PlayersState.Groups.Stones` 并集）里该地表格被占的比例取平均，另给终局快照的比例。全图口径（落子次数 / 可落子格）作对照。

{stats}
## analyze 报告节选（all，20 局）
```
{nl.join(pick)}
{nl.join(theme)}
```

## 与 open-map 段 A 冒烟对照（同命令、同种子、改前地图）

| 指标 | open-map（改前） | link-map-surfaces |
|---|---|---|
| 规则终局 / 截断 | 20 / 0 | 20 / 0 |
| 上限结算 | —（当时无上限） | 0 |
| 大回合 中位 / 最小 / 最大 | 24 / 20 / 29 | 26 / 18 / 33 |
| link:1 ×10 中位 | 22.5 | 25.5 |
| 每局换图 ×10 中位 | 24.5 | 27 |
| 五主题平均名次 | Plateau 2.37 / Mixed 2.50 / Palisade 2.52 / River 2.53 / Forest 2.64 | Mixed 1.92 / Palisade 2.48 / Plateau 2.63 / Forest 2.64 / River 2.73 |
| 锁定人次 | Forest 11 / Mixed 12 / Palisade 23 / Plateau 19 / River 15 | 相同（插旗只由对局种子决定，不受地图影响） |

## 结论（只报数，不调参）

- 20 局全部规则终局（最后战场结算，结算原因全是整轮 Pass），`turn_limit` 截断 0、上限结算 0。
- 大回合中位 26（open-map 24），最小 18、最大 33，略长；20 局样本，差异不可靠。
- 五主题平均名次 1.92–2.73：混合 1.92（12 人次，整局胜 7/12）明显靠前，河流 2.73 垫底（与 design「已知风险」"浅滩空着不算气、河流可能变弱"方向一致，但只有 15 人次）；open-map 时是 2.37–2.64。按 E6 不补偿，列入观察项。
- 新地表落子：四种每格落子 1.10–1.29 次（全图每格 1.08），被占比例逐小回合平均 57–68%、终局 74–98%——AI 并不回避新地表；岩台终局被占最低（73.8%），浅滩落子最多（每格 1.29 次）且终局几乎全被占（98%）。
- 单小回合耗时中位 35 ms（单进程串行；open-map 冒烟 72 ms 为并行跑，口径不同，不比）。
'''
open('.trellis/tasks/10-20-link-map-surfaces/smoke.md','w',encoding='utf-8').write(doc)
