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
    /// 黄金值（retire-legacy-rules 1.4 改钉，清单 P1 / R8）：手写小链接档图（<see cref="SmallLinkMap"/>，链接模式、四人同锁棋盘 0）、种子 31、4 名标准 AI（权重与停手阈值写死为
    /// <see cref="SimFixtures.PreCalibrationWeights"/> / <see cref="SimFixtures.PreCalibrationPassThreshold"/>）、跑满 6 个大回合，全体 AI 的决策日志与终了盘面序列化的 SHA-256，
    /// 以改钉时的实现在新载体上自证钉下；旧值 DEB4ED371A7E9EC71B08517058C06148B8919A8D4B26458FFC229C50EA3B87E4（9×9 手写夹具、Legacy 规则）。
    /// <para>retire-legacy-maps 1.5 时的口径：9×9 手写夹具（Legacy 规则，裁决 L6）、种子 31、4 名标准 AI（权重与停手阈值写死为
    /// <see cref="SimFixtures.PreCalibrationWeights"/> / <see cref="SimFixtures.PreCalibrationPassThreshold"/>）、跑满 6 个大回合，全体 AI 的决策日志（<see cref="AiFixtures.DecisionLog"/>）
    /// 与终了盘面序列化的 SHA-256。在删除 v5 之前、以当时的实现钉下。</para>
    /// <para>原黄金值 <c>V4GoldenTurnHash</c>（v5、种子 31、24 个小回合的快照哈希，历次重建记录见 git 历史）随 v5 删除：v5 的 105 格不超过 150、缺省 K = 0，
    /// 守的是"小图不限制时走法不变"；链接图一律超过 150 格（<c>link:1</c> 为 157），这条规则今后只能在手写小图上守（规格「小图不限制」：如 9×9 手写测试夹具）。</para>
    /// </summary>
    /// <para>fence-eye-rule 2.2 重钉：本值是 AI 决策日志 + 终了盘面，<b>不是</b>只依赖地图的值——小图上的 AI 会征募匠人并立栅。首个分歧在第 1 大回合首位行动者 P3 的第一手：
    /// 旧规则下它落 F3 匠人并立栅 G3–G4（贴着 5×5 棋盘外沿的格被栅栏围成"眼"），新规则下栅栏不再造眼，改落 D4 普通子；此后全局走法随之变。旧值 26AF3B28F94F88CA44847D5F9BF7B41733CFEC357CFF6A9CA7F337523CB4BD41。</para>
    /// <para>ai-whole-batch 1.6 重钉（标准预设缺省开整批候选）：首个分歧在第 2 大回合 P0 的第一手（第 6 小回合），原落 E5 堡垒子，开启后选中配对候选
    /// E5 连珠 + C6 哨兵 + D7 堡垒（见任务目录 rebaseline.txt）。旧值 5B16259CE148873CE2540C584BCA9645B525906ADCA02827E674099170BDE994（开关 0 下不变，见 <see cref="SmallMapGoldenHashWholeBatchOff"/>）。</para>
    internal const string SmallMapGoldenHash = "E77AA12B9687CA621913356A833543C1EA9E2B17807B5C56DE50235B93869298";

    /// <summary>ai-whole-batch 守门：<see cref="SmallMapGoldenHash"/> 同口径、整批候选开关为 0 时的黄金值——即引入整批候选之前的钉值。</summary>
    internal const string SmallMapGoldenHashWholeBatchOff = "5B16259CE148873CE2540C584BCA9645B525906ADCA02827E674099170BDE994";

    /// <summary>ai-bridge-candidate 守门：<see cref="SmallMapGoldenHash"/> 同口径、搭桥候选开关为 0（整批候选开）时的黄金值——即引入搭桥候选之前（HEAD 01efcd4）的钉值。</summary>
    internal const string SmallMapGoldenHashEditSeedOff = "E77AA12B9687CA621913356A833543C1EA9E2B17807B5C56DE50235B93869298";

    /// <summary>
    /// 链接图上"此前记录过的对局"（retire-legacy-maps 1.5：原 v5 的 <c>V4GoldenTurnHash</c> 在 <c>对局内容集Tests</c>、<c>带入带出配置Tests</c>、<c>批量跑局Tests</c> 里充当这一角色）：
    /// <c>link:1</c>、种子 31、4 名标准 AI、24 个小回合、跑局层（<see cref="BatchRunner"/>，种子派生配对）、<see cref="SimFixtures.PinPreCalibration"/> 写死口径，
    /// 全部小回合快照（去耗时）逐行拼接后的 SHA-256（<see cref="TurnHash"/>）。在删除 v5 之前、以当时的实现钉下。
    /// <para>link-map-terrain 第 2 组重钉（M10）：link:1 的地图内容变了（出生棋盘 6–8、主题地形、出生区为核心区、10–15 个信物格），同一配置的走法随之变；旧值 B4A74124D2C6C25CD8576ACC8C444D3D4A71A7C076E9CC1310725C06C61BF498。"此前记录过的对局"的角色从这次重钉起算。</para>
    /// </summary>
    /// <para>open-map 1.5 重钉：link:1 改为 3+2 紧贴布局，同一配置的走法随之变；旧值 1CFBFF61319693C0E9593642D890D221A461A4739EA2CCE8E0C1BF85E2ECB538。</para>
    /// <para>open-map 段 B 重钉（格式变化、走法不变）：快照 JSON 的玩家项去掉 WaitingRounds、结算项去掉 Deadline；旧值 8A03DA5EA2B81E5D233B55145161AC101498D71FFD976E9B02CF0E225C1CD1BE。5 份决策序列基线逐字节不变。</para>
    /// <para>link-map-surfaces 2.2 重钉：link:1 的中间带投放了新地表（9 格，见任务目录 export/diff-before-after.txt），同一配置的走法随之变；旧值 C898F79260501E911192893115646DE6DE451CCE8A5BA6FECC7242A1A5FC9727。</para>
    /// <para>link-map-surfaces 1.3 再重钉（实施后裁决 F1 / F2：小棋盘免除核心区四邻限制、混合主题补位，link:1 新地表 9 → 10 格）；第一次重钉值 900E334FCB5EFA59B2108598EC38E2DF60A9FCBB576E643F9E03DA143B4853C9。</para>
    /// <para>fence-eye-rule 2.2 重钉：活形判定无视栅栏（栅栏不再造眼、栅栏阵主题的预置栅栏也不再算墙），同一配置的 AI 走法随之变；旧值 A75EDD49F9C3E8EAABD186C5F7D1980CBAB6E47ADFC12DE68D3B8E6E2138FF78。</para>
    /// <para>terrain-barriers 3.1 重钉（地图变了：link:1 的河流 / 高台改为横穿棋盘的隔断线、大棋盘加湖泊 / 岩区，同一配置的走法随之变）；旧值 F471700800B1A38A72AF2C31B6516847F2BD04136D2E622FEF3BD374EB7ED47A。</para>
    /// <para>terrain-density 2.2 重钉（地图变了：出生棋盘边长 6–8 → 6–7，link:1 的五块棋盘与地形全部重抽，同一配置的走法随之变）；旧值 DC4CDF0CFC681BF1455267BF5FD8D7DE448201ADBBBDCB921268123FA124842B。</para>
    /// <para>ai-bridge-candidate 1.6 重钉（标准预设缺省开搭桥候选）：首个分歧在第 16 小回合（第 4 大回合）P1，原落 E10 堡垒 + D13 普通，开启后选中搭桥候选
    /// E7 匠人搭桥 D7 + D13 堡垒（见任务目录 rebaseline.txt）。旧值 58F2C051F9797F545F4D9F81F44AA98DB4D51000CDE00009DDBADE265BC4248A（开关 0 下不变，见 <see cref="LinkGoldenTurnHashEditSeedOff"/>）。</para>
    /// <para>map-expand-25 段 A2 重钉（地图变了：link:1:4 换成 44×44 浮空岛，同一配置的走法随之变）；旧值 AA44B22BB943731EDDAB8F3FEE7A175622352069E604667F5AA66542B8CD0814。
    /// 本值是当前实现在新图上的自证钉值；旁证只有"同一配置经 对局内容集（v1 逐步相同）/ 带入带出（关闭、开启但无人带入）/ 批量跑局（缺省关闭带入）四条路径得到同一哈希"。</para>
    /// <para>map-expand-25 段 A3 重钉（地图变了：不放静态信物格后布局流不再为信物消费随机，link:1:4 的坡道与低地重抽、可落子 1485 → 1484，平台落位 / 主题 / 平台内部不变）；旧值 101B7111E1A1DE624BF275A1D72736AEC78BB07B339EE334045737D0D4A8CF3E。
    /// 四条路径实跑得到同一新值。</para>
    /// <para>relic-redesign 接线段 W1 重钉（旧信物删除 + 地图信物第 3 大回合起预告、第 4 大回合起落位，AI 评价版本 11 → 12；同一配置的走法随之变）；快照行另删去旧信物字段（信物状态 / 驿站来源 / 工坊、棋串连营 / 犄角），哈希输入格式也变了。
    /// 旧值 C3CB36FB366073E995D97B344C7B7EC8B405FD488FD4B824C9FD93BA1C5EC4EC；四条路径实跑得到同一新值。</para>
    /// <para>relic-redesign 段 C1 重钉（信物维改为势力增量口径 + AI 挂出改为势力增量最大落点，AI 评价版本 12 → 13；盘面有信物起同一配置的走法随之变）：旧值 1E42C386CD06B6BA1BBC2CD138D0EC05742AFC7F569EE9D98937E3260F30D8EE；四条路径（对局内容集 v1 逐步相同 / 带入带出关闭 / 开启但无人带入 / 批量跑局缺省关闭带入）实跑得到同一新值。</para>
    internal const string LinkGoldenTurnHash = "1E6AC219F967E612CC550AD7A933BA42BC9E7706599D65F27442FE5FD16F0B4D";

    /// <summary>
    /// ai-whole-batch 守门（规格「整批候选」Scenario「关闭时逐步不变」）：<see cref="LinkGoldenConfig"/> 显式给整批候选开关 0 时的小回合快照哈希——
    /// 即引入整批候选之前（HEAD 0b1b4bf）的 <see cref="LinkGoldenTurnHash"/>，开关 0 MUST 与它逐步相同（首部不写该字段，哈希输入不变）。
    /// </summary>
    /// <remarks>map-expand-25 段 A2 重钉（地图变了）：旧值 58F2C051F9797F545F4D9F81F44AA98DB4D51000CDE00009DDBADE265BC4248A。重钉后不再是"引入整批候选之前"的独立参照，而是开关 0 路径在新图上的自证值（与 <see cref="LinkGoldenTurnHashEditSeedOff"/> 同值，改前亦同值）。
    /// 段 A3 重钉（地图变了：不放静态信物格后布局流不再为信物消费随机，link:1:4 的坡道与低地重抽、可落子 1485 → 1484，平台落位 / 主题 / 平台内部不变）：旧值 F785984A642F4D97CA4EA8B5DB98C3B7E8267190EEB961D8D02AF8A2EEB5CF6F，仍与 EditSeedOff 同值。
    /// relic-redesign 接线段 W1 重钉（旧信物删除 + 地图信物第 3 大回合起预告、第 4 大回合起落位，AI 评价版本 11 → 12；同一配置的走法随之变）：旧值 622BD1876D688FD2CD8C33BC3D90C4FC91BFFB2107FD76FFD75CD3AB80884A01，仍与 EditSeedOff 同值、与缺省开的 <see cref="LinkGoldenTurnHash"/> 不同。
    /// relic-redesign 段 C1 重钉（信物维改为势力增量口径 + AI 挂出改为势力增量最大落点，AI 评价版本 12 → 13；盘面有信物起同一配置的走法随之变）：旧值 1F68747E41B1158C6AF3035364EE51D478322ED49C6D929E5B0618087A8956B2，仍与 EditSeedOff 同值、与缺省开的 <see cref="LinkGoldenTurnHash"/> 不同。</remarks>
    internal const string LinkGoldenTurnHashWholeBatchOff = "D0173938F25DEB73846035FC0EB280D60353D61F50267924B9121B792692C47C";

    /// <summary>
    /// ai-bridge-candidate 守门（规格「搭桥候选」Scenario「关闭时逐步不变」）：<see cref="LinkGoldenConfig"/> 显式给搭桥候选开关 0 时的小回合快照哈希——
    /// 即引入搭桥候选之前（HEAD 01efcd4）的 <see cref="LinkGoldenTurnHash"/>（首部不写该字段，哈希输入不变）。
    /// </summary>
    /// <remarks>map-expand-25 段 A2 重钉（地图变了）：旧值 58F2C051F9797F545F4D9F81F44AA98DB4D51000CDE00009DDBADE265BC4248A。重钉后是开关 0 路径在新图上的自证值。
    /// 段 A3 重钉（地图变了：不放静态信物格后布局流不再为信物消费随机，link:1:4 的坡道与低地重抽、可落子 1485 → 1484，平台落位 / 主题 / 平台内部不变）：旧值 F785984A642F4D97CA4EA8B5DB98C3B7E8267190EEB961D8D02AF8A2EEB5CF6F。
    /// relic-redesign 接线段 W1 重钉（旧信物删除 + 地图信物第 3 大回合起预告、第 4 大回合起落位，AI 评价版本 11 → 12；同一配置的走法随之变）：旧值 622BD1876D688FD2CD8C33BC3D90C4FC91BFFB2107FD76FFD75CD3AB80884A01（仍与 WholeBatchOff 同值）。
    /// relic-redesign 段 C1 重钉（信物维改为势力增量口径 + AI 挂出改为势力增量最大落点，AI 评价版本 12 → 13；盘面有信物起同一配置的走法随之变）：旧值 1F68747E41B1158C6AF3035364EE51D478322ED49C6D929E5B0618087A8956B2（仍与 WholeBatchOff 同值）。</remarks>
    internal const string LinkGoldenTurnHashEditSeedOff = "D0173938F25DEB73846035FC0EB280D60353D61F50267924B9121B792692C47C";

    /// <summary><see cref="LinkGoldenTurnHash"/> 的跑局配置（<c>link:1</c>、种子 31、标准、24 个小回合、写死口径）。</summary>
    internal static RunConfig LinkGoldenConfig() =>
        SimFixtures.PinPreCalibration(SimFixtures.Config(seedStart: 31, turnLimit: 24, difficulty: AiDifficulty.Standard) with { MapId = "link:1:4" });


    internal static string TurnHash(MatchLog log) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', SimFixtures.TurnTexts(log.Turns)))));

    /// <summary>与 <see cref="LinkGoldenTurnHash"/> 比对并在失败时打印完整实际值（Assert.Equal 会截断 64 位哈希）。</summary>
    internal static void AssertLinkGolden(MatchLog log)
    {
        string actual = TurnHash(log);
        Assert.True(LinkGoldenTurnHash == actual, $"链接图黄金小回合哈希：{actual}");
    }

    private static AiSearchConfig StandardWith(int cellLimit) => AiSearchConfig.Standard with { CandidateCellLimit = cellLimit };

    /// <summary>第 5 大回合、部署上限 8、60+ 合法空格的开放局面（与「不穷举排列」同一局面）。</summary>
    private static MatchFlow OpenPosition()
    {
        MatchFlow match = AiFixtures.Round5().Stones(AiFixtures.P1, "G7", "H8", "F9").Stones(AiFixtures.P2, "J5");
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

    /// <summary>
    /// 手写小链接档图（retire-legacy-rules 1.4，清单 P1）：五块 5×5 出生棋盘一字排开（x = 2 / 10 / 18 / 26 / 34，间距 3），全平地、无林地、无信物格，
    /// 可落子 125 格（不超过 <see cref="AiSearchConfig.LargeMapPlayableThreshold"/>）。链接档图的可落子格恒超过 150，"小图"只能是未校验的手写图（边长 5 低于出生棋盘下限 6）。
    /// </summary>
    private static readonly MapData SmallLinkMap = LinkMapFixtures.Build(
        "link:90005:4", [.. Enumerable.Range(0, 5).Select(i => new BoardPlate(new Coord(2 + (i * 8), 2), 5, 5, BoardPlateKind.Birth, (BoardTheme)i))]);

    /// <summary>
    /// 小链接档图上 4 名标准 AI 跑满 6 个大回合：返回全体 AI 的决策日志与终了盘面序列化。retire-legacy-maps 1.5 起是 v5 整局的替身（9×9 手写夹具，Legacy 规则）；
    /// retire-legacy-rules 1.4 起载体换为 <see cref="SmallLinkMap"/>（链接模式、K = 0，四人同锁棋盘 0，各持 50 枚普通子）。
    /// 权重与停手阈值写死为 <see cref="SimFixtures.PreCalibrationWeights"/> / <see cref="SimFixtures.PreCalibrationPassThreshold"/>（本测试钉的是"K 缺省不改变走法"，不是默认权重）。
    /// </summary>
    private static string SmallMapRun(AiSearchConfig config, ulong seed = 31)
    {
        MatchFlow match = LinkModeFixtures.RequireLink(MatchFlow.CreateUnvalidated(
            SmallLinkMap, new Siege.Core.Determinism.GameSeed(seed), TestMaps.All, LinkModeFixtures.PlainOptions(null)));
        foreach (PlayerId p in TestMaps.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(TestMaps.All.Select(p => (p, 0)));
        var runner = new MatchRunner(match);
        ImmutableSortedDictionary<PlayerId, HeuristicTurnController> ais =
            runner.AttachAi(AiDifficulty.Standard, SimFixtures.PreCalibrationWeights, config with { PassThreshold = SimFixtures.PreCalibrationPassThreshold });
        runner.RunMajorRounds(6);
        return AiFixtures.DecisionLog(ais) + "\n" + match.Board.Serialize();
    }

    [Fact]
    public void 小图不限制()
    {
        // retire-legacy-maps 1.5：原「缺省不限制时标准图整局与改动前逐步相同」（v5、种子 31、24 个小回合的快照黄金哈希），随 v5 改到 9×9 手写夹具，
        // 测试名随规格 Scenario「小图不限制」（可落子格不超过 150 的地图，如 9×9 手写测试夹具）。所守规则不变：小图上缺省 K 为 0，决策与显式 K = 0 逐步相同，且与钉下的黄金值相同。
        // 变异 M-K1：RankPoints 的启用条件改成恒真（K = 0 也预筛，Take(0) 取空）→ 黄金值红（retire-legacy-maps 改钉后重做，见 migration.md）。
        // retire-legacy-rules 1.4（清单 P1）：载体由 9×9 手写夹具（81 格）改为手写小链接档图（125 格，见 SmallLinkMap），黄金值按新载体以当前实现重钉（见迁移清单第 6 节）。
        int playable = SmallLinkMap.PlayableCount;
        Assert.Equal(125, playable);
        Assert.True(playable <= AiSearchConfig.LargeMapPlayableThreshold, $"夹具可落子格 {playable}");
        AiSearchConfig auto = AiSearchConfig.ForMap(AiDifficulty.Standard, playable);
        Assert.Equal(0, auto.CandidateCellLimit);

        string byDefault = SmallMapRun(auto);
        string explicitZero = SmallMapRun(StandardWith(0));
        Assert.Equal(explicitZero, byDefault);

        // 样本口径：6 个大回合里确有落子（不是全员 Pass 的空局）。
        Assert.Contains(" D:", byDefault, StringComparison.Ordinal);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(byDefault)));
        Assert.True(SmallMapGoldenHash == hash, $"小链接档图种子 31 的决策与盘面哈希变了：现为 {hash}。");

        // ai-whole-batch 守门：整批候选开关为 0 时与引入之前的钉值逐字节相同。
        string off = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SmallMapRun(auto with { WholeBatch = 0, EditSeed = 0 }))));
        Assert.True(SmallMapGoldenHashWholeBatchOff == off, $"开关 0 的小链接档图哈希：{off}。");

        // ai-bridge-candidate 守门：搭桥候选开关为 0（整批候选开）时与引入搭桥候选之前的钉值逐字节相同。
        string editSeedOff = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SmallMapRun(auto with { EditSeed = 0 }))));
        Assert.True(SmallMapGoldenHashEditSeedOff == editSeedOff, $"搭桥候选开关 0 的小链接档图哈希：{editSeedOff}。");
    }

    [Fact]
    public void 难度默认参数不启用上限()
    {
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(0, AiSearchConfig.ForDifficulty(d).CandidateCellLimit));
        Assert.Equal(0, HeuristicAi.Create(OpenPosition(), AiFixtures.P0).Config.CandidateCellLimit);
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
        // ai-whole-batch：标准预设开着整批候选，组合另加整批候选的上界（见 高部署上限下的候选剪枝Tests.WholeBatchRehearsalBound）。
        int combine = AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1)
            + 高部署上限下的候选剪枝Tests.WholeBatchRehearsalBound(AiSearchConfig.Standard.CandidatePointCount, 8);
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
    /// C4 是深水、D3 是敌子（气：C3、E3；D4 是己子）：C3 的气边邻格只有 D3，不带改造落 C3 是自杀手。
    /// 匠人落 C3 同时立栅 D3–E3，则 D3 只剩 C3 一口气、随落子被提，C3 合法且提一子（terrain-edit T-3：改造先于提子与自杀手判定）；
    /// 在 C4 搭桥也能让 C3 合法（补一口气），但不提子。这是一手"只有带改造的匠人才下得出"的妙手。
    /// </summary>
    private static MatchFlow ArtisanOnlyCellPosition(bool holdsArtisan)
    {
        MatchFlow match = LinkModeFixtures.PlainStarted(TestMaps.Terrain(surfaces: [("C4", Surface.DeepWater)]))
            .AtRound(5, [AiFixtures.P0, AiFixtures.P1, AiFixtures.P2, AiFixtures.P3])
            .Stones(AiFixtures.P0, "D4")
            .Stones(AiFixtures.P1, "D3");
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
        Coord a1 = TestMaps.At("C3");
        MatchFlow oracleMatch = ArtisanOnlyCellPosition(holdsArtisan: true);
        HeuristicTurnController oracleAi = HeuristicAi.Create(oracleMatch, AiFixtures.P0);
        StagedBatch batch = oracleMatch.OpenDeploy();
        BatchEvaluator evaluator = oracleAi.CreateEvaluator();
        Coord[] empties = [.. batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        ImmutableArray<TerrainEdit> a1Edits = TerrainEditRules.LegalTargets(batch.Board.Map, a1);

        // 前提：C3 不带改造是自杀手，带改造（立栅 D3–E3 / 在 C4 搭桥）才合法；其余空格普通子都落得下。
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
        Assert.Contains(TerrainEdit.Bridge(TestMaps.At("C4")), a1Edits);
        Assert.Contains(TerrainEdit.Fence(TestMaps.At("D3"), TestMaps.At("E3")), a1Edits);
        Assert.NotEmpty(a1Totals);
        scored.Add((a1, a1Totals.Max()));

        // K 取"恰好把 C3 收进来"的名次：C3 在前 K 格里，K 仍小于合法空格数（预筛生效）。
        (Coord Cell, BigInteger Total)[] ranked = [.. scored.OrderByDescending(s => s.Total).ThenBy(s => s.Cell)];
        int k = Array.FindIndex(ranked, s => s.Cell == a1) + 1;
        Assert.InRange(k, 1, empties.Length - 1);   // A1 垫底时无法既收进它又让预筛生效——那样局面要重摆
        Coord[] expected = [.. ranked.Take(k).Select(s => s.Cell).Order()];
        Assert.Contains(a1, expected);

        DeployTrace limited = DeployOnce(ArtisanOnlyCellPosition(holdsArtisan: true), StandardWith(k));

        Assert.Equal(expected, limited.Ai.LastCandidateCells);
        Assert.True(a1Totals.Max() > a1Totals.Min(), "格分取的是各合法改造里最高的总分（立栅提子高于搭桥补气），不是第一个合法的。");

        // 开销：预筛每格 1 次 + 只在 C3 上逐个改造再试 + K 格的完整枚举（普通子 1 次、匠人 1 + 改造目标数）+ 组合 M × (N + 1)。
        int fullEnumeration = limited.Ai.LastCandidateCells.Sum(c => 2 + TerrainEditRules.LegalTargets(batch.Board.Map, c).Length);
        int combine = AiSearchConfig.Standard.CandidateBatchCount * (AiSearchConfig.Standard.CandidatePointCount + 1);
        Assert.True(
            limited.Rehearsals <= empties.Length + a1Edits.Length + fullEnumeration + combine,
            $"预演 {limited.Rehearsals} 次，上界 {empties.Length} + {a1Edits.Length} + {fullEnumeration} + {combine}");

        // 手里没有匠人：C3 谁也落不下，不进候选，也不为它多做预演。
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
    [InlineData("a", "G6")]
    [InlineData("b", "F6")]
    [InlineData("c", "G6")]
    public void 小K下仍找到妙手(string position, string key)
    {
        // 提子点 / 救命点没有单独的豁免名单：提子走"敌方损失"维、补气走"安全"维，都与落下的类型无关，代表类型的格分自然把它们排进前 K。
        // 局面同「高部署上限下的候选剪枝 · 必须找到的妙手」，K 取 4。
        MatchFlow match = position switch
        {
            // retire-legacy-rules 1.4（清单 P2）：局面 a / b 里 P0 原本势力落后（a 的先手位用例就靠这一点），链接规则下落后者的停手阈值取 0；
            // 这里在远离妙手的盘角给 P0 补一块 2×2，使其居本战场首位（AsLeader 断言前提）。局面 c 里 P0 本就领先。
            "a" => 启发式评价维度Tests.CaptureRelicPosition().AsLeader(AiFixtures.P0, "J3", "K3", "J4", "K4"),
            "b" => AiFixtures.Round5()
                .Stones(AiFixtures.P0, "E5", "E6", "G6", "H5", "F4", "G4")
                .Stones(AiFixtures.P1, "E7", "F7", "D6", "D5", "F5", "G5", "E4")
                .AsLeader(AiFixtures.P0, "J9", "K9", "J10", "K10"),
            "c" => AiFixtures.Round5()
                .Stones(AiFixtures.P0, "E6", "E7", "F5", "F8", "G8", "H7")
                .Stones(AiFixtures.P1, "F6", "F7", "G7"),
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
        // retire-legacy-maps 1.5：原在 v4（105 格）上跑，改到 link:1（157 格）；对照局由"v4 不限制的黄金局"改为同种子显式 K = 0 的一局，断言不变。
        // 显式 K = 2：每个小回合都走预筛。预筛不消费随机流、平分按坐标序 → 两次运行快照逐字节相同。
        // K 取 2（小于部署上限）是为了让这一局必然不同于不限制的那一局，证明 K 确实传到了 AI。
        // map-expand-25 段 A2：link:1:4 换成约 1500 格的浮空岛后，K = 0（不限制）的标准 AI 每步要逐格评价全岛，24 个小回合单条数分钟；截断改为 8 个小回合（前 2 个大回合），
        // 所守的"K = 2 两次逐步相同、且与不限制的那一局不同"不变。
        static MatchLog Run(int k) => BatchRunner.Execute(
            SimFixtures.Config(seedStart: 31, turnLimit: 8, difficulty: AiDifficulty.Standard) with { MapId = "link:1:4", CandidateCellLimit = k }, parallelism: 1)[0];

        // 变异 M-K9：Sim 的 MatchSession 建 AI 时不传跑局配置的 K → 与不限制的那一局相同 → 本测试红。
        MatchLog a = Run(2);
        MatchLog b = Run(2);

        Assert.False(a.IsFailed);
        Assert.Equal(TurnHash(a), TurnHash(b));
        Assert.NotEqual(TurnHash(Run(0)), TurnHash(a));   // K 确实传到了 AI：与不限制的那一局不同
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

        // retire-legacy-maps 1.5：小图由 v4（105 格）改为 9×9 手写夹具（81 格），大图由边疆图（411 格）改为 link:1（157 格）；断言不变。
        int v4 = TestMaps.NineByNine().PlayableCount;
        int frontier = MapCatalog.Resolve("link:1:4").PlayableCount;
        Assert.True(v4 <= AiSearchConfig.LargeMapPlayableThreshold, $"小图可落子格 {v4}");
        Assert.True(frontier > AiSearchConfig.LargeMapPlayableThreshold, $"大图可落子格 {frontier}");
        Assert.All(Enum.GetValues<AiDifficulty>(), d => Assert.Equal(AiSearchConfig.ForDifficulty(d), AiSearchConfig.ForMap(d, v4)));
        Assert.Equal(StandardWith(AiSearchConfig.LargeMapCellLimit), AiSearchConfig.ForMap(AiDifficulty.Standard, frontier));
        Assert.Equal(AiSearchConfig.Standard, AiSearchConfig.ForMap(AiDifficulty.Standard, frontier, cellLimit: 0));
        Assert.Equal(StandardWith(5), AiSearchConfig.ForMap(AiDifficulty.Standard, v4, cellLimit: 5));
    }

    [Fact]
    public void 三个入口共用同一处阈值逻辑()
    {
        // src/godot 不在 siege.sln 里，只能做源码文本扫描（testing.md）；配样本口径下界与反面命中。
        // 变异 M-K6：把 src/godot/scripts/MatchSession.cs 的建 AI 一行改回 HeuristicAi.Create(Match, player, Difficulty) → 本测试红。M-K10：终端版同样改回 → 本测试红。
        string root = TestPaths.RepoRoot();   // retire-legacy-maps：RepoRoot 由已删除的 FrontierFixtures 原样迁到 TestPaths
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
        // 该项出现之前的大图日志首部没有 CandidateCellLimit：当时就是不限制。回放 MUST NOT 按今天的大图缺省 K 重跑，重建的首部也不得多出一项。
        // 变异 M-K12：Replayer 改回不带 recorded 的 MatchSession.Create → 重建首部多出该项、第 1 行即分歧 → 本测试红。
        // retire-legacy-maps 1.5：大图由边疆图改为 link:1（157 格 > 150），断言不变。
        RunConfig frontier = SimFixtures.Config(turnLimit: 4) with { MapId = "link:1:4" };
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
        // cycle-stop 2.2：旧文本同样没有停滞大回合数，按 0 读入（预设为 6）。
        // ai-whole-batch 1.2：旧文本同样没有整批候选开关，按 0 读入（预设为 1）。
        Assert.Equal(AiSearchConfig.Standard with { PassThreshold = 0, StagnationRounds = 0, WholeBatch = 0, EditSeed = 0 }, old);   // ai-bridge-candidate 1.2：缺搭桥候选开关同样按 0 读
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
        // link:1（大图）、Easy、1 个大回合（构筑期只有自家出生棋盘可落，跑得快）。读 config.json 原文，不经反序列化，避免缺省值掩盖漏写。
        // 变异 M-K8：BatchRunner.ExecuteToDirectory 去掉 ResolvedFor → config.json 不含该项 → 本测试红。
        // retire-legacy-maps 1.5：大图由边疆图改为 link:1；小图一段见下。
        RunConfig auto = SimFixtures.Config(turnLimit: 4) with { MapId = "link:1:4" };
        string autoDir = SimFixtures.TempDir("cell-limit-auto");
        BatchRunner.ExecuteToDirectory(auto, autoDir, parallelism: 1);
        Assert.Contains($"\"CandidateCellLimit\": {AiSearchConfig.LargeMapCellLimit}", File.ReadAllText(Path.Combine(autoDir, "config.json")), StringComparison.Ordinal);
        MatchLog autoLog = MatchLog.Read(Directory.EnumerateFiles(autoDir, "match-*.jsonl").Single());
        Assert.Equal(AiSearchConfig.LargeMapCellLimit, autoLog.Header.Config.CandidateCellLimit);

        // 显式 0（不限制）优先于大图缺省，并如实写出。
        string zeroDir = SimFixtures.TempDir("cell-limit-zero");
        BatchRunner.ExecuteToDirectory(auto with { CandidateCellLimit = 0 }, zeroDir, parallelism: 1);
        Assert.Contains("\"CandidateCellLimit\": 0", File.ReadAllText(Path.Combine(zeroDir, "config.json")), StringComparison.Ordinal);

        // 小图：配置记录与引入本项之前一样，不多出这一项。retire-legacy-maps 1.5：批量入口只接受链接图（一律超过 150 格），小图（原 v4）到不了批量入口；
        // 改在写出配置记录的同一处落成（RunConfig.ResolvedFor，ExecuteToDirectory 写 config.json 用的正是它）上对 9×9 手写夹具断言。
        Assert.DoesNotContain("CandidateCellLimit", SimFixtures.Config(turnLimit: 4).ResolvedFor(TestMaps.NineByNine()).ToJson(), StringComparison.Ordinal);
    }
}
