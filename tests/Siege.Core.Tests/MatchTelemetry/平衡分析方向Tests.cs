using System.Security.Cryptography;
using System.Text;
using Siege.Core.Board;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;

namespace Siege.Core.Tests.MatchTelemetry;

/// <summary>规格：match-telemetry —— Requirement: 平衡分析方向</summary>
public class 平衡分析方向Tests
{
    [Fact]
    public void 棋子选择率与胜率()
    {
        // 五种棋子各自的选择率（选取 / 展示）与选过它的玩家的关联胜率都被列出。合成：P0（胜者）选 Basic、Line；P1 选 Fortress。
        // 变异验证：本类以 M-B10 为准（见 最小落子拖延检测）。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"]), SimFixtures.Turn(2, 1, 1, [1, 1, 1, 1], ["H1:Basic"])],
            [
                SimFixtures.Recruit(1, 0, "Basic,Line,Basic,Fortress,Synergy", "Basic,Line"),
                SimFixtures.Recruit(2, 1, "Fortress,Multiplier,Basic,Basic,Line", "Fortress"),
            ],
            SimFixtures.ResultOf(5, [0]));

        SelectionSection s = BalanceAnalyzer.Analyze([log]).Selection;

        // more-pieces-relics（MODIFIED Scenario「该批次内容集中的每一种棋子」）：合成日志首部没有内容集 → v1，列原六种棋子（relic-redesign W1：旧十类信物的统计随旧信物删除）。
        Assert.Equal(ContentSets.PieceTypesOf(ContentSet.V1).Select(t => t.ToString()).Order(), s.Pieces.Select(p => p.Type).Order());
        PieceStat basic = s.Pieces.Single(p => p.Type == "Basic");
        Assert.Equal((4, 1), (basic.Offered, basic.Picked));
        Assert.Equal(0.25, basic.SelectionRate.Value);
        Assert.Equal((1, 1), (basic.WinRateOfPickers.Successes, basic.WinRateOfPickers.Trials));
        PieceStat fortress = s.Pieces.Single(p => p.Type == "Fortress");
        Assert.Equal((2, 1), (fortress.Offered, fortress.Picked));
        Assert.Equal((0, 1), (fortress.WinRateOfPickers.Successes, fortress.WinRateOfPickers.Trials));
        PieceStat multiplier = s.Pieces.Single(p => p.Type == "Multiplier");
        Assert.Equal((1, 0), (multiplier.Offered, multiplier.Picked));
        Assert.True(multiplier.WinRateOfPickers.IsEmpty);

        // restore-go-core-rules 段 E：被截断的局没有胜者，MUST NOT 计入胜率类指标——但它的征募仍计入选择率（选择率不是胜率）。
        // 截断局里 P2 选了 Basic：选择率 1/4 → 2/5，选过 Basic 的玩家胜率仍是 (1/1)；不排除的话会变成 (1/2)。
        // 变异验证 M-E4：Selection 的胜率样本不看截断（截断局也记"选过它的玩家"）→ 实跑红 1（本测试）。
        MatchLog cut = SimFixtures.Synthetic(
            2,
            [SimFixtures.Turn(1, 1, 2, [1, 1, 1, 1], ["A7:Basic"])],
            [SimFixtures.Recruit(1, 2, "Basic", "Basic")],
            SimFixtures.TruncatedResult(150));
        PieceStat basicWithCut = BalanceAnalyzer.Analyze([log, cut]).Selection.Pieces.Single(p => p.Type == "Basic");
        Assert.Equal((5, 2), (basicWithCut.Offered, basicWithCut.Picked));
        Assert.Equal((1, 1), (basicWithCut.WinRateOfPickers.Successes, basicWithCut.WinRateOfPickers.Trials));

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(SimFixtures.Sample.Value));
        Assert.All(ContentSets.PieceTypesOf(ContentSet.V1), type => Assert.Contains($"- 棋子 {type}：", text));
        Assert.DoesNotContain("- 棋子 Bannerman：", text);
        Assert.DoesNotContain("- 信物 ", text);

        // 批次里有 v2 对局：列十种棋子（未出现的也列出，0 次）。变异 MC-S1（列表不按内容集、恒取 v1）应红。
        MatchLog v2 = WithContentSet(log, ContentSet.V2);
        SelectionSection mixed = BalanceAnalyzer.Analyze([v2, cut]).Selection;
        Assert.Equal(Enum.GetNames<PieceType>().Order(), mixed.Pieces.Select(p => p.Type).Order());
        Assert.Equal(0, mixed.Pieces.Single(p => p.Type == "Sentry").Offered);
    }

    [Fact]
    public void 新来源占比()
    {
        // more-pieces-relics 规格 Scenario：读取一批内容集 v2 对局的分析报告 → 给出旗手、铁链、哨兵、界碑四种来源各自占全部位置加值的比例，
        // 以及经工坊扩展的改造次数与占比，即使某项为 0 也 MUST NOT 省略该行（本例界碑、犄角为 0）。第 12 项另给连营 / 犄角额外加值占比与驿站平均展示数加成。
        // 口径：终局快照（最后一条小回合快照）里参赛玩家的全部棋串；驿站加成按每条小回合快照（行动玩家本小回合的快照）平均；工坊按改造记录计。
        // 合成 v2 局：终局棋串 连珠 2 / 高地 1 / 旗手 3 / 铁链 2 / 哨兵 4 / 界碑 0 → 位置加值 12（relic-redesign W1：连营 / 犄角 / 驿站 / 工坊四行随旧信物删除）。
        // 被排除的样本（testing.md「统计口径测试必须放一个被排除的样本」）：一局 v1 对局（终局连珠 6）单列不适用，不进任何分母。
        // 变异 MC-A12a（v1 对局也计入）应红。
        GroupEntry[] final =
        [
            new() { Stones = ["A1", "A2", "A3"], Base = 3, LineBonus = 2, SynergyBonus = 0, HighGroundBonus = 1, BannerBonus = 3, ChainBonus = 2, SentryBonus = 4, BoundaryBonus = 0, MultiplierCount = 0, Power = 15 },
        ];
        TerrainEditEntry Edit(string target) => new() { Action = "Bridge", Target = target, Player = 0, Artisan = "E5" };
        TurnSnapshot first = SimFixtures.Turn(1, 1, 0, [15, 1, 1, 1], ["E5:Artisan"], edits: [Edit("B:E7"), Edit("B:D5")]);
        TurnSnapshot second = SimFixtures.Turn(2, 1, 1, [15, 1, 1, 1], ["H1:Basic"], groupsOfPlayer: null);
        second = second with { PlayersState = [.. second.PlayersState.Select(p => p.Player == 0 ? p with { Groups = [.. final] } : p)] };
        MatchLog v2 = WithContentSet(SimFixtures.Synthetic(1, [first, second], [], SimFixtures.ResultOf(1, [0])), ContentSet.V2);

        GroupEntry[] v1Final = [new() { Stones = ["B1", "B2", "B3"], Base = 3, LineBonus = 6, SynergyBonus = 0, HighGroundBonus = 0, MultiplierCount = 0, Power = 9 }];
        MatchLog v1 = SimFixtures.Synthetic(
            2,
            [SimFixtures.Turn(1, 1, 0, [9, 1, 1, 1], ["B1:Line"], groupsOfPlayer: v1Final, edits: [Edit("B:E4"), Edit("B:E6")])],
            [],
            SimFixtures.ResultOf(1, [0]));

        NewContentSection n = BalanceAnalyzer.Analyze([v2, v1]).NewContent;
        Assert.Equal((1, 1), (n.Matches, n.NotApplicable));
        Assert.Equal(12, n.FinalPositionBonus);
        Assert.Equal(
            ["旗手 3", "铁链 2", "哨兵 4", "界碑 0"],
            n.Sources.Select(x => $"{x.Name} {x.Bonus}"));
        Assert.Equal(0.25, n.Sources[0].Share, 10);

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([v2, v1]));
        Assert.Contains("### 12. 新棋子", text);
        Assert.Contains("- 内容集 v2 纳入 1 局；内容集 v1（含首部缺内容集的旧日志）1 局：本项不适用，不计入下列各项", text);
        Assert.Contains("  - 旗手：3（25.0%）", text);
        Assert.Contains("  - 铁链：2（16.7%）", text);
        Assert.Contains("  - 哨兵：4（33.3%）", text);
        Assert.Contains("  - 界碑：0（0.0%）", text);
        Assert.DoesNotContain("连营", text, StringComparison.Ordinal);
        Assert.DoesNotContain("驿站", text, StringComparison.Ordinal);

        // 只有 v1 对局：本项只单列"不适用"，不给出任何比例行。
        string v1Only = ReportWriter.Render(BalanceAnalyzer.Analyze([v1]));
        Assert.Contains("- 内容集 v2 纳入 0 局；内容集 v1（含首部缺内容集的旧日志）1 局：本项不适用，不计入下列各项", v1Only);
        Assert.DoesNotContain("  - 旗手：", v1Only);
    }

    [Fact]
    public void 内容集v1的报告除第12项外钉值()
    {
        // link-map-terrain 裁决 #8：原名「内容集v1的报告除第12项外与引入新内容之前逐字节相同」。改图后黄金值已是当前实现的自证值，故改名；
        // 钉值来源：当前实现在 link:1 共用样本（SimFixtures.Sample / RankedSample）上的报告，2026-10-14 link-map-terrain 第 2 组重钉。
        // 段 B 待决 6：分析器的棋子 / 信物列表按内容集展开，v1 样本的报告不因枚举追加四值而多出 0 行；
        // 新增的第 12 项对 v1 对局只单列"不适用"。黄金值取自引入新内容之前的提交（ad78d50，同一共用样本、去掉耗时字段后渲染）的 SHA-256：
        // 共用样本（4 局 Easy、写死 v1）146 行、名次样本 152 行。去掉第 12 项那一段后逐字节相同。
        static string Hash(IEnumerable<MatchLog> logs)
        {
            string report = ReportWriter.Render(BalanceAnalyzer.Analyze([.. logs.Select(l => MatchLog.Parse(l.DeterministicText()))]));
            string[] lines = report.Split('\n');
            int start = Array.FindIndex(lines, l => l.StartsWith("### 12. ", StringComparison.Ordinal));
            Assert.True(start > 0, "报告里没有第 12 项");
            int end = start;
            while (end < lines.Length && lines[end].TrimEnd('\r').Length > 0)
            {
                end++;
            }

            Assert.Contains(lines[start..end], l => l.Contains("本项不适用", StringComparison.Ordinal));
            string[] kept = [.. lines[..start].Concat(lines[end..])];

            // link-map-terrain 5.2：按主题统计是纯追加的一段（前面一个空行 + 标题 + 口径行 + 各主题行）。去掉它之后报告与追加之前逐字节相同，所以黄金值不重钉；
            // 这一段本身的内容由 按主题统计Tests 守。样本是带主题的 link:1 日志，这一段必须在（否则"去掉"是空操作）。
            int themes = Array.FindIndex(kept, l => l.StartsWith("## 按主题统计（link-map-terrain）", StringComparison.Ordinal));
            Assert.True(themes > 0 && kept[themes - 1].TrimEnd('\r').Length == 0, "报告里没有按主题统计一段");
            int themesEnd = themes;
            while (themesEnd < kept.Length && kept[themesEnd].TrimEnd('\r').Length > 0)
            {
                themesEnd++;
            }

            Assert.Equal(7, themesEnd - themes);   // 标题 + 口径行 + 五个主题各一行

            // ai-terrain-review 1.3：「主题表现」同样是纯追加在报告最末的一段（前面一个空行 + 标题 + 口径行 + 五个主题各一行）。去掉它之后报告与追加之前逐字节相同，
            // 黄金值不重钉；这一段本身的内容由 AI改造与主题诊断Tests.主题表现可查 守。样本带主题，这一段必须在（否则"去掉"是空操作）。
            string[] rest = [.. kept[..(themes - 1)].Concat(kept[themesEnd..])];
            int performance = Array.FindIndex(rest, l => l.StartsWith("## 主题表现（ai-terrain-review", StringComparison.Ordinal));
            Assert.True(performance > 0 && rest[performance - 1].TrimEnd('\r').Length == 0, "报告里没有主题表现一段");
            int performanceEnd = performance;
            while (performanceEnd < rest.Length && rest[performanceEnd].TrimEnd('\r').Length > 0)
            {
                performanceEnd++;
            }

            Assert.Equal(7, performanceEnd - performance);
            string stripped = string.Join('\n', rest[..(performance - 1)].Concat(rest[performanceEnd..]));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stripped)));
        }

        // retire-legacy-maps 1.8：两份共用样本由 v5 改到 link:1，黄金值在删除 v5 之前按当时实现重钉（原值 83302588…、19E3B957… 取自 ad78d50 的 v5 样本）。
        // 守的规则不变：v1 样本的报告不因内容枚举追加而多出行（第 12 项只单列"不适用"）；此后报告任何一行改动都会红。
        // link-map-terrain 第 2 组重钉（M10）：link:1 的地图内容变了（出生棋盘 6–8、主题地形、出生区为核心区、10–15 个信物格），同一配置的走法随之变；旧值 A4D48D1F732226F8… / 665851BBD2CE8ACF…。守的规则不变（第 12 项只单列"不适用"由上面的 Hash 内断言守）。
        string sample = Hash(SimFixtures.Sample.Value);
        string ranked = Hash(SimFixtures.RankedSample.Value);
        // open-map 1.5 重钉：link:1 改为 3+2 紧贴布局，样本走法随之变；旧值 8AF0C0CD430175146C459064F0BD0DFEFE6031C8B55B2D41076061466B8BF8D5 / B9C2D624CFB570D60F38A0A0EEA964BC8F2D98A876FB13861203F8A8FF237872。
        // open-map 段 B 重钉：报告删等待者 / 倒计时段、链接模式段改为同区率 + 各棋盘被选次数 + 对局大回合数、主题表现改整局胜率（报告格式变化，走法不变）；旧值 65989D85… / A2EA5792…。
        // cycle-stop 2.1 重钉：链接模式一段多一行"战场结算原因 …；上限结算 N 次"（报告格式变化，走法不变：样本 ≤ 4 个大回合，K = 6 不可能触发、上限 100 到不了）；旧值 8F6ED482… / 5CBE36F0…。
        // link-map-surfaces 2.2 重钉：link:1 中间带投放新地表，样本走法随之变；旧值 D9D33978… / 8FB438E5…。
        // link-map-surfaces 1.3 再重钉（F1 / F2）：第一次重钉值 9312D1D5… / 1552A610…。
        // terrain-barriers 3.1 重钉（地图变了：link:1 的河流 / 高台改为横穿棋盘的隔断线、大棋盘加湖泊 / 岩区，同一配置的走法随之变）；旧值 849F6632… / 23498075…。
        // terrain-density 2.2 重钉（地图变了：出生棋盘边长 6–8 → 6–7，link:1 重抽，同一配置的走法随之变）；旧值 AE582F0C… / EB76C9AC…。
        // map-expand-25 段 A2 重钉（地图变了：link:1:4 换成 44×44 浮空岛，同一配置的走法随之变）；旧值 D75715D6… / 12BEB16D…。
        // map-expand-25 段 A3 重钉（地图变了：不放静态信物格后布局流不再为信物消费随机，link:1:4 的坡道与低地重抽、可落子 1485 → 1484，平台落位 / 主题 / 平台内部不变）；旧值 A1282AA4… / F76C100E…。
        // map-expand-25 段 B 重钉（报告格式变化，走法不变）：链接模式一段的标题由"5 块出生棋盘紧贴成一张连通地图"改为"出生平台与其间的低地连成一座浮空岛"；
        // 实跑核对：把标题临时改回旧文案，旧值照常通过。旧值 6375D253… / A4D11329…。
        // relic-redesign W1 重钉（规则与报告格式都变了）：信物从第 3 大回合起刷新预告、第 4 大回合落位，样本里的走法随之变；报告删去旧信物各段（信物控制、收敛 / 未收敛分组、
        // 主题行的公共信物、地图规模的信物格数），第 12 项新内容来源改为四种新棋子。旧值 EB8CF108… / 45017B88…。
        // seat-gap-25 段 A 重钉（地图变了：link:1:4 改为四角出生座位岛 + 中立平台；盲选座位互斥，样本座次与走法随之变；首部棋盘只列座位，按主题统计段随之变）；旧值 EC001B28… / 088551F5…。
        // map-coastline 重钉（地图变了：裁决 5 同标识出新图——link:1:4 改为不规则岸线 + 湖泊 / 岩区 / 加宽河道，可落子 1927 → 1368，平台不变；AI 未改，样本走法随之变）；旧值 64330608… / 903E5CD5…。
        Assert.True("96090C6190D1D15EDE021C6C355C0B71F1BBC6D60D3ED1C0DB9BFBAD0F53F1B4" == sample, $"Sample 报告哈希：{sample}；RankedSample 报告哈希：{ranked}");
        Assert.True("EFD6EBA4D4AE84578C59C05BEA224AD543A74F362B4FD5C4C6D989ABB3D139FC" == ranked, $"RankedSample 报告哈希：{ranked}");
    }

    /// <summary>经文本往返复制一份日志并把首部内容集改为 <paramref name="set"/>。</summary>
    private static MatchLog WithContentSet(MatchLog log, ContentSet set)
    {
        MatchLog copy = MatchLog.Parse(log.FullText());
        return new MatchLog
        {
            Header = copy.Header with { Config = copy.Header.Config with { ContentSet = set } },
            Turns = copy.Turns,
            Events = copy.Events,
            Result = copy.Result,
        };
    }

    [Fact]
    public void 出生区公平性()
    {
        // 各出生区胜率 + 显著性（基线 1/4 落在 Wilson 区间之外即显著）（relic-redesign W1：原裁决 8 的"信物生成收敛 / 未收敛分组"随旧信物删除）。
        // 合成 40 局：出生区 0 的玩家全胜。
        // 出生区映射用 zones: [2, 1, 3, 0] 故意错位，zone 与玩家下标不同，抓得住"取 p 而非 Zones[p]"的错误。
        MatchLog[] logs = [.. Enumerable.Range(0, 40).Select(i => SimFixtures.Synthetic(
            (ulong)i + 1,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
            [],
            SimFixtures.ResultOf(6, [3]),
            zones: [2, 1, 3, 0]))];

        BirthZoneSection z = BalanceAnalyzer.Analyze(logs).BirthZones;

        Assert.Equal(0.25, z.Baseline);
        ZoneStat zone0 = z.All.Single(s => s.Zone == 0);
        Assert.Equal((40, 40), (zone0.WinRate.Successes, zone0.WinRate.Trials));
        Assert.True(zone0.Significant);
        ZoneStat zone2 = z.All.Single(s => s.Zone == 2);
        Assert.Equal((0, 40), (zone2.WinRate.Successes, zone2.WinRate.Trials));
        Assert.True(zone2.Significant);

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(logs));
        Assert.Contains("出生区 1：胜率 100.0% (40/40", text);
        Assert.Contains("，显著", text);
        Assert.DoesNotContain("收敛局", text, StringComparison.Ordinal);   // 原"信物生成收敛 / 未收敛"分组随旧信物删除

        // restore-go-core-rules 段 E：再混入 10 局截断局（出生区 2 的玩家"未胜"——截断局没有胜者）→ 各区胜率与样本数不变。
        // 变异验证 M-E4b：BirthZones 的输入改回全部纳入局 → 实跑红 1（本测试；出生区 0 的样本 40 → 50）。
        MatchLog[] withCut = [.. logs, .. Enumerable.Range(0, 10).Select(i => SimFixtures.Synthetic(
            (ulong)i + 200, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.TruncatedResult(150), zones: [2, 1, 3, 0]))];
        ZoneStat zone0WithCut = BalanceAnalyzer.Analyze(withCut).BirthZones.All.Single(s => s.Zone == 0);
        Assert.Equal((40, 40), (zone0WithCut.WinRate.Successes, zone0WithCut.WinRate.Trials));

        // 均衡样本不显著：4 局各区各胜一次
        MatchLog[] even = [.. Enumerable.Range(0, 4).Select(i => SimFixtures.Synthetic(
            (ulong)i + 100, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [i])))];
        Assert.All(BalanceAnalyzer.Analyze(even).BirthZones.All, s => Assert.False(s.Significant));
    }

    [Fact]
    public void 最小落子拖延检测()
    {
        // 信号 = 该小回合落子数为 1 且该玩家势力与上一快照相比无变化；报告给出占全部小回合的比例。
        // 合成 4 个小回合：#2 落 1 枚势力不变（信号）、#3 落 1 枚势力上升（否）、#4 落 2 枚势力不变（否）→ 1/4。
        // 变异验证 M-B10：Stalling 把 Placements.Count != 1 改成 < 1 → 红 1（本测试：#4 被误计，比例 2/4）。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 1, 0, [5, 0, 0, 0], ["A1:Basic"]),
                SimFixtures.Turn(2, 1, 0, [5, 0, 0, 0], ["A2:Basic"]),
                SimFixtures.Turn(3, 1, 0, [7, 0, 0, 0], ["A3:Basic"]),
                SimFixtures.Turn(4, 1, 0, [7, 0, 0, 0], ["B1:Basic", "B2:Basic"]),
            ],
            [],
            SimFixtures.ResultOf(1, [0]));

        StallingSection s = BalanceAnalyzer.Analyze([log]).Stalling;

        Assert.Equal((1, 4), (s.SignalTurns, s.TotalTurns));
        Assert.Equal(0.25, s.Ratio.Value);
        Assert.Contains("信号出现 1 次，占全部 4 个小回合的 25.0% (1/4", ReportWriter.Render(BalanceAnalyzer.Analyze([log])));
    }

    [Fact]
    public void 成长轴部署阈值按分阶段基础值()
    {
        // growth-pass-1：部署轴的"获取"= 部署上限高于该小回合所在大回合的分阶段基础值（3 / 4 / 5），而不是固定的 3——
        // 否则第 4 大回合起人人都会被误记为"获取了部署轴"。合成 P0：第 4 大回合 4、第 6 大回合 4、第 7 大回合 5 都只是基础值；第 8 大回合 6 才是军令带来的获取。
        // 变异验证 M-GP9：BalanceAnalyzer.AxisAcquisitionRounds 的阈值改为 BaseDeployLimitFor(1) → 全套红 1（本测试，部署轴记为第 4 大回合）。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 4, 0, [3, 0, 0, 0], ["A3:Basic"], deployLimit: 4),
                SimFixtures.Turn(2, 6, 0, [3, 0, 0, 0], ["A4:Basic"], deployLimit: 4),
                SimFixtures.Turn(3, 7, 0, [3, 0, 0, 0], ["A5:Basic"], deployLimit: 5),
                SimFixtures.Turn(4, 8, 0, [3, 0, 0, 0], ["A6:Basic"], deployLimit: 6),
            ],
            [],
            SimFixtures.ResultOf(8, [0]));

        Assert.Equal([null, 8, null, null], BalanceAnalyzer.AxisAcquisitionRounds(log, 0));
    }

    [Fact]
    public void 成长轴顺序分析()
    {
        // 四条成长轴（供给 / 部署 / 槽位 / 倍率）在胜局中的获取顺序分布。合成胜者 P0：第 1 大回合出现倍增串、第 2 大回合部署上限 4、第 3 大回合槽位 6，供给未获取。
        // 部署轴阈值若误为 >= BaseDeployLimitFor(大回合)，顺序会变成 部署>倍率>槽位，本测试的期望值钉住 > 。
        GroupEntry[] multiplierGroup = [new() { Stones = ["A1", "A2"], Base = 2, MultiplierCount = 1, Power = 3 }];
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 1, 0, [3, 0, 0, 0], ["A2:Multiplier"], groupsOfPlayer: multiplierGroup),
                SimFixtures.Turn(2, 2, 0, [3, 0, 0, 0], ["A3:Basic"], deployLimit: 4, groupsOfPlayer: multiplierGroup),
                SimFixtures.Turn(3, 3, 0, [3, 0, 0, 0], ["A4:Basic"], deployLimit: 4, typeSlots: 6, groupsOfPlayer: multiplierGroup),
                SimFixtures.Turn(4, 3, 1, [3, 1, 0, 0], ["H1:Basic"], deployLimit: 5, showCount: 6),
            ],
            [],
            SimFixtures.ResultOf(3, [0]));

        int?[] rounds = BalanceAnalyzer.AxisAcquisitionRounds(log, 0);
        Assert.Equal([null, 2, 3, 1], rounds);
        Assert.Equal("倍率>部署>槽位", BalanceAnalyzer.AxisSequence(rounds));
        Assert.Equal("供给=部署", BalanceAnalyzer.AxisSequence(BalanceAnalyzer.AxisAcquisitionRounds(log, 1)));
        Assert.Equal("无", BalanceAnalyzer.AxisSequence(BalanceAnalyzer.AxisAcquisitionRounds(log, 2)));

        GrowthAxisSection g = BalanceAnalyzer.Analyze([log]).GrowthAxes;
        Assert.Equal(1, g.WinnerSamples);
        Assert.Equal(1, g.SequenceCounts["倍率>部署>槽位"]);
        Assert.Equal(1, g.FirstAxisCounts["倍率"]);
        Assert.Equal("倍率>部署>槽位", g.DominantSequence);
        Assert.Contains("顺序 倍率>部署>槽位：1 次", ReportWriter.Render(BalanceAnalyzer.Analyze([log])));
    }

    [Fact]
    public void 终局原因分布()
    {
        // 规格 Scenario：200 局中 30 局只剩一名参赛玩家、150 局整轮 Pass、8 局棋盘填满、12 局被截断 →
        // 报告给出三类终局原因各自的占比与平均结束大回合，截断局单列。占比的分母是全部纳入局（200）。
        // 结束大回合按原因各取不同的值（5 / 8 / 12 / 150），把截断局混进任一类的均值都会改变它。
        // 变异验证 M-E14：Ending 把截断局计入整轮 Pass（按"未知原因归 AllPassed"）→ 实跑红 1（本测试）。
        static MatchLog Of(int seed, LogResult result) =>
            SimFixtures.Synthetic((ulong)seed, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], result);
        List<MatchLog> logs =
        [
            .. Enumerable.Range(0, 30).Select(i => Of(i + 1, SimFixtures.ResultOf(5, [0], reason: nameof(Match.EndReason.LastPlayerStanding)))),
            .. Enumerable.Range(0, 150).Select(i => Of(i + 100, SimFixtures.ResultOf(8, [i % 4]))),
            .. Enumerable.Range(0, 8).Select(i => Of(i + 300, SimFixtures.ResultOf(12, [1], reason: LogResult.LegacyBoardFull))),
            .. Enumerable.Range(0, 12).Select(i => Of(i + 400, SimFixtures.TruncatedResult(150))),
        ];

        EndingSection e = BalanceAnalyzer.Analyze(logs).Ending;

        Assert.Equal(
            [(nameof(Match.EndReason.LastPlayerStanding), 30, 0.15, 5.0), (LogResult.LegacyBoardFull, 8, 0.04, 12.0), (LogResult.LegacyAllPassed, 150, 0.75, 8.0)],
            e.Reasons.Select(r => (r.Reason, r.Count, Math.Round(r.Share, 10), r.MeanEndRound)));
        Assert.Equal((200, 12, 0, 150.0), (e.Matches, e.Truncated, e.Other, e.MeanTruncatedRound));

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze(logs));
        Assert.Contains("- 只剩一名参赛玩家（LastPlayerStanding）30 局（15.0%），平均结束大回合 5", text);
        Assert.Contains("- 棋盘填满（BoardFull）8 局（4.0%），平均结束大回合 12", text);
        Assert.Contains("- 整轮 Pass（AllPassed）150 局（75.0%），平均结束大回合 8", text);
        Assert.Contains("- 截断（turn_limit）12 局（6.0%）", text);

        // 某类一局都没有也照常给出（0 局、无样本），不省略该行。
        Assert.Contains("- 棋盘填满（BoardFull）0 局（0.0%），平均结束大回合 无样本", ReportWriter.Render(BalanceAnalyzer.Analyze([.. logs.Take(180)])));
    }

    [Fact]
    public void 领地分占比口径()
    {
        // 规格 Scenario：某局终局时四名参赛玩家总势力合计 600，其中领地分合计 180 → 该局领地分占比为 30%，报告给出全批次平均。
        // 口径：逐局取终局快照（最后一条小回合快照）中参赛玩家（Active）的领地分之和 ÷ 其总势力之和，再对纳入局取平均。
        // 被排除的样本（testing.md）：① 同一局里一名已弃赛玩家（领地 500 / 势力 500，不是参赛玩家）；
        // ② 一局旧日志（领地分字段缺失 → null）：整局排除并计数，MUST NOT 回填成 0。
        // 另一局 50%（领地 100 / 势力 200）→ 全批次平均 (30% + 50%) / 2 = 40%。
        // 变异验证 M-E6：领地占比的分母误用棋串军势（总势力 − 领地分）→ 实跑红 1；M-E6b：不过滤非参赛玩家 → 实跑红 1（均为本测试）。
        TurnSnapshot last = SimFixtures.Turn(9, 8, 0, [200, 150, 150, 100, 500], ["A1:Basic"], territory: [60, 45, 45, 30, 500]);
        last = last with { PlayersState = [.. last.PlayersState.Select(p => p.Player == 4 ? p with { Status = nameof(Scoring.PlayerStatus.Resigned) } : p)] };
        MatchLog thirty = SimFixtures.Synthetic(
            1,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1, 1], ["A1:Basic"], territory: [1, 1, 1, 1, 1]), last],
            [],
            SimFixtures.ResultOf(8, [0], players: 5),
            players: 5);
        MatchLog fifty = SimFixtures.Synthetic(
            2, [SimFixtures.Turn(1, 7, 0, [120, 80, 0, 0], ["A1:Basic"], territory: [60, 40, 0, 0])], [], SimFixtures.ResultOf(7, [0]));
        MatchLog legacy = SimFixtures.Synthetic(
            3, [SimFixtures.Turn(1, 7, 0, [90, 10, 0, 0], ["A1:Basic"])], [], SimFixtures.ResultOf(7, [0]));

        TerritoryShareSection single = BalanceAnalyzer.Analyze([thirty]).TerritoryShare;
        Assert.Equal((1, 0), (single.Matches, single.Skipped));
        Assert.Equal(0.30, single.MeanShare, 10);

        TerritoryShareSection batch = BalanceAnalyzer.Analyze([thirty, fifty, legacy]).TerritoryShare;
        Assert.Equal((2, 1), (batch.Matches, batch.Skipped));
        Assert.Equal(0.40, batch.MeanShare, 10);

        string text = ReportWriter.Render(BalanceAnalyzer.Analyze([thirty, fifty, legacy]));
        Assert.Contains("- 终局领地分占参赛玩家总势力：全批次平均 40.0%（纳入 2 局；排除缺领地分字段的旧日志 / 参赛玩家势力为 0 的局 1 局）", text);

        // 数值目标回归「势力值的成长曲线，分别给出领地分与棋串军势两条」：第 8 大回合参赛四人 总势力均值 (200+150+150+100)/4 = 150、
        // 领地分均值 (60+45+45+30)/4 = 45（弃赛者不计）→ 棋串军势 105；旧日志那一局领地分无样本，MUST NOT 回填成 0。
        // 变异验证 M-E17：成长曲线的领地分一律记 0 → 实跑红 1（本测试）。
        var round8 = BalanceAnalyzer.Analyze([thirty]).Targets.PowerCurve.Single(c => c.Round == 8);
        Assert.Equal((150.0, 45.0, 4), (round8.MeanPower, round8.MeanTerritory, round8.Samples));
        Assert.Contains("- 第 8 大回合结束：参赛玩家平均势力 150（其中领地分 45、棋串军势 105）", ReportWriter.Render(BalanceAnalyzer.Analyze([thirty])));
        Assert.True(double.IsNaN(BalanceAnalyzer.Analyze([legacy]).Targets.PowerCurve.Single().MeanTerritory));
        Assert.Contains("领地分无样本（旧日志）", ReportWriter.Render(BalanceAnalyzer.Analyze([legacy])));
    }
}
