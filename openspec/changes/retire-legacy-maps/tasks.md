## 0. 跨机器红测（段 0）

- [x] 0.1 被哈希的日志首部 / `config.json` 去掉机器相关值；查明并修另外 5 条（行尾或其他）（D0）。验证：本机全量全绿；`DOTNET_PROCESSOR_COUNT=8` 与 `=28` 各跑一次同样全绿；`testing.md` 记录根因。

## 1. 测试迁到棋盘图（段 A）

- [x] 1.1 夹具与跑局类测试迁到内置棋盘图，整局类加截断或改用 2 人图（D1）。
- [x] 1.2 只把 v5 当棋盘用的规则测试、相机小图、终端脚本、信物生成测试改写（D1）。
- [ ] 1.3 黄金值与"逐步相同"基线在新图上重定，逐条记录。验证：全量全绿；总耗时不超过改动前 2 倍；改写的断言配变异。

## 2. 删除（段 B）

- [x] 2.1 删旧图类、边疆生成器、新地表投放、`MapSymmetry`、`maps/*.json` 与嵌入资源；迁出 `MapGenerationException`、`FriendlySeed`（D2）。
- [x] 2.2 每局换图支持 `board:`；AI 覆盖表清空；删只测旧图的测试。
- [x] 2.3 `MapCatalog.Resolve` 对旧标识明确报错、删隐式文件回落。验证：`run` / `play` / `replay` / 图形版 `--map=siege-4p-base-v5` 与 `gen:1` 都报"已删除"；全量全绿；两处构建 0 警告。

## 3. 校验器收口（段 C）

- [ ] 3.1 删边疆档与标准档声明及专属规则，`Standard` 报"已删除"（D3）。验证：三张内置棋盘图摘要不变；全量全绿。

## 4. 文档与规格（段 D）

- [ ] 4.1 openspec 增量规格补全（D4），`openspec validate retire-legacy-maps --strict`。
- [ ] 4.2 设计文档 §3 重写并升版；HANDOFF、README、ROADMAP、发布页、`art/*/README.md` 同步。

## 5. 验收（主会话）

- [ ] 5.1 Godot：`--auto-demo` 与 `--pick-check`（缺省图）、选图自检通过；两处构建 0 警告；全量全绿。
- [ ] 5.2 负责人过目。
