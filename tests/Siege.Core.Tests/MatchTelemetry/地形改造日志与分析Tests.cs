using Siege.Core.Board.Maps;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Tests.SimulationHarness;
using Siege.Sim.Analysis;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>
/// 规格：match-telemetry —— Requirement: 对局日志的记录内容（第 4 条改造记录）/ 平衡分析方向（第 11 项）；
/// simulation-harness —— Requirement: 批量跑局（匠人权重写入批次配置）。tasks 3.2 / 3.3 / 3.4。
/// </summary>
public class 地形改造日志与分析Tests
{
    private static TerrainEditEntry Edit(string action, string target, int player, string artisan, bool caused = false) =>
        new() { Action = action, Target = target, Player = player, Artisan = artisan, CausedCapture = caused };

    [Fact]
    public void 地形可离线重建()
    {
        // 段 F 6.4e 改名（原 回放日志改造可重建终局地形并与对局逐项一致）：测试名 = Scenario 名。
        // match-telemetry「地形可离线重建」的正题：把日志里的全部改造按顺序重放到**开局地图**上，
        // 结果必须与这一局终局时的真实地形**逐项**相同——桥集合、栅栏集合、每一格的地表与可落子性。
        // 只比条数或只比自己算出来的期望值是恒真断言，挡不住"日志漏记一条改造"。
        // 这里走 MatchSession（而不是 BatchRunner.Execute），因为只有它同时给得到日志与活的对局终态。
        // 权重写死，不取 EvaluationWeights.Default：下面「样本里确实有致提子的改造」依赖 AI 的实际走法，
        // 默认权重一校准（scoring-sites 的 27、artisan 的 35）样本就会变，那属于校准而非日志保真度的回归。
        var pinned = new EvaluationWeights(PowerGain: 10, EnemyLoss: 8, Relic: 6, Safety: 27, Growth: 4, Initiative: 20, Supply: 2, Eye: 0, Threat: 0);
        // 段 A（restore-go-core-rules）重挑种子：计分口径改为"领地 + 整体乘倍率"后 AI 走法随之变，原种子 1–3 里第 2 局一次改造都没有（样本口径下界响亮失败）。
        // 同一份写死权重下扫种子 1–24，取连续的 3–5：改造 5 / 3 / 5 次、致提子 3 / 1 / 0 次。断言与期望均未改，只换样本。
        // life-shape 段 B 再次重挑：预演新增活棋禁入 / 破坏活形后 AI 走法随之变，种子 3–5 致提子降为 0 / 0 / 0（下界响亮失败）。
        // 同一份写死权重下重扫种子 1–24：致提子只剩种子 10、18、19、23 各 1 次；取连续的 17–19：改造 2 / 2 / 3 次、致提子 0 / 1 / 1 次。断言与期望均未改，只换样本。
        // ai-eye 段 A 第三次重挑：GroupSafety 改用活形查询（两眼潜力 = min(2, 眼值之和)、已确定活形取常数）后 AI 走法随之变，
        // 种子 17–19 致提子降为 0 / 0 / 0（下界响亮失败）。同一份写死权重（新增 Eye / Threat 取 0）下重扫种子 1–24（CLI 探针，
        // 探针配置先用改动前二进制复现了 17–19 的 改造 2 / 2 / 3、致提子 0 / 1 / 1）：致提子只剩种子 3、4 各 1 次；
        // 取连续的 3–5：改造 5 / 6 / 1 次、致提子 1 / 1 / 0 次。断言与期望均未改，只换样本。
        // ai-eye 段 B 第四次重挑：活形硬约束 + 停手阈值（缺省 20）之后种子 3–5 致提子降为 0 / 0 / 0（下界响亮失败；硬约束之后只剩种子 4 的 1 次，阈值之后为 0）。
        // 同一份写死权重、并把停手阈值也写死为 20（与权重同理：段 D 调缺省阈值不应再翻掉样本）重扫种子 1–200（CLI 探针）：1–24 致提子全为 0，
        // 25–200 里有 20 局各有致提子；取连续且每局都有改造的 53–55：改造 1 / 5 / 3 次、致提子 1 / 1 / 1 次。断言与期望均未改，只换样本。
        // life-single-stone 1.3 第五次重挑：单子不成活后 AI 走法随之变，种子 53 一次改造都没有（改造 0 / 4 / 5、致提子 0 / 0 / 1，第一条下界响亮失败）。
        // 同一份写死权重与阈值重扫种子 1–200（临时探针；探针先在去掉单子上限的二进制上复现了 53–55 的 改造 1 / 5 / 3、致提子 1 / 1 / 1）：
        // 58 局有致提子；取最小的连续且每局都有改造的 1–3：改造 3 / 3 / 6 次、致提子 1 / 0 / 0 次。断言与期望均未改，只换样本。
        // more-pieces-relics 段 A 探针 P1（新四种权重 8 → 400）下本测试红：样本口径下界依赖走法、原先跟随缺省内容集 → 写死 v1。
        // retire-legacy-maps 1.8 第七次重挑：共用配置由 v5 改到 link:1。链接图第 3 大回合结束即可能链接，链接后坐标随之平移（match-telemetry「坐标随链接平移」），
        // "把改造重放到开局地图"只在链接之前成立；于是截断改为 12 个小回合（第 3 大回合），并取这 12 个小回合内没有链接、每局都有改造的种子。
        // 同一份写死权重与阈值扫 link:1 种子 1–60（临时探针）：致提子的改造只有种子 12（有链接）与 36（无链接）；取无链接的 34 / 36 / 43：改造 5 / 2 / 4 次、致提子 0 / 1 / 0 次。
        // 断言与期望均未改，只换样本（种子不再连续，改为显式列出）。
        RunConfig config = SimFixtures.Config(count: 1, seedStart: 1, turnLimit: 12, difficulty: AiDifficulty.Standard) with { PassThreshold = 20, ContentSet = ContentSet.V1 };
        config = config with { Players = [.. config.Players.Select(p => p with { Weights = pinned })] };
        var records = new List<TerrainEditRecord>();
        ulong[] seeds = [34, 36, 43];
        // link-map-terrain 裁决 T1 = A：第二条样本下界（"这几局里确有致提子的改造"）不再依赖 AI 走法——link:1 地图改了之后种子 34 / 36 / 43 的致提子改造为 0。
        // 改法：在三局真实跑局之外加一局脚本化的对局（ScriptedCaptureSession），必然发生一次致提子的立栅；它走与三局完全相同的逐条转录与重建断言，
        // 主体断言（日志重放到开局地图后与终局地形逐项相同、反面对照）一字未改。
        MatchSession scripted = ScriptedCaptureSession();
        MatchSession[] sessions = [.. seeds.Select(s => MatchSession.Create(config, s)), scripted];

        foreach (MatchSession session in sessions)
        {
            Assert.Empty(session.Match.Links);   // 样本口径：开局时自然没有链接（截断之内也不应有，跑完后再核一次）
            MatchLog log = session.Run();
            Assert.Empty(session.Match.Links);
            records.AddRange(session.Match.TerrainEdits);

            TerrainEdit[] replayed = [.. log.Turns.SelectMany(t => t.Edits!).Select(e => TerrainEdit.Parse(e.Target))];
            Assert.NotEmpty(replayed);   // 样本口径下界：真跑出了改造，否则下面什么都没证明

            // 日志层是对 Core 留痕的**逐条转录**，不是自己推的：动作、目标、改造方、匠人落点、是否致提子五项全对得上。
            // 少了这一条，MatchSession 把某个字段写成常量也照样绿。
            Assert.Equal(
                session.Match.TerrainEdits.Select(r =>
                    $"{r.Edit.Kind}/{r.Edit}/{r.Player.Value}/{r.ArtisanCoord.ToNotation()}/{r.CausedCapture}"),
                log.Turns.SelectMany(t => t.Edits!).Select(e =>
                    $"{e.Action}/{e.Target}/{e.Player}/{e.Artisan}/{e.CausedCapture}"));

            MapData start = session.Match.Board.BaseMap;
            MapData rebuilt = TerrainWriter.ApplyAll(start, replayed);
            MapData actual = session.Match.Board.Map;

            Assert.Equal(actual.TerrainData.Bridges, rebuilt.TerrainData.Bridges);
            Assert.Equal(actual.TerrainData.Fences, rebuilt.TerrainData.Fences);
            foreach (Coord c in actual.AllCoords())
            {
                Assert.Equal(actual.SurfaceAt(c), rebuilt.SurfaceAt(c));
                Assert.Equal(actual.IsPlayable(c), rebuilt.IsPlayable(c));
                Assert.Equal(actual.HeightAt(c), rebuilt.HeightAt(c));
            }

            // 反面：开局地图与终局地形确实不同（否则"重建一致"可以靠"一条改造都没发生"通过），
            // 且重放少一条就对不上——逐项比对确实在做比对。三元组覆盖三种动作各自改变的那一项。
            Assert.NotEqual(Shape(start), Shape(actual));
            Assert.NotEqual(Shape(actual), Shape(TerrainWriter.ApplyAll(start, replayed[..^1])));
        }

        // 样本口径下界之二：这三局里确实有"致提子的改造"，上面那条逐条转录才真的校到了 CausedCapture
        // （T-11 之后立栅致提子是第 11 项的主力指标；单局样本里它可能恰好全是 false）。
        Assert.Contains(records, r => r.CausedCapture);
        Assert.Contains(scripted.Match.TerrainEdits, r => r.CausedCapture && r.Edit.Kind == TerrainEditKind.Fence);   // 脚本局的那一手确实致提子（不靠样本）

        static (int Bridges, int Fences, int Forests) Shape(MapData m) =>
            (m.TerrainData.Bridges.Count, m.TerrainData.Fences.Count, m.AllCoords().Count(c => m.SurfaceAt(c) == Surface.Forest));
    }

    /// <summary>
    /// 脚本化的致提子立栅（裁决 T1 = A）：link:1、四人都锁定出生棋盘 3（下标 2，一个战场、不会链接），只在核心区（恒为高度 0 草地、内部无栅栏）里落子。
    /// 第 1 大回合 P1 落核心区中心，P0 落它的西 / 东 / 南三格；第 2 大回合 P0 在核心区左上角落匠人，立中心与北邻之间的栅栏，切断 P1 最后一口气 → 提子。
    /// 其余小回合与其余玩家一律 Pass；截到第 8 个小回合（2 个大回合）。
    /// </summary>
    private static MatchSession ScriptedCaptureSession()
    {
        MatchFlow match = LinkModeFixtures.Started([2, 2, 2, 2]);
        foreach (PlayerId p in match.Players)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 20), (PieceType.Artisan, 2));
        }

        Coord C(int dx, int dy) => LinkModeFixtures.CoreCell(match.Map, 2, dx, dy);
        MatchSession session = MatchSession.ForMatch(match, LinkSimFixtures.Config(turnLimit: 8) with { ContentSet = match.ContentSet });
        session.SetController(LinkModeFixtures.P0, new LinkSimFixtures.ScriptedSeat(match, new Dictionary<int, (Coord, PieceType, TerrainEdit?)[]>
        {
            [1] = [(C(0, 1), PieceType.Basic, null), (C(2, 1), PieceType.Basic, null), (C(1, 0), PieceType.Basic, null)],
            [2] = [(C(0, 2), PieceType.Artisan, TerrainEdit.Fence(C(1, 2), C(1, 1)))],
        }));
        session.SetController(LinkModeFixtures.P1, new LinkSimFixtures.ScriptedSeat(match, new Dictionary<int, (Coord, PieceType, TerrainEdit?)[]>
        {
            [1] = [(C(1, 1), PieceType.Basic, null)],
        }));
        session.SetController(LinkModeFixtures.P2, new LinkSimFixtures.PassController());
        session.SetController(LinkModeFixtures.P3, new LinkSimFixtures.PassController());
        return session;
    }

    [Fact]
    public void 改造可查()
    {
        // 段 F 6.4e 改名（原 真实跑局把改造写进日志且可离线重建地形）：测试名 = Scenario 名；「地形可离线重建」的正题在同名方法里，这里是附带的第二条腿。
        // 「改造可查」+「地形可离线重建」：走真实跑局（Standard——Easy 结构性地几乎不落匠人）→ 日志往返 → 重放。
        // more-pieces-relics 段 A 探针 P1（新四种权重 8 → 400）下本测试红：样本口径依赖走法、原先跟随缺省内容集 → 写死 v1。
        List<MatchLog> logs = BatchRunner.Execute(
            SimFixtures.Config(count: 3, seedStart: 1, turnLimit: 24, difficulty: AiDifficulty.Standard) with { ContentSet = ContentSet.V1 }, parallelism: 1);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        // 每一条小回合快照都带改造字段（没有改造就是空表），首部带匠人权重。
        Assert.All(logs, l => Assert.All(l.Turns, t => Assert.NotNull(t.Edits)));
        Assert.All(logs, l => Assert.Equal(10, l.Header.ArtisanWeight));

        List<TerrainEditEntry> all = [.. logs.SelectMany(l => l.Turns).SelectMany(t => t.Edits!)];

        // 样本口径下界：真跑出了改造，否则下面的往返与重放什么都没证明。
        Assert.NotEmpty(all);

        // 日志往返保留全部字段。
        foreach (MatchLog log in logs)
        {
            MatchLog back = MatchLog.Parse(log.DeterministicText());
            Assert.Equal(
                log.Turns.SelectMany(t => t.Edits!).Select(e => $"{e.Action}/{e.Target}/{e.Player}/{e.Artisan}/{e.CausedCapture}"),
                back.Turns.SelectMany(t => t.Edits!).Select(e => $"{e.Action}/{e.Target}/{e.Player}/{e.Artisan}/{e.CausedCapture}"));
        }

        // 离线重建：按日志顺序把改造重放到开局地图上，结果与该局终局地形逐格一致。
        foreach (MatchLog log in logs)
        {
            MapData start = Siege.Core.Board.Maps.MapCatalog.Resolve(log.Header.MapId);   // retire-legacy-maps 1.8：原写死 v5（样本地图），改按日志首部的地图标识取开局地图（共用配置已是 link:1）
            MapData replayed = TerrainWriter.ApplyAll(
                start, log.Turns.SelectMany(t => t.Edits!).Select(e => TerrainEdit.Parse(e.Target)));

            // 记法可解析且动作名与记法一致（Action 与 Target 不会写反）。
            Assert.All(log.Turns.SelectMany(t => t.Edits!),
                e => Assert.Equal(e.Action, TerrainEdit.Parse(e.Target).Kind.ToString()));
            Assert.Equal(
                start.TerrainData.Bridges.Count + log.Turns.SelectMany(t => t.Edits!).Count(e => e.Action == nameof(TerrainEditKind.Bridge)),
                replayed.TerrainData.Bridges.Count);
            Assert.Equal(
                start.TerrainData.Fences.Count + log.Turns.SelectMany(t => t.Edits!).Count(e => e.Action == nameof(TerrainEditKind.Fence)),
                replayed.TerrainData.Fences.Count);
        }
    }

    [Fact]
    public void 匠人权重写进批次配置与日志首部()
    {
        // simulation-harness「扫档配置可追溯」：匠人权重 18 如实写出。
        // 首部取自**对局本身**（不是 Config），两者不一致时 MatchSession 建局即抛。
        RunConfig config = SimFixtures.Config(count: 1, seedStart: 3, turnLimit: 12, difficulty: AiDifficulty.Standard) with
        {
            ArtisanWeight = 18,
        };

        MatchLog log = BatchRunner.Execute(config, parallelism: 1).Single();   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        Assert.Equal(18, log.Header.ArtisanWeight);
        Assert.Equal(18, log.Header.Config.ArtisanWeight);
        // 序列化往返：配置记录真的写进了文件，不是只活在内存对象里。
        MatchLog back = MatchLog.Parse(log.FullText());
        Assert.Equal(18, back.Header.ArtisanWeight);
        Assert.Equal(18, back.Header.Config.ArtisanWeight);

        // 反向：默认配置下首部是 10，不是"永远写 18"。
        Assert.Equal(10, BatchRunner.Execute(
            SimFixtures.Config(count: 1, seedStart: 3, turnLimit: 12), parallelism: 1).Single().Header.ArtisanWeight);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
    }

    [Fact]
    public void 改造分析分动作输出()
    {
        // 段 F 6.4e 改名（原 第11项改造分析按手算样本输出）：测试名 = Scenario 名。
        // 分析第 11 项的逐指标手算样本。两局：
        //   局 901：第 2 大回合 P0 搭桥（匠人带改造）、第 3 大回合 P1 立栅；落盘匠人 3 枚，其中 2 枚带改造。P0 获胜。
        //   局 902：整局无改造，落盘匠人 1 枚。P1 获胜。
        //   局 903：旧日志（快照缺改造字段）→ 整局排除并计数。
        MatchLog withEdits = SimFixtures.Synthetic(
            901,
            [
                SimFixtures.Turn(1, 2, 0, [10, 5, 5, 5], ["C4:Artisan+B:D4"],
                    edits: [Edit(nameof(TerrainEditKind.Bridge), "B:D4", 0, "C4")]),
                SimFixtures.Turn(2, 3, 1, [10, 5, 5, 5], ["G6:Artisan+F:G6-H6"],
                    edits: [Edit(nameof(TerrainEditKind.Fence), "F:G6-H6", 1, "G6", caused: true)]),
                SimFixtures.Turn(3, 4, 0, [10, 5, 5, 5], ["B2:Artisan", "B3:Basic"]),
            ],
            [],
            SimFixtures.ResultOf(8, [0]));

        MatchLog noEdits = SimFixtures.Synthetic(
            902,
            [SimFixtures.Turn(1, 2, 1, [5, 10, 5, 5], ["E5:Artisan", "F5:Basic"])],
            [],
            SimFixtures.ResultOf(8, [1]));

        MatchLog legacy = SimFixtures.Synthetic(
            903,
            [SimFixtures.Turn(1, 2, 0, [10, 5, 5, 5], ["C4:Artisan+B:D4"], legacyNoEdits: true)],
            [],
            SimFixtures.ResultOf(8, [0]));
        Assert.Null(legacy.Turns[0].Edits);

        TerrainEditSection t = BalanceAnalyzer.Analyze([withEdits, noEdits, legacy]).TerrainEdits;

        Assert.Equal(2, t.Matches);
        Assert.Equal(1, t.Skipped);                 // 旧日志整局排除并计数（R-6）
        Assert.Equal(2, t.TotalEdits);
        Assert.Equal(1.0, t.MeanEditsPerMatch);     // 2 次 ÷ 2 局（含整局无改造的那局）
        Assert.Equal(1, t.MatchesWithoutEdit);
        Assert.Equal(2.0, t.MeanFirstEditRound);    // 只有 901 有改造，首次在第 2 大回合

        // 三种动作逐条：烧林 0 次也在列（规格 MUST NOT 省略该行）。
        Assert.Equal(["Bridge", "Fence", "Burn"], t.Actions.Select(a => a.Action));
        Assert.Equal(1, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Bridge)).Count);
        Assert.Equal(1, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Fence)).Count);
        Assert.Equal(0, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Burn)).Count);
        Assert.Equal(0.5, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Bridge)).Share);
        Assert.Equal(0, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Burn)).Share);

        // 匠人：901 落 3 枚（C4 / G6 / B2）其中 2 枚带改造，902 落 1 枚不带 → 4 枚中 2 枚带改造。
        Assert.Equal(4, t.ArtisansPlaced);
        Assert.Equal(2, t.ArtisansWithEdit);
        Assert.Equal(0.5, t.EditingArtisanShare);

        // 致提子：立栅那一条记 true、搭桥那一条记 false。总数与逐动作两处都钉住——
        // 只断言 0 的话，把 CausedCapture 恒写 false 的实现照样绿（T-11 之后立栅致提子是主力指标）。
        Assert.Equal(1, t.CausedCaptures);
        Assert.Equal(1, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Fence)).CausedCaptures);
        Assert.Equal(0, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Bridge)).CausedCaptures);
        Assert.Equal(0, t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Burn)).CausedCaptures);

        // 改造过的玩家胜率：样本 = {901 的 P0, 901 的 P1}，胜者是 P0 → 1/2。
        Assert.Equal(2, t.WinRateOfEditors.Trials);
        Assert.Equal(1, t.WinRateOfEditors.Successes);

        // 终局新增设施 = 三种动作各自的次数（按日志重放即得）。
        Assert.Equal(1, t.FinalBridges);
        Assert.Equal(1, t.FinalFences);
        Assert.Equal(0, t.FinalBurns);

        // 报告段落：三行动作全在，烧林那行如实给出 0。
        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([withEdits, noEdits, legacy]));
        Assert.Contains("§17-11 地形改造", text, StringComparison.Ordinal);
        Assert.Contains("搭桥：1 次（50.0%），其中直接导致提子 0 次", text, StringComparison.Ordinal);
        Assert.Contains("立栅：1 次（50.0%），其中直接导致提子 1 次", text, StringComparison.Ordinal);
        Assert.Contains("烧林：0 次（0.0%），其中直接导致提子 0 次", text, StringComparison.Ordinal);
        Assert.Contains("改造直接导致提子 1 次；首次改造平均第 2 大回合", text, StringComparison.Ordinal);
        Assert.Contains("排除缺改造字段的旧日志 1 局", text, StringComparison.Ordinal);
        Assert.Contains("带改造的匠人占已落匠人：50.0%（2/4）", text, StringComparison.Ordinal);
        Assert.Contains("桥 1 座，栅栏 1 道，被烧林地 0 格", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 烧林无人使用也如实给出()
    {
        // match-telemetry「烧林无人使用也如实给出」：一批对局里没有任何一次烧林 → 报告给出烧林 0 次与 0% 占比，MUST NOT 省略该行。
        // 段 F 6.4e 从「改造分析分动作输出」里拆出（原来只是那条手算样本里的几行断言，没有同名方法）。
        // 反面：同一批里搭桥与立栅确有次数，证明不是"整段都没输出"。
        // 变异验证 M-F3（段 F 实跑）：ReportWriter 第 11 项跳过次数为 0 的动作行 → 红 2（本测试 + 改造分析分动作输出）。
        MatchLog sample = SimFixtures.Synthetic(
            905,
            [
                SimFixtures.Turn(1, 2, 0, [10, 5, 5, 5], ["C4:Artisan+B:D4"],
                    edits: [Edit(nameof(TerrainEditKind.Bridge), "B:D4", 0, "C4")]),
                SimFixtures.Turn(2, 3, 1, [10, 5, 5, 5], ["G6:Artisan+F:G6-H6"],
                    edits: [Edit(nameof(TerrainEditKind.Fence), "F:G6-H6", 1, "G6")]),
            ],
            [],
            SimFixtures.ResultOf(8, [0]));

        TerrainEditSection t = BalanceAnalyzer.Analyze([sample]).TerrainEdits;
        TerrainEditActionStat burn = t.Actions.Single(a => a.Action == nameof(TerrainEditKind.Burn));
        Assert.Equal((0, 0.0), (burn.Count, burn.Share));
        Assert.Equal(2, t.TotalEdits);

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([sample]));
        Assert.Contains("烧林：0 次（0.0%），其中直接导致提子 0 次", text, StringComparison.Ordinal);
        Assert.Contains("搭桥：1 次（50.0%）", text, StringComparison.Ordinal);
        Assert.Contains("立栅：1 次（50.0%）", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 一局里只要有一条快照缺改造字段就整局排除()
    {
        // R-6 的边界：半旧半新的日志 MUST NOT 被当成"这局只改造了这些"，否则每局平均改造次数会被系统性低估。
        MatchLog halfOld = SimFixtures.Synthetic(
            904,
            [
                SimFixtures.Turn(1, 2, 0, [10, 5, 5, 5], ["C4:Artisan+B:D4"],
                    edits: [Edit(nameof(TerrainEditKind.Bridge), "B:D4", 0, "C4")]),
                SimFixtures.Turn(2, 3, 1, [10, 5, 5, 5], ["E5:Basic"], legacyNoEdits: true),
            ],
            [],
            SimFixtures.ResultOf(8, [0]));

        TerrainEditSection t = BalanceAnalyzer.Analyze([halfOld]).TerrainEdits;

        Assert.Equal(0, t.Matches);
        Assert.Equal(1, t.Skipped);
        Assert.Equal(0, t.TotalEdits);
    }

    [Fact]
    public void 真实批次的第11项分析自洽()
    {
        // 真实跑局上跑一遍分析：分母自洽、动作次数与日志逐条对得上。
        // more-pieces-relics 段 A 探针 P1（新四种权重 8 → 400）下本测试红：样本口径依赖走法、原先跟随缺省内容集 → 写死 v1。
        List<MatchLog> logs = BatchRunner.Execute(
            SimFixtures.Config(count: 3, seedStart: 1, turnLimit: 24, difficulty: AiDifficulty.Standard) with { ContentSet = ContentSet.V1 }, parallelism: 1);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        TerrainEditSection t = BalanceAnalyzer.Analyze(logs).TerrainEdits;

        Assert.Equal(logs.Count, t.Matches);
        Assert.Equal(0, t.Skipped);
        Assert.Equal(logs.SelectMany(l => l.Turns).SelectMany(x => x.Edits!).Count(), t.TotalEdits);
        Assert.Equal(t.TotalEdits, t.Actions.Sum(a => a.Count));
        Assert.True(t.ArtisansWithEdit <= t.ArtisansPlaced);
        Assert.Equal(t.TotalEdits, t.ArtisansWithEdit);   // 一枚匠人至多带一个改造
        Assert.True(t.TotalEdits > 0, "真实样本里一次改造都没有，本条什么都没证明。");
    }
}
