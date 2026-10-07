using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.AiDecision;

/// <summary>frontier-map 裁决 12：AI 搜索配置的"候选格上限"旋钮（缺省 0 = 不限制，v4 与一切既有行为零变化）。</summary>
public class 候选格上限Tests
{
    /// <summary>
    /// 黄金值：取自引入候选格上限<b>之前</b>的代码（工作树 = 段 A + 段 B）的实际运行结果——v4、种子 31、4 名 Standard AI、6 大回合，
    /// 全部小回合快照（去耗时）逐行拼接后的 SHA-256。
    /// <para>restore-go-core-rules 段 A 重建：计分口径（领地计分、加值进倍率、不封顶）与快照字段（删"生效倍率指数"）都变了，整局走法与快照文本随之变，
    /// 旧值 83755037…403E18 作废。</para>
    /// <para>段 B 再次重建：<b>走法一步没变</b>——把段 A 的快照文本与段 B 的逐条比对，去掉被删的 <c>TurnSnapshot.Sites</c> 与
    /// <c>PlayerEntry.SiteScore</c> 两项之后 24 个小回合逐字节相同（比对脚本见 implement 记录）。变的只是快照 JSON 少了这两个字段，
    /// 段 A 值 43D7E980…A757D 因此作废。新值取自段 B 完成后 K = 0 的实际运行，连跑两次一致；变异 M-K1 在新值下重跑仍红。</para>
    /// <para>段 C 第三次重建：<b>走法一步没变</b>——段 B 提交（HEAD）用 <c>--max-rounds 6</c>、段 C 用 <c>--turn-limit 24</c> 各跑种子 31，
    /// 两份日志的 24 个小回合快照去掉耗时、再去掉被改名的 <c>PlayerEntry.Protection</c> → <c>HasEstablishedPower</c> 之后逐条相同（比对脚本见 implement 记录）。
    /// 变的只是快照 JSON 里这一个字段名（及其取值：保护状态 → 出局标记），段 B 值 96D6C02A…385917 因此作废。样本长度由规则级大回合上限 6
    /// 改为跑局层小回合数截断 24（同为 24 个小回合）。</para>
    /// <para>段 D 第四次重建：<b>走法确实变了</b>，变化只来自删除名次征募加成（裁决 #7）。段 C 提交（HEAD）与段 D 在同一测试夹具下各跑种子 31、24 个小回合，
    /// 旧快照去掉被删的两个留痕字段后逐条比对：第 1 个小回合（全员并列名次 1、旧实现无加成）逐字节相同；第 2 个小回合起分叉，
    /// 而分叉点恰是旧日志里第一次出现加成的小回合，且第 2、3 个小回合只差展示数 / 选取数（旧 5/4、6/4 → 新 5/3、5/3），
    /// 第 4 个小回合起候选抽取不同、落点随之分叉。旧值 ABA5D7F9…229A65 在 HEAD 上重跑复现，因此作废；新值取自段 D 的实际运行。</para>
    /// </summary>
    // restore-go-core-rules 段 E：F3DA0A40…48D8F6E0 → 49BCFA49…11DEA30C。走法一步没变：快照新增 PlayerEntry.TerritoryScore 一个字段；
    // 同一局 24 条快照逐条去掉该字段后的哈希恰为旧值 F3DA0A40…（临时探针实跑，roundtrip 逐字节一致），领地分非零 90 处。
    // life-shape 段 B：49BCFA49…11DEA30C → F1B2CAB6…4ACEB088。<b>走法确实变了</b>，来源是活棋禁入 / 破坏活形生效：
    // 改动前后的二进制各跑种子 31、24 个小回合，快照去掉耗时后前 15 个小回合逐条相同；第 16 个小回合（第 4 大回合、P0，保护期后第一手全图落子）起分叉——
    // 旧落点 H2 / M8 / E9 中 M8、E9 在新规则下正是 P0 的禁入格（临时探针实测该时刻 P0 禁入 39 格），新落点 H2 / E5 / F12。新值连跑两次一致。
    // 契约是否扣除禁入格不影响这一局（变异 M-B7「契约不扣除」下本测试仍绿：落进禁入格的候选在预演第 1 步被拒、不进排名）。
    // life-shape 段 D：F1B2CAB6…4ACEB088 → CDEB4C13…70084563。走法一步没变：快照新增 TurnSnapshot.Life 一个字段（活形记录，4.1）；
    // 同一局 24 条快照逐条去掉该字段（JsonNode 删键后重新序列化）的哈希恰为旧值 F1B2CAB6…（临时探针实跑），24 条快照的活形字段全部非空。
    // ai-eye 段 A：CDEB4C13…70084563 → 14E1B0D2…104CBA72。<b>走法确实变了</b>，来源是 GroupSafety 改用活形查询（1.4：两眼潜力 = min(2, 眼值之和)、
    // 已确定活形的安全分取公式上界常数 18）。新增的眼位 / 威胁两维默认权重为 0、不进总分（1.1 之后种子 1–20 的 4 AI 对局与改动前逐步相同，
    // 比对含候选与预演事件）。改动前后的二进制各跑种子 31、24 个小回合：第 1 个小回合（P3）即分叉——旧落点 B10 / B11 / C12 全为要塞、
    // 安全维原始值 12；新落点 B11 改为普通子、安全维 36（B10-B11 与 C12 两条棋串共享平台角上一块 8 格眼空间、眼值 2，均为已确定活形，各取常数 18），眼位原始值 8 = 2 + 2 × 3（权重 0）。新值连跑两次一致。
    // ai-eye 段 B：14E1B0D2…104CBA72 → D1481DD5…772968BD，<b>走法确实变了</b>，分两步归因（每步都有独立的零变化证据）：
    // ① 活形硬约束（2.2）：14E1B0D2… → 8EEC49A7…04679FC8。探针（硬约束判据短路为恒不淘汰、其余改动保留）跑种子 1–20 与改动前逐步相同（20 / 20，含候选与预演事件），
    //    所以变化只来自淘汰本身。本局第 23 个小回合（第 6 大回合、P2）起分叉：旧落点 E12 协同子，新实现 Pass——E12 在 v5 上被地形围成 P2 的单格眼（四邻只有 F12 有气边）。
    // ② 停手阈值缺省 20（2.3）：8EEC49A7… → D1481DD5…。阈值取 0 时整局重现 8EEC49A7…（停手阈值Tests.阈值为0时零变化 钉住）。
    //    第 16 个小回合（第 4 大回合、P0）起分叉：旧选中批次 [G3 普通, G4 连珠, J9 要塞] = 1422 里 G3 是最后加入的一枚、边际提升 1422 − 1402 = 20，
    //    恰等于阈值被撤回，该扰动次序改出 [G4 连珠, J9 要塞] = 1402；同分 1422 的另一候选 [G3 要塞, G4 普通, J9 连珠] 当选。新值连跑两次一致。
    // life-single-stone（规则变更）：D1481DD5…772968BD → 35E25329…8B88D3D7，<b>走法确实变了</b>，来源只是单子上限（单子眼值之和 ≥ 2 也只判未定）：
    // 探针 = 工作树只把三态判定里的 `when stoneCount > 1` 去掉（其余改动全保留），全量测试只红本 change 新增的 4 条，本测试与「阈值为0时零变化」
    // 在旧常量下重新变绿，即两份哈希被逐字节复现。两份二进制各跑种子 31、24 个小回合逐条比对：第 1 个小回合（P3）落点 B10 / B11 / C12 不变，
    // 快照只差活形记录——旧记录里单子 C12（与 B10-B11 共享平台角 8 格眼空间、眼值 2）的"确立"事件消失；第 2 个小回合（P0）起走法分叉：
    // 旧落点 C2 连珠 + D3 匠人（立 D2–E2）两枚单子共享同一块 11 格眼空间、各自"确立"活形，新落点 C2 连珠 + B3 匠人（立 A2–B2）。
    // 旧日志各小回合快照里的单子活形条数由 1 增到 26，新日志恒为 0。新值连跑两次一致。
    // retire-legacy-maps 段 A：改钉到 4 人棋盘图（SimFixtures.Board4）。上面的历次重建都是 v4 / v5、种子 31 上的；最后的 v5 值 35E25329…8B88D3D7 作废。
    // 新基线 = 4 人棋盘图、种子 GoldenSeed（11）、4 名 Standard、GoldenTurns（12）个小回合、权重 / 阈值 / 冒险概率 / 内容集 / 计分规则写死（PinPreCalibration）、
    // 候选格上限按地图缺省（465 格 > 150 → 24）。种子由 31 改为 11：棋盘图上种子 31 的前 24 个小回合里阈值 0 与缺省阈值 20 走法相同（探针种子 1–12：
    // 3、7、8、10 两者相同），「停手阈值Tests.阈值为0时零变化」的反面对照因此不成立；种子 11 在第 5 个小回合即分叉。
    internal const string BoardGoldenTurnHash = "0963D262DFD1B6B7D39037C435A8BA98EF370F324B6AE7F727811EC7206C7109";

    /// <summary>
    /// 本 change 黄金值的对局种子（retire-legacy-maps 段 A：原 v5 上为 31，选 11 的依据见 <see cref="BoardGoldenTurnHash"/>）。
    /// 引用 <see cref="BoardGoldenTurnHash"/> 的各条"逐步相同"测试一律用它，不各写一份字面量。
    /// </summary>
    internal const ulong GoldenSeed = 11;

    /// <summary>
    /// 本 change 黄金值的小回合数截断（retire-legacy-maps 段 A：原 v5 上为 24）。4 人棋盘图上标准 AI 每小回合约 90 ms、是 v5 的 3 倍，
    /// 引用 <see cref="BoardGoldenTurnHash"/> 的十余局都按 24 跑会让全量测试耗时超过改动前的 2 倍；取 12 个小回合（3 个大回合），
    /// 阈值 0 / 20 在第 5 个小回合即分叉，内容集 v1 / v2、计分规则 v1 / v2、带入开 / 关的反面对照在 12 个小回合内都成立（各测试的 NotEqual 断言钉住）。
    /// </summary>
    internal const int GoldenTurns = 12;

    /// <summary>
    /// 同一局显式不限制候选格（K = 0）的快照哈希（retire-legacy-maps 段 A 钉下）。v5 只有 105 格，缺省 K 就是 0，「缺省不限制」与 <see cref="BoardGoldenTurnHash"/> 是同一局；
    /// 棋盘图上缺省 K 是 24，"不限制"只能显式给 0，于是分成两个哈希。
    /// </summary>
    internal const string UnlimitedTurnHash = "BF9041AF7549B362089CE4BCE3BE6D3CCB57CCD4E70AEEA4C097F1FDAF3E4E6C";

    internal static string TurnHash(MatchLog log) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', SimFixtures.TurnTexts(log.Turns)))));

    private static AiSearchConfig StandardWith(int cellLimit) => AiSearchConfig.Standard with { CandidateCellLimit = cellLimit };

    /// <summary>第 5 大回合、部署上限 8、60+ 合法空格的开放局面（与「不穷举排列」同一局面）。</summary>
    private static MatchFlow OpenPosition()
    {
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P1, "E5", "F6", "D7").Stones(AiFixtures.P2, "G3");
        match.SetDeployLimit(8);
        return match;
    }

    private sealed record DeployTrace(HeuristicTurnController Ai, int Rehearsals, int Empties, int Types, string Placements);

    private static DeployTrace DeployOnce(MatchFlow match, AiSearchConfig config)
    {
        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard, config: config);
        StagedBatch batch = match.OpenDeploy();
        int empties = batch.Context.LegalRange.Count(c => batch.Board[c].IsPlayableEmpty);
        int types = batch.Context.Stock.Count(kv => kv.Value > 0);
        int rehearsals = 0;
        ai.Deploy(batch, () =>
        {
            rehearsals++;
            return match.Rehearse();
        });
        return new DeployTrace(ai, rehearsals, empties, types, string.Join(" ", batch.Placements.Select(p => p.ToString())));
    }

    private static string RankingText(HeuristicTurnController ai) =>
        string.Join("|", ai.LastPointRanking.Select(p => $"{p.Coord.ToNotation()}:{p.Type}:{p.Edit}:{p.Total}"));

    // ---------- K = 0：零变化 ----------

    [Fact]
    public void 显式0不限制_整局与钉下的不限制基线逐步相同且与缺省K不同()
    {
        // 规格 Scenario「显式 0 不限制」（retire-legacy-maps 段 D 由「缺省不限制时标准图整局与改动前逐步相同」改名）。
        // 变异 M-K1：RankPoints 的启用条件改成恒真（K = 0 也预筛，Take(0) 取空）→ 本测试红。
        // 段 A check 实跑：在重建后的黄金哈希上 M-K1 仍红 36（含本测试）——哈希虽是段 A 后重生成的，但不是自证的。
        // ai-eye 段 D2（4.5）：黄金哈希产自定值之前的缺省（Eye / Threat 0、停手阈值 20），权重与阈值写死为该口径——本测试钉的是"K 缺省不改变走法"，不是默认权重。
        // retire-legacy-maps 段 A：4 人棋盘图 465 格，缺省 K = 24；"不限制"在这张图上只能显式给 0（K = 0 走"不预筛"分支，M-K1 仍然打得到），
        // 对照哈希 UnlimitedTurnHash 在本 change 钉下。缺省 K 的那一局由 BoardGoldenTurnHash 钉住（对局内容集 / 计分规则 / 带入带出 / 批量跑局共用）。
        MatchLog log = BatchRunner.Execute(
            SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: GoldenSeed, turnLimit: GoldenTurns, difficulty: AiDifficulty.Standard)) with { CandidateCellLimit = 0 }, parallelism: 1)[0];

        Assert.False(log.IsFailed);
        Assert.True(log.Turns.Count >= GoldenTurns, $"小回合 {log.Turns.Count}");
        Assert.Equal(0, log.Header.Config.CandidateCellLimit);
        string hash = TurnHash(log);
        Assert.True(UnlimitedTurnHash == hash, $"种子 {GoldenSeed} 不限制候选格的小回合快照哈希变了：现为 {hash}。");
        Assert.NotEqual(UnlimitedTurnHash, BoardGoldenTurnHash);   // 缺省 K = 24 确实预筛了：与不限制的那一局不同
    }

    [Fact]
    public void 小图零变化_难度默认参数不写上限按地图取缺省()
    {
        // 规格 Scenario「小图零变化」（段 D 由「难度默认参数不写上限按地图取缺省」改名）。retire-legacy-maps 段 A2（主会话裁决 1，原名「难度默认参数不启用上限」）：四档预设不写候选格上限（null），实际生效值按地图取：
        // 小图（≤ 150 格）0 = 不限制，与改名前的断言等价；大图取缺省 K。
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Null(AiSearchConfig.ForDifficulty(d).CandidateCellLimit));
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(0, AiSearchConfig.ForDifficulty(d).CellLimitOn(AiSearchConfig.LargeMapPlayableThreshold)));
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(24, AiSearchConfig.ForDifficulty(d).CellLimitOn(AiSearchConfig.LargeMapPlayableThreshold + 1)));
        DeployTrace small = DeployOnce(OpenPosition(), AiSearchConfig.Standard);
        Assert.Null(small.Ai.Config.CandidateCellLimit);
        Assert.Equal(small.Empties, small.Ai.LastCandidateCells.Length);   // 9×9 合成图上不预筛
    }

    /// <summary>可落子格不超过 150 的合成小图（9×9 = 81 格，<see cref="MatchFixtures.Map(string[])"/>）：retire-legacy-maps 段 A2 起"小图"情形一律用它（内置图都 &gt; 150 格）。</summary>
    private static MapData SmallMap => MatchFixtures.Map();

    // ---------- 显式剪枝参数未写上限（retire-legacy-maps 段 A2，主会话裁决 1） ----------

    private static RunConfig WithSearch(RunConfig config, AiSearchConfig search) =>
        config with { Players = [.. config.Players.Select(p => p with { Search = search })] };

    [Fact]
    public void 显式剪枝参数未写上限时按地图取缺省()
    {
        // 产品路径（run --config 给 Players[].Search 而不写 CandidateCellLimit）：未写 → 大图 24、小图 0；写 0 → 不限制；写 K → K。
        // 新建的局把落成值写进首部（RunConfig.ResolvedFor），按首部重建（回放）时首部里没有该项的旧记录按 0（当时不限制），不取地图缺省。
        // 变异 A2-K1：MatchSession.AttachConfigured 对显式 Search 不补上限（原样传入）→ 本测试红（会话侧 null ≠ 24）。
        // 变异 A2-K2：ResolvedFor 不把显式 Search 的上限落成具体值 → 本测试红（首部 null）。
        // 变异 A2-K3：按首部重建时显式 Search 的缺项改取地图缺省 → 本测试红（recorded 断言）。
        RunConfig large = WithSearch(SimFixtures.Config(turnLimit: 4, difficulty: AiDifficulty.Standard), AiSearchConfig.Standard);
        Assert.True(MapCatalog.Resolve(large.MapId).PlayableCount > AiSearchConfig.LargeMapPlayableThreshold);
        Assert.All(large.Players, p => Assert.Null(p.Search!.CandidateCellLimit));

        MatchSession unset = MatchSession.Create(large, 1);
        Assert.All(unset.Match.Players, p => Assert.Equal(AiSearchConfig.LargeMapCellLimit, unset.AiOf(p)!.Config.CandidateCellLimit));
        Assert.All(unset.Config.Players, p => Assert.Equal(AiSearchConfig.LargeMapCellLimit, p.Search!.CandidateCellLimit));   // 首部记落成值

        foreach (int k in new[] { 0, 5 })
        {
            MatchSession set = MatchSession.Create(WithSearch(large, AiSearchConfig.Standard with { CandidateCellLimit = k }), 1);
            Assert.All(set.Match.Players, p => Assert.Equal(k, set.AiOf(p)!.Config.CandidateCellLimit));
            Assert.All(set.Config.Players, p => Assert.Equal(k, p.Search!.CandidateCellLimit));
        }

        // 跑局级显式给了 K（--cell-limit）：显式 Search 未写上限的玩家同样取它。
        MatchSession runLevel = MatchSession.Create(large with { CandidateCellLimit = 7 }, 1);
        Assert.All(runLevel.Match.Players, p => Assert.Equal(7, runLevel.AiOf(p)!.Config.CandidateCellLimit));

        // 按首部重建：显式 Search 缺上限 = 该项出现之前的旧记录，当时就是不限制。
        MatchSession recorded = MatchSession.Create(large, 1, map: null, recorded: true);
        Assert.All(recorded.Match.Players, p => Assert.Equal(0, recorded.AiOf(p)!.Config.CandidateCellLimit));

        // 小图：未写 → 0（会话与首部）。
        MatchFlow small = MatchFixtures.Started();
        Assert.True(small.Map.PlayableCount <= AiSearchConfig.LargeMapPlayableThreshold, $"合成图 {small.Map.PlayableCount} 格");
        MatchSession smallSession = MatchSession.ForMatch(small, WithSearch(SimFixtures.Config(turnLimit: 1, difficulty: AiDifficulty.Standard), AiSearchConfig.Standard));
        Assert.All(small.Players, p => Assert.Equal(0, smallSession.AiOf(p)!.Config.CandidateCellLimit));
        Assert.All(large.ResolvedFor(SmallMap).Players, p => Assert.Equal(0, p.Search!.CandidateCellLimit));
    }

    [Fact]
    public void 显式搜索配置未写K时取跑局级值()
    {
        // 规格：ai-decision「候选格上限」Scenario「显式搜索配置未写 K 时取跑局级值」（retire-legacy-maps 段 D 收尾补）。
        // 配置文件给每名玩家显式写了搜索配置但没写 K，在 4 人棋盘图上跑局：跑局级给了 K = 7 → 玩家生效 7；跑局级未给 → 按地图缺省 24。
        // 三处都要是具体数值：会话里的 AI、config.json 原文（逐名玩家的 Search，读 JSON 节点、不经反序列化）与日志首部。
        // 变异 D-K1（段 D 实跑）：RunConfig.ResolvedFor 落成显式 Search 的缺项时改按地图缺省、不读跑局级 K → 本测试红 1。
        // 变异 D-K2（段 D 实跑，0 红、等价变异）：MatchSession.AttachConfigured 的 `?? CellLimit` 改按地图缺省——新建的局在建会话之前已由 ResolvedFor 落成具体值，
        // 这条回退只剩按首部重建的旧日志走得到（由「显式剪枝参数未写上限时按地图取缺省」的 recorded 段钉住）。
        RunConfig baseConfig = WithSearch(SimFixtures.Config(turnLimit: 1, difficulty: AiDifficulty.Standard), AiSearchConfig.Standard);
        Assert.Equal(SimFixtures.Board4, baseConfig.MapId);
        Assert.Null(baseConfig.CandidateCellLimit);
        Assert.All(baseConfig.Players, p => Assert.Null(p.Search!.CandidateCellLimit));   // 前提：显式 Search 确实没写 K

        foreach ((int? runLevel, int expected) in new (int?, int)[] { (7, 7), (null, AiSearchConfig.LargeMapCellLimit) })
        {
            RunConfig config = baseConfig with { CandidateCellLimit = runLevel };

            MatchSession session = MatchSession.Create(config, 1);
            Assert.All(session.Match.Players, p => Assert.Equal(expected, session.AiOf(p)!.Config.CandidateCellLimit));

            string dir = SimFixtures.TempDir($"cell-limit-explicit-search-{runLevel?.ToString() ?? "unset"}");
            BatchRunner.ExecuteToDirectory(config, dir, parallelism: 1);
            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "config.json"))))
            {
                JsonElement[] players = [.. json.RootElement.GetProperty("Players").EnumerateArray()];
                Assert.Equal(4, players.Length);
                Assert.All(players, p => Assert.Equal(expected, p.GetProperty("Search").GetProperty("CandidateCellLimit").GetInt32()));
            }

            MatchLog log = MatchLog.Read(Directory.EnumerateFiles(dir, "match-*.jsonl").Single());
            Assert.Equal(4, log.Header.Config.Players.Count);
            Assert.All(log.Header.Config.Players, p => Assert.Equal(expected, p.Search!.CandidateCellLimit));
        }
    }

    [Fact]
    public void 未写上限的AI在大图上按缺省上限预筛()
    {
        // Core 侧：直接装配的 AI（不经跑局会话）配置未写上限时，按开局地图的可落子格数取缺省（AiSearchConfig.CellLimitOn）。
        // 4 人棋盘图开局第一手：保护期内合法范围 = 本人出生棋盘（5–7 边长，≥ 25 格 > 24）。
        // 变异 A2-K4：CellLimitOn 改成 `CandidateCellLimit ?? 0`（未写即不限制）→ 本测试红。
        static DeployTrace Board(AiSearchConfig config)
        {
            MatchFlow match = MatchSession.Create(SimFixtures.Config(turnLimit: 4, difficulty: AiDifficulty.Standard), 1).Match;
            PlayerId me = match.CurrentPlayer!.Value;
            HeuristicTurnController ai = HeuristicAi.Create(match, me, AiDifficulty.Standard, config: config);
            StagedBatch batch = match.OpenDeploy();
            int empties = batch.Context.LegalRange.Count(c => batch.Board[c].IsPlayableEmpty);
            ai.Deploy(batch, match.Rehearse);
            return new DeployTrace(ai, 0, empties, 0, string.Empty);
        }

        DeployTrace unset = Board(AiSearchConfig.Standard);
        Assert.True(unset.Empties > AiSearchConfig.LargeMapCellLimit, $"合法空格 {unset.Empties}");
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, unset.Ai.LastCandidateCells.Length);
        DeployTrace zero = Board(AiSearchConfig.Standard with { CandidateCellLimit = 0 });
        Assert.Equal(zero.Empties, zero.Ai.LastCandidateCells.Length);
        DeployTrace five = Board(AiSearchConfig.Standard with { CandidateCellLimit = 5 });
        Assert.Equal(5, five.Ai.LastCandidateCells.Length);
    }

    [Fact]
    public void 未写上限的专家两层扫描同样按地图缺省预筛()
    {
        // retire-legacy-maps 段 A2 检查补：专家的两层扫描（ExpertLookahead.BestSingleGain）经 CellLimitOn 读 K。A2 删掉夹具 WithMapCellLimit 之后，
        // 直接装配的专家（LookaheadFixtures.Shadow 传 null K）在 4 人棋盘图上靠它取 24；此前没有测试钉住这一路径。
        // 4 人棋盘图种子 1 前 4 个部署局面（保护期内对手的合法范围 = 其出生棋盘，≥ 25 格 > 24）：未写 K 的两层预演次数与显式 24 相同、与显式 0（不限制）不同。
        // 变异 M-A2C-7：BestSingleGain 改为 `_config.CandidateCellLimit ?? 0`（未写即不限制）→ 本测试红。
        AiSearchConfig unset = LookaheadFixtures.ExpandedExpert with { PassThreshold = LookaheadFixtures.PassThreshold };
        Assert.Null(unset.CandidateCellLimit);
        long unsetTotal = 0, k24Total = 0, unlimitedTotal = 0;
        int positions = LookaheadFixtures.ProbePositionsOn(LookaheadFixtures.BoardConfig(
            LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard, LookaheadFixtures.Standard) with { TurnLimit = 4 }, every: 1, (match, batch) =>
        {
            int TwoPly(AiSearchConfig config) =>
                LookaheadFixtures.Shadow(match, batch.Context, AiDifficulty.Expert, config).Ai.LastLookahead!.TwoPlyRehearsals;
            unsetTotal += TwoPly(unset);
            k24Total += TwoPly(unset with { CandidateCellLimit = AiSearchConfig.LargeMapCellLimit });
            unlimitedTotal += TwoPly(unset with { CandidateCellLimit = 0 });
        }, 1);

        Assert.True(positions >= 4, $"局面 {positions} 个");
        Assert.True(unsetTotal > 0, "两层扫描没有发生（样本口径）");
        Assert.Equal(k24Total, unsetTotal);
        // 反面：显式 0 确实走了另一条路（不预筛；预筛本身也计入两层预演次数，所以只断言不同、不断言大小）。
        Assert.NotEqual(unlimitedTotal, unsetTotal);
    }

    [Fact]
    public void 局中改造不让上限跳档_未写上限时按开局地图取缺省()
    {
        // retire-legacy-maps 段 A2 检查补：CellLimitOn 读的是开局地图（GameBoard.BaseMap）的可落子格数，不是改造后的当前地图。
        // 棋盘图上不会架出桥，"读当前地图"在内置图上是等价变异，只能用合成图钉：13×12 合成图开局恰 150 格可落子（阈值本身，不算大图），
        // 中央 G6 一格未架桥深水；对局中把它架成桥后当前地图 151 格（越过阈值），未写上限的 AI 仍按开局的 150 格取 0（不预筛）。
        // 变异 M-A2C-1：HeuristicTurnController.RankPoints 改读 batch.Board.Map.PlayableCount → 本测试红（候选格被截到 24）。
        static ImmutableHashSet<Coord> Corner(int x0, int y0) =>
            [.. Enumerable.Range(0, 9).Select(i => new Coord(x0 + (i % 3), y0 + (i / 3)))];

        TerrainData terrain = TestMaps.Terrain(surfaces: [("G6", Surface.DeepWater)]);
        MapData map = new()
        {
            TerrainData = terrain,
            Id = "test-cell-limit-13x12",
            Width = 13,
            Height = 12,
            MaxPlayers = 4,
            Obstacles = [.. new[] { "F5", "H5", "F7", "H7", "G8" }.Select(Coord.Parse)],
            BirthZones = [Corner(0, 0), Corner(10, 0), Corner(0, 9), Corner(10, 9)],
            RelicCells = ImmutableDictionary<Coord, RelicCellSpec>.Empty,
            ChokePoints = [],
            CentralEntrance = Coord.Parse("G4"),
        };
        Assert.Equal(AiSearchConfig.LargeMapPlayableThreshold, map.PlayableCount);

        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchFixtures.V1);
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        match.AtRound(5, [AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3]);
        match.Board.ApplyTerrainEdits([TerrainEdit.Bridge(Coord.Parse("G6"))]);
        Assert.Equal(AiSearchConfig.LargeMapPlayableThreshold, match.Board.BaseMap.PlayableCount);
        Assert.Equal(AiSearchConfig.LargeMapPlayableThreshold + 1, match.Board.Map.PlayableCount);   // 前提：当前地图确已越过阈值

        DeployTrace trace = DeployOnce(match, AiSearchConfig.Standard);
        Assert.True(trace.Empties > AiSearchConfig.LargeMapCellLimit, $"合法空格 {trace.Empties}");
        Assert.Equal(trace.Empties, trace.Ai.LastCandidateCells.Length);
    }

    // ---------- K > 0 ----------

    [Fact]
    public void 启用后只对前K格做完整枚举()
    {
        // 变异 M-K2：PrefilterCells 去掉 Take(K) → 本测试红。
        const int k = 8;
        DeployTrace unlimited = DeployOnce(OpenPosition(), AiSearchConfig.Standard);
        DeployTrace limited = DeployOnce(OpenPosition(), StandardWith(k));

        Assert.True(limited.Empties >= 60, $"合法空格 {limited.Empties}");
        Assert.Equal(unlimited.Empties, unlimited.Ai.LastCandidateCells.Length);
        Assert.Equal(k, limited.Ai.LastCandidateCells.Length);
        Assert.Equal(limited.Ai.LastCandidateCells.Order(), limited.Ai.LastCandidateCells);
        var cells = limited.Ai.LastCandidateCells.ToHashSet();
        Assert.All(limited.Ai.LastPointRanking, p => Assert.Contains(p.Coord, cells));
        Assert.All(limited.Ai.LastCandidates, c => Assert.All(c.Placements, p => Assert.Contains(p.Coord, cells)));

        // 预演次数：预筛每格 1 次 + K 格 × 类型数（库存无匠人，每格每类型 1 次）+ 组合 M × (N + 1)。
        int combine = AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1);
        Assert.True(limited.Rehearsals <= limited.Empties + (k * limited.Types) + combine, $"预演 {limited.Rehearsals} 次");
    }

    [Fact]
    public void 预筛按代表类型的格分取前K同分按坐标序()
    {
        // 独立复算：代表类型 = 持有类型里枚举序最前的一种、不带改造；格分 = 预筛口径的总分；降序、同分按坐标序、取前 K。
        // ai-eye 段 C（3.1）：格分由"既有评估函数（九维）"改为预筛口径（七维、不查活形，EvaluatePrefilter），oracle 随之改用它；
        // 本测试钉的是"前 K 的取法"，格分口径本身由「预筛阶段不触发活形查询」钉住。
        // 变异 M-K3：PrefilterCells 的 OrderByDescending 改成 OrderBy（取最差的 K 格）→ 本测试红。变异 M-K11：同分次序改成坐标逆序 → 本测试红。
        //（单删 ThenBy 是等价变异：LINQ 排序稳定，而输入已按坐标序。）
        const int k = 6;
        MatchFlow oracleMatch = OpenPosition();
        HeuristicTurnController oracleAi = HeuristicAi.Create(oracleMatch, AiFixtures.P0);
        StagedBatch batch = oracleMatch.OpenDeploy();
        BatchEvaluator evaluator = oracleAi.CreateEvaluator();
        PieceType representative = batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order().First();
        var scored = new List<(Coord Cell, BigInteger Total)>();
        foreach (Coord cell in batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order())
        {
            batch.Clear();
            Assert.Null(batch.Stage(cell, representative));
            RehearsalResult result = oracleMatch.Rehearse();
            if (result.IsLegal)
            {
                scored.Add((cell, evaluator.EvaluatePrefilter(batch.Placements, result, batch.Context).Total));
            }
        }

        Coord[] expected = [.. scored.OrderByDescending(s => s.Total).ThenBy(s => s.Cell).Take(k).Select(s => s.Cell).Order()];

        DeployTrace limited = DeployOnce(OpenPosition(), StandardWith(k));

        Assert.Equal(expected, limited.Ai.LastCandidateCells);
    }

    /// <summary>
    /// A2 是深水、B1 是敌子（气：A1、C1；B2 是己子）：A1 的气边邻格只有 B1，不带改造落 A1 是自杀手。
    /// 匠人落 A1 同时立栅 B1–C1，则 B1 只剩 A1 一口气、随落子被提，A1 合法且提一子（terrain-edit T-3：改造先于提子与自杀手判定）；
    /// 在 A2 搭桥也能让 A1 合法（补一口气），但不提子。这是一手"只有带改造的匠人才下得出"的妙手。
    /// </summary>
    private static MatchFlow ArtisanOnlyCellPosition(bool holdsArtisan)
    {
        MatchFlow match = MatchFixtures.Started(TestMaps.Terrain(surfaces: [("A2", Surface.DeepWater)]))
            .AtRound(5, [AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3])
            .Stones(AiFixtures.P0, "B2")
            .Stones(AiFixtures.P1, "B1");
        match.Debug.SeedHand(AiFixtures.P0, holdsArtisan ? [(PieceType.Basic, 50), (PieceType.Artisan, 5)] : [(PieceType.Basic, 50)]);
        return match;
    }

    [Fact]
    public void 只有带改造的匠人才落得下的格不被预筛漏掉()
    {
        // 落子合法性与类型无关，代表类型（普通子）落不下的格别的类型不带改造也落不下；唯一的例外是匠人的改造先于自杀手判定。
        // 预筛对这类格退而用匠人逐个合法改造目标预演，格分取最高的合法总分；其余格仍只预演一次。
        // 变异 M-K13：去掉回退 → A1 不进候选，本测试红。变异 M-K14：回退条件去掉"代表类型落不下"（每格都逐个改造预演）→ 预演次数上界红。
        // 变异 M-K15：格分改取第一个合法改造（搭桥补气，分低）而不是最高分（立栅提子）→ A1 名次掉出前 K，本测试红。
        Coord a1 = TestMaps.At("A1");
        MatchFlow oracleMatch = ArtisanOnlyCellPosition(holdsArtisan: true);
        HeuristicTurnController oracleAi = HeuristicAi.Create(oracleMatch, AiFixtures.P0);
        StagedBatch batch = oracleMatch.OpenDeploy();
        BatchEvaluator evaluator = oracleAi.CreateEvaluator();
        Coord[] empties = [.. batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        ImmutableArray<TerrainEdit> a1Edits = TerrainEditRules.LegalTargets(batch.Board.Map, a1);

        // 前提：A1 不带改造是自杀手，带改造（立栅 B1–C1 / 在 A2 搭桥）才合法；其余空格普通子都落得下。
        // 格分按预筛口径（ai-eye 段 C 3.1：七维、不查活形，EvaluatePrefilter）复算。
        var scored = new List<(Coord Cell, BigInteger Total)>();
        foreach (Coord cell in empties)
        {
            batch.Clear();
            Assert.Null(batch.Stage(cell, PieceType.Basic));
            RehearsalResult plain = oracleMatch.Rehearse();
            Assert.Equal(cell != a1, plain.IsLegal);
            if (plain.IsLegal)
            {
                scored.Add((cell, evaluator.EvaluatePrefilter(batch.Placements, plain, batch.Context).Total));
            }
        }

        var a1Totals = new List<BigInteger>();
        foreach (TerrainEdit edit in a1Edits)
        {
            batch.Clear();
            Assert.Null(batch.Stage(a1, PieceType.Artisan, edit));
            RehearsalResult edited = oracleMatch.Rehearse();
            if (edited.IsLegal)
            {
                a1Totals.Add(evaluator.EvaluatePrefilter(batch.Placements, edited, batch.Context).Total);
            }
        }

        batch.Clear();
        Assert.Contains(TerrainEdit.Bridge(TestMaps.At("A2")), a1Edits);
        Assert.Contains(TerrainEdit.Fence(TestMaps.At("B1"), TestMaps.At("C1")), a1Edits);
        Assert.NotEmpty(a1Totals);
        scored.Add((a1, a1Totals.Max()));

        // K 取"恰好把 A1 收进来"的名次：A1 在前 K 格里，K 仍小于合法空格数（预筛生效）。
        (Coord Cell, BigInteger Total)[] ranked = [.. scored.OrderByDescending(s => s.Total).ThenBy(s => s.Cell)];
        int k = Array.FindIndex(ranked, s => s.Cell == a1) + 1;
        Assert.InRange(k, 1, empties.Length - 1);   // A1 垫底时无法既收进它又让预筛生效——那样局面要重摆
        Coord[] expected = [.. ranked.Take(k).Select(s => s.Cell).Order()];
        Assert.Contains(a1, expected);

        DeployTrace limited = DeployOnce(ArtisanOnlyCellPosition(holdsArtisan: true), StandardWith(k));

        Assert.Equal(expected, limited.Ai.LastCandidateCells);
        Assert.True(a1Totals.Max() > a1Totals.Min(), "格分取的是各合法改造里最高的总分（立栅提子高于搭桥补气），不是第一个合法的。");

        // 开销：预筛每格 1 次 + 只在 A1 上逐个改造再试 + K 格的完整枚举（普通子 1 次、匠人 1 + 改造目标数）+ 组合 M × (N + 1)。
        int fullEnumeration = limited.Ai.LastCandidateCells.Sum(c => 2 + TerrainEditRules.LegalTargets(batch.Board.Map, c).Length);
        int combine = AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1);
        Assert.True(
            limited.Rehearsals <= empties.Length + a1Edits.Length + fullEnumeration + combine,
            $"预演 {limited.Rehearsals} 次，上界 {empties.Length} + {a1Edits.Length} + {fullEnumeration} + {combine}");

        // 手里没有匠人：A1 谁也落不下，不进候选，也不为它多做预演。
        DeployTrace noArtisan = DeployOnce(ArtisanOnlyCellPosition(holdsArtisan: false), StandardWith(k));
        Assert.DoesNotContain(a1, noArtisan.Ai.LastCandidateCells);
        Assert.True(noArtisan.Rehearsals <= empties.Length + k + combine, $"预演 {noArtisan.Rehearsals} 次");
    }

    [Fact]
    public void 预筛阶段不触发活形查询()
    {
        // ai-eye D5 / 段 C 3.1：预筛只算既有七维（眼位、威胁记 0；安全按不查活形的口径），不做活形查询；进入完整枚举的 ≤ K 格才算九维。
        // 计数桩：换上记录盘面的活形查询，并关掉决策内缓存（每一次请求都落到桩上，缓存命中不会掩盖预筛的查询）。
        // 预筛预演的盘面是"代表类型落在某一格"：若预筛也查活形，被剪掉的每一格（合法空格 − K 格）都会各留下一次"新落子不在完整枚举格里"的查询。
        // 变异 M-C1（PrefilterCells 改回 evaluator.Evaluate，即九维）→ 红 1（本测试）。
        const int k = 8;
        MatchFlow match = OpenPosition();
        GameBoard before = match.Board.Clone();
        var queried = new List<GameBoard>();
        HeuristicTurnController ai = HeuristicAi.Create(
            match,
            AiFixtures.P0,
            AiDifficulty.Standard,
            weights: null,
            StandardWith(k),
            lifeQuery: board =>
            {
                queried.Add(board.Clone());
                return LifeShapeReport.Analyze(board);
            },
            cacheLife: false);
        StagedBatch batch = match.OpenDeploy();
        int empties = batch.Context.LegalRange.Count(c => batch.Board[c].IsPlayableEmpty);

        ai.Deploy(batch, match.Rehearse);

        Assert.True(empties > k, $"合法空格 {empties}");
        Assert.Equal(k, ai.LastCandidateCells.Length);
        var cells = ai.LastCandidateCells.ToHashSet();
        Coord[] outside = [.. queried.SelectMany(b => NewStones(before, b)).Where(c => !cells.Contains(c)).Distinct().Order()];
        Assert.True(outside.Length == 0, $"预筛阶段查了活形：新落子在 {string.Join(" ", outside.Select(c => c.ToNotation()))}");

        // 桩确实接上（否则上面恒真）：批次前 1 次，完整枚举每个候选至少 1 次（缓存关着，硬约束与评价各查一次）。
        Assert.Equal(1, queried.Count(b => !NewStones(before, b).Any()));
        Assert.True(queried.Count >= 1 + ai.LastPointRanking.Length, $"活形查询 {queried.Count} 次");
        Assert.All(ai.LastCandidateCells, c => Assert.Contains(queried, b => NewStones(before, b).Contains(c)));

        // 口径：同一个预演结果，预筛口径与完整口径的其余五维逐项相同，眼位、威胁记 0；安全维仍在（按不查活形的口径），不是整维丢掉。
        // 变异 M-C9（预筛把安全维也记 0，即只剩六维）→ 红 1（本测试，最后一条断言；两条 oracle 测试跟着预筛口径走，不红）。
        BatchEvaluator evaluator = ai.CreateEvaluator();
        PieceType representative = batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key).Order().First();
        var prefilterSafety = new List<BigInteger>();
        foreach (Coord cell in ai.LastCandidateCells)
        {
            batch.Clear();
            Assert.Null(batch.Stage(cell, representative));
            RehearsalResult result = match.Rehearse();
            Assert.True(result.IsLegal);
            EvaluationBreakdown full = evaluator.Evaluate(batch.Placements, result, batch.Context);
            EvaluationBreakdown prefilter = evaluator.EvaluatePrefilter(batch.Placements, result, batch.Context);
            foreach (EvaluationDimension d in new[] { EvaluationDimension.PowerGain, EvaluationDimension.EnemyLoss, EvaluationDimension.Relic,
                EvaluationDimension.Growth, EvaluationDimension.Initiative, EvaluationDimension.Supply })
            {
                Assert.Equal(full.RawOf(d), prefilter.RawOf(d));
            }

            Assert.Equal(BigInteger.Zero, prefilter.RawOf(EvaluationDimension.Eye));
            Assert.Equal(BigInteger.Zero, prefilter.RawOf(EvaluationDimension.Threat));
            prefilterSafety.Add(prefilter.RawOf(EvaluationDimension.Safety));
        }

        batch.Clear();
        Assert.Contains(prefilterSafety, v => !v.IsZero);
    }

    /// <summary>盘面 <paramref name="after"/> 上比 <paramref name="before"/> 多出棋子的格。</summary>
    private static IEnumerable<Coord> NewStones(GameBoard before, GameBoard after) =>
        after.AllCoords().Where(c => after[c].Occupant is not null && before[c].Occupant is null);

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void K不小于合法空格数时等价于不限制(int extra)
    {
        // 变异 M-K4：启用条件去掉 `cells.Length > K`（K 够大也照样预筛）→ 预演次数多出一轮 → 本测试红。
        DeployTrace unlimited = DeployOnce(OpenPosition(), AiSearchConfig.Standard);
        DeployTrace wide = DeployOnce(OpenPosition(), StandardWith(unlimited.Empties + extra));

        Assert.Equal(unlimited.Rehearsals, wide.Rehearsals);
        Assert.Equal(unlimited.Ai.LastCandidateCells, wide.Ai.LastCandidateCells);
        Assert.Equal(RankingText(unlimited.Ai), RankingText(wide.Ai));
        Assert.Equal(unlimited.Ai.LastCandidates.Select(c => $"{c.Key}={c.Total}"), wide.Ai.LastCandidates.Select(c => $"{c.Key}={c.Total}"));
        Assert.Equal(unlimited.Placements, wide.Placements);
        Assert.Equal(unlimited.Ai.Decisions, wide.Ai.Decisions);
    }

    [Theory]
    [InlineData("a", "F5")]
    [InlineData("b", "E5")]
    [InlineData("c", "F5")]
    public void 小K下仍找到妙手(string position, string key)
    {
        // 提子点 / 救命点没有单独的豁免名单：提子走"敌方损失"维、补气走"安全"维，都与落下的类型无关，代表类型的格分自然把它们排进前 K。
        // 局面同「高部署上限下的候选剪枝 · 必须找到的妙手」，K 取 4。
        MatchFlow match = position switch
        {
            "a" => 启发式评价维度Tests.CaptureRelicPosition(),
            "b" => AiFixtures.Round5()
                .Stones(AiFixtures.P0, "D4", "D5", "F5", "G4", "E3", "F3")
                .Stones(AiFixtures.P1, "D6", "E6", "C5", "C4", "E4", "F4", "D3"),
            "c" => AiFixtures.Round5(relics: [("E6", RelicFixtures.Conscription())])
                .Stones(AiFixtures.P0, "D5", "D6", "E4", "E7", "F7", "G6")
                .Stones(AiFixtures.P1, "E5", "E6", "F6"),
            _ => throw new ArgumentOutOfRangeException(nameof(position)),
        };

        HeuristicTurnController ai = HeuristicAi.Create(match, AiFixtures.P0, AiDifficulty.Standard, config: StandardWith(4));
        StagedBatch batch = match.OpenDeploy();
        ai.Deploy(batch, match.Rehearse);

        Assert.Equal(4, ai.LastCandidateCells.Length);
        Assert.Contains(TestMaps.At(key), ai.LastCandidateCells);
        Assert.Contains(batch.Placements, p => p.Coord == TestMaps.At(key));
        SettlementOutcome outcome = match.Confirm();
        Assert.True(outcome.Confirmed);
        Assert.True(outcome.CaptureRecord is { } captured && captured.Captured.Length >= 1, "妙手必须真的提子");
    }

    [Fact]
    public void 启用后同种子两次运行逐步相同()
    {
        // 显式 K = 2：每个小回合都走预筛。预筛不消费随机流、平分按坐标序 → 两次运行快照逐字节相同。
        // K 取 2（小于部署上限）是为了让这一局必然不同于不限制的那一局（原在 v4 上实测 K = 8 与不限制逐步相同）。
        // retire-legacy-maps 段 A：改到 4 人棋盘图种子 GoldenSeed；该图缺省 K = 24，所以另断言与缺省那一局也不同。
        static MatchLog Run() => BatchRunner.Execute(
            SimFixtures.Config(seedStart: GoldenSeed, turnLimit: GoldenTurns, difficulty: AiDifficulty.Standard) with { CandidateCellLimit = 2 }, parallelism: 1)[0];

        // 变异 M-K9：Sim 的 MatchSession 建 AI 时不传跑局配置的 K → 与不限制的那一局相同 → 本测试红。
        MatchLog a = Run();
        MatchLog b = Run();

        Assert.False(a.IsFailed);
        Assert.Equal(TurnHash(a), TurnHash(b));
        Assert.NotEqual(UnlimitedTurnHash, TurnHash(a));   // K 确实传到了 AI：与不限制的那一局不同
        Assert.NotEqual(BoardGoldenTurnHash, TurnHash(a));   // 也不同于按地图缺省 K = 24 的那一局
        Assert.Equal(2, a.Header.Config.CandidateCellLimit);
    }

    // ---------- 缺省 K：阈值逻辑只有一处 ----------

    [Fact]
    public void 大图缺省取上限小图为零显式配置优先()
    {
        // 变异 M-K5：DefaultCellLimitFor 的 `>` 改成 `>=` → 边界断言红。
        Assert.Equal(0, AiSearchConfig.DefaultCellLimitFor(AiSearchConfig.LargeMapPlayableThreshold));
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, AiSearchConfig.DefaultCellLimitFor(AiSearchConfig.LargeMapPlayableThreshold + 1));
        Assert.True(AiSearchConfig.LargeMapCellLimit > 0);

        // retire-legacy-maps 段 A："大图"由边疆图改为 4 人棋盘图。段 A2："小图"由 v5 改为合成 9×9 图（内置图都 > 150 格）；
        // 预设不再写上限（null），ForMap 在小图上落成显式 0，与预设的生效值相同。
        int v4 = SmallMap.PlayableCount;
        int frontier = MapCatalog.Resolve(SimFixtures.Board4).PlayableCount;
        Assert.True(v4 <= AiSearchConfig.LargeMapPlayableThreshold, $"小图可落子格 {v4}");
        Assert.True(frontier > AiSearchConfig.LargeMapPlayableThreshold, $"4 人棋盘图可落子格 {frontier}");
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(AiSearchConfig.ForDifficulty(d) with { CandidateCellLimit = 0 }, AiSearchConfig.ForMap(d, v4)));
        Assert.Equal(StandardWith(AiSearchConfig.LargeMapCellLimit), AiSearchConfig.ForMap(AiDifficulty.Standard, frontier));
        Assert.Equal(StandardWith(0), AiSearchConfig.ForMap(AiDifficulty.Standard, frontier, cellLimit: 0));
        Assert.Equal(StandardWith(5), AiSearchConfig.ForMap(AiDifficulty.Standard, v4, cellLimit: 5));
    }

    [Fact]
    public void 三个入口共用同一处阈值逻辑()
    {
        // src/godot 不在 siege.sln 里，只能做源码文本扫描（testing.md）；配样本口径下界与反面命中。
        // 变异 M-K6：把 src/godot/scripts/MatchSession.cs 的建 AI 一行改回 HeuristicAi.Create(Match, player, Difficulty) → 本测试红。M-K10：终端版同样改回 → 本测试红。
        string root = TestMaps.RepoRoot();
        string[][] entries =
        [
            ["src", "Siege.Sim", "Play", "PlayCommand.cs"],
            ["src", "Siege.Sim", "Running", "MatchSession.cs"],
            ["src", "godot", "scripts", "MatchSession.cs"],
        ];
        foreach (string[] entry in entries)
        {
            string text = File.ReadAllText(Path.Combine([root, .. entry]));
            Assert.Contains("AiSearchConfig.ForMap(", text, StringComparison.Ordinal);
            string[] creates = [.. text.Split('\n').Where(l => l.Contains("HeuristicAi.Create(", StringComparison.Ordinal) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal))];
            Assert.NotEmpty(creates);
            Assert.All(creates, l => Assert.Contains("search)", l, StringComparison.OrdinalIgnoreCase));
        }

        // 阈值与缺省 K 两个常量只在 Core 里被读：下游不得自己比较可落子格数。
        string[] downstream =
        [
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "Siege.Sim"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(Path.Combine(root, "src", "godot", "scripts"), "*.cs", SearchOption.AllDirectories),
        ];
        Assert.True(downstream.Length >= 20, $"样本口径：只扫到 {downstream.Length} 个下游文件。");
        Assert.Empty(downstream
            .Where(p => File.ReadAllText(p) is var t && (t.Contains("LargeMapPlayableThreshold", StringComparison.Ordinal) || t.Contains("LargeMapCellLimit", StringComparison.Ordinal)))
            .Select(Path.GetFileName));
        Assert.Contains("LargeMapPlayableThreshold", File.ReadAllText(Path.Combine(root, "src", "Siege.Core", "Ai", "AiDifficulty.cs")), StringComparison.Ordinal);
    }

    // ---------- 回放 ----------

    [Fact]
    public void 首部没有上限项的大图旧日志按不限制回放()
    {
        // 该项出现之前的边疆图日志首部没有 CandidateCellLimit：当时就是不限制。回放 MUST NOT 按今天的大图缺省 K 重跑，重建的首部也不得多出一项。
        // 变异 M-K12：Replayer 改回不带 recorded 的 MatchSession.Create → 重建首部多出该项、第 1 行即分歧 → 本测试红。
        RunConfig frontier = SimFixtures.Config(turnLimit: 4);   // retire-legacy-maps 段 A：大图由边疆图改为 4 人棋盘图（夹具缺省）
        Assert.Equal(SimFixtures.Board4, frontier.MapId);
        MatchLog zero = BatchRunner.Execute(frontier with { CandidateCellLimit = 0 }, parallelism: 1)[0];
        string text = zero.DeterministicText();
        Assert.Contains("\"CandidateCellLimit\":0,", text, StringComparison.Ordinal);
        MatchLog old = MatchLog.Parse(text.Replace("\"CandidateCellLimit\":0,", string.Empty, StringComparison.Ordinal));
        Assert.Null(old.Header.Config.CandidateCellLimit);

        ReplayResult replay = Replayer.Replay(old);

        Assert.True(replay.Identical, replay.ToString());
        Assert.Null(replay.Replayed.Header.Config.CandidateCellLimit);

        // 会话层：新建的局按地图取缺省 K 并写进首部配置；按首部重建的局不取。
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, MatchSession.Create(frontier, 1).CellLimit);
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, MatchSession.Create(frontier, 1).Config.CandidateCellLimit);
        Assert.Equal(0, MatchSession.Create(frontier, 1, map: null, recorded: true).CellLimit);
        Assert.Equal(7, MatchSession.Create(frontier with { CandidateCellLimit = 7 }, 1, map: null, recorded: true).CellLimit);
    }

    // ---------- 配置记录 ----------

    [Fact]
    public void 剪枝参数的文本往返与旧文本兼容()
    {
        // ai-eye 段 B：旧文本没有停手阈值，按 0 读入（当时的保留条件就是严格提高），所以等于"标准预设去掉阈值"，不再等于预设本身（预设阈值为缺省 20）。
        AiSearchConfig old = JsonSerializer.Deserialize<AiSearchConfig>("""{"CandidatePointCount":12,"CandidateBatchCount":8,"ImmediateOnly":false}""")!;
        Assert.Equal(AiSearchConfig.Standard with { PassThreshold = 0 }, old);
        AiSearchConfig tuned = AiSearchConfig.Standard with { PassThreshold = 7 };
        Assert.Equal(tuned, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(tuned)));

        AiSearchConfig limited = StandardWith(16);
        Assert.Equal(limited, JsonSerializer.Deserialize<AiSearchConfig>(JsonSerializer.Serialize(limited)));
        Assert.Throws<ArgumentOutOfRangeException>(() => StandardWith(-1).Validated());
    }

    [Fact]
    public void 跑局配置的文本往返()
    {
        // 变异 M-K7：RunConfig.CandidateCellLimit 标 [JsonIgnore] → 本测试红。
        RunConfig unset = SimFixtures.Config();
        Assert.Null(unset.CandidateCellLimit);
        Assert.DoesNotContain("CandidateCellLimit", unset.ToJson(), StringComparison.Ordinal);
        Assert.Null(RunConfig.FromJson(unset.ToJson()).CandidateCellLimit);

        foreach (int k in new[] { 0, 16 })
        {
            RunConfig set = unset with { CandidateCellLimit = k };
            Assert.Contains($"\"CandidateCellLimit\": {k}", set.ToJson(), StringComparison.Ordinal);
            Assert.Equal(k, RunConfig.FromJson(set.ToJson()).CandidateCellLimit);
        }

        Assert.Throws<ArgumentException>(() => (unset with { CandidateCellLimit = -1 }).Validated());
    }

    [Fact]
    public void 批次配置记录写入实际生效的上限()
    {
        // 大图（retire-legacy-maps 段 A：边疆图 → 4 人棋盘图，夹具缺省）、Easy、1 个大回合。读 config.json 原文，不经反序列化，避免缺省值掩盖漏写。
        // 变异 M-K8：BatchRunner.ExecuteToDirectory 去掉 ResolvedFor → config.json 不含该项 → 本测试红。
        RunConfig auto = SimFixtures.Config(turnLimit: 4);
        Assert.Equal(SimFixtures.Board4, auto.MapId);
        string autoDir = SimFixtures.TempDir("cell-limit-auto");
        BatchRunner.ExecuteToDirectory(auto, autoDir, parallelism: 1);
        Assert.Contains($"\"CandidateCellLimit\": {AiSearchConfig.LargeMapCellLimit}", File.ReadAllText(Path.Combine(autoDir, "config.json")), StringComparison.Ordinal);
        MatchLog autoLog = MatchLog.Read(Directory.EnumerateFiles(autoDir, "match-*.jsonl").Single());
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, autoLog.Header.Config.CandidateCellLimit);

        // 显式 0（不限制）优先于大图缺省，并如实写出。
        string zeroDir = SimFixtures.TempDir("cell-limit-zero");
        BatchRunner.ExecuteToDirectory(auto with { CandidateCellLimit = 0 }, zeroDir, parallelism: 1);
        Assert.Contains("\"CandidateCellLimit\": 0", File.ReadAllText(Path.Combine(zeroDir, "config.json")), StringComparison.Ordinal);

        // 小图：配置记录与引入本项之前一样，不多出这一项。retire-legacy-maps 段 A2：内置图都 > 150 格，改用合成 9×9 图直接验落成
        //（BatchRunner.ExecuteToDirectory 写的就是 ResolvedFor 的结果，M-K8 由上面两段钉住）；正反两面：同一配置在大图上落成 24。
        Assert.DoesNotContain("CandidateCellLimit", auto.ResolvedFor(SmallMap).ToJson(), StringComparison.Ordinal);
        Assert.Contains($"\"CandidateCellLimit\": {AiSearchConfig.LargeMapCellLimit}", auto.ResolvedFor(MapCatalog.Resolve(auto.MapId)).ToJson(), StringComparison.Ordinal);
    }
}
