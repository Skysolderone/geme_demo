# 围杀 Siege 首轮原型 — 父任务执行计划

父任务不直接实现，只负责排期、集成与跨子任务验收。

## 0. 启动前（阻塞全部子任务）

- [x] 0.1 确定技术栈：Godot 4 / C#，规则内核为零 Godot 依赖的 net8.0 类库（见 `design.md`）
- [x] 0.2 `git init` 并建立首个提交
- [x] 0.3 建立 `.trellis/spec/core/` 编码规范（边界与依赖、确定性、坐标、测试组织）
- [x] 0.4 补全各子任务 `design.md` 的实现侧补充与 `implement.md` 的验证命令

## 1. 按依赖顺序推进子任务

- [ ] 1.1 `09-12-board-core`
- [ ] 1.2 `09-12-batch-deployment`
- [ ] 1.3 `09-12-territory-power`
- [ ] 1.4 `09-12-relic-system`
- [ ] 1.5 `09-12-recruit-hand`
- [ ] 1.6 `09-12-match-flow`
- [ ] 1.7 `09-12-heuristic-ai` 与 `09-12-tactical-ui`（可并行）

每个子任务完成后：`openspec archive <change>` 把其 spec 合并进 `openspec/specs/`，使规格基线随实现推进。

## 2. 集成验收

- [ ] 2.1 端到端跑通 4 人 AI 对局，连续 100 局无异常终止
- [ ] 2.2 同种子 + 同决策序列完整重放，逐步一致
- [ ] 2.3 三个最高危陷阱的强制回归全部通过
- [ ] 2.4 执行 2000 局基线跑局并归档报告（§16 六项目标 + §17 七个方向）
- [ ] 2.5 依据报告决定数值调整方向——调整落在征募权重 / 信物生成预算 / 地图，不在计分与测量层

## 验证命令

```bash
openspec validate --all
openspec list
python3 ./.trellis/scripts/task.py list
```
