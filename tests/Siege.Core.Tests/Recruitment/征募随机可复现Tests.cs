using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Tests.Determinism;

namespace Siege.Core.Tests.Recruitment;

/// <summary>规格：recruitment —— Requirement: 征募随机可复现</summary>
public class 征募随机可复现Tests
{
    /// <summary>一段固定的决策脚本：4 名玩家各 3 个小回合，选取 / 弃牌 / 落子 / Pass 全部由脚本决定，不看候选内容。</summary>
    private static HandLedger Play(GameSeed seed, int p1FirstTurnPicks = 2)
    {
        HandLedger ledger = HandFixtures.Ledger(seed);
        PlayerId[] order = [HandFixtures.P0, HandFixtures.P1, HandFixtures.P2, HandFixtures.P3];
        for (int round = 1; round <= 3; round++)
        {
            foreach (PlayerId player in order)
            {
                PlayerHandAccess access = HandFixtures.Begin(ledger, player, reveal: 5 + round % 2, slots: 7, round: round);
                if (round == 3 && player == HandFixtures.P2)
                {
                    access.Discard(PieceType.Basic);
                }

                RecruitPanelView panel = access.EnterRecruit();
                int picks = player == HandFixtures.P1 && round == 1 ? p1FirstTurnPicks : (player.Value + round) % 4;
                for (int i = 0; i < picks && i < panel.ShowCount; i++)
                {
                    access.Pick(i);
                }

                if ((player.Value + round) % 3 == 0)
                {
                    ledger.OnPass(player);
                }
                else
                {
                    HandPrivateView hand = access.PrivateView();
                    PieceType type = hand.Types.First();
                    ledger.DeductHand(player, HandFixtures.Deployed((type, 1)));
                }

                ledger.EndTurn(player);
            }
        }

        return ledger;
    }

    [Fact]
    public void 同种子同决策可复现()
    {
        // 设计文档 §17：同一对局种子 + 相同决策序列 → 每一轮的征募候选与最终手牌完全一致。
        // 变异验证 M-R20b：HandLedger 构造用 new GameSeed(静态自增计数器).Stream(...)（每局种子不同）→ 红 11，含本测试；
        // M-R20：用 Environment.TickCount64 → 红 3 但本测试不红（两局在同一毫秒内构造，种子相同）——所以用 M-R20b 作为本测试的守门变异。
        HandLedger a = Play(HandFixtures.Seed);
        HandLedger b = Play(HandFixtures.Seed);

        Assert.Equal(12, a.Records.Count);
        Assert.Equal(a.Records, b.Records);
        foreach (PlayerId player in a.Players)
        {
            Assert.Equal(a.Debug.PrivateViewOf(player), b.Debug.PrivateViewOf(player));
        }

        // 不同种子 → 候选序列不同（排除"恒定候选"的假复现）
        HandLedger c = Play(new GameSeed(HandFixtures.Seed.Value + 1));
        Assert.NotEqual(a.Records.Select(r => r.Candidates), c.Records.Select(r => r.Candidates));
    }

    [Fact]
    public void 征募记录完整()
    {
        // 设计文档 §17：每个小回合都可读出全部候选、被选取的候选与（若 Pass）被撤销的数量；落子数与征募数是"至少落 1 子规避 Pass"信号的原始量。
        // 变异验证 M-R21：OnPass 不写 RevokedCount → 红 3（本测试 + 同类型只扣回新增数量 + 小回合中途弃赛）；M-R22：EndTurn 不写记录 → 红 9。
        HandLedger ledger = Play(HandFixtures.Seed);

        Assert.Equal(12, ledger.Records.Count);
        Assert.Equal(Enumerable.Range(1, 12), ledger.Records.Select(r => r.Sequence));
        foreach (RecruitTurnRecord record in ledger.Records)
        {
            Assert.Equal(5 + record.MajorRound % 2, record.Candidates.Length);
            Assert.Equal((record.Player.Value + record.MajorRound) % 3 == 0, record.Passed);
            Assert.All(record.PickedIndices, i => Assert.InRange(i, 0, record.Candidates.Length - 1));
            Assert.Equal(record.PickedIndices.Length, record.PickedTypes.Length);
            if (record.Passed)
            {
                Assert.Equal(record.RecruitedCount, record.RevokedCount);
                Assert.Equal(0, record.DeployedCount);
            }
            else
            {
                Assert.Equal(0, record.RevokedCount);
                Assert.Equal(1, record.DeployedCount);
            }
        }

        // P1 第 1 回合：(1+1)%3 ≠ 0 → 落子；选 2 枚
        RecruitTurnRecord p1r1 = ledger.Records.Single(r => r.Player == HandFixtures.P1 && r.MajorRound == 1);
        Assert.Equal([0, 1], p1r1.PickedIndices);
        Assert.Equal(2, p1r1.RecruitedCount);
        // P0 第 3 回合：(0+3)%3 == 0 → Pass；选 (0+3)%4 = 3 枚，撤销 3
        RecruitTurnRecord p0r3 = ledger.Records.Single(r => r.Player == HandFixtures.P0 && r.MajorRound == 3);
        Assert.True(p0r3.Passed);
        Assert.Equal(3, p0r3.RevokedCount);
        // P2 第 3 回合弃掉普通子
        RecruitTurnRecord p2r3 = ledger.Records.Single(r => r.Player == HandFixtures.P2 && r.MajorRound == 3);
        Assert.Equal([PieceType.Basic], p2r3.Discarded);
        // 文本导出含全部字段
        string line = p0r3.ToString();
        Assert.Contains("candidates=", line);
        Assert.Contains("revoked=3", line);
        Assert.Contains("pass=yes", line);
    }

    [Fact]
    public void 改变征募决策后信物生成逐格不变()
    {
        // 规范强制回归（determinism.md）：改变某玩家的一次征募选择 → 信物生成结果逐格不变；且 recruit 子流消费次数确实不同。
        // 变异验证 M-R23：HandLedger 构造用 seed.Stream(GameSeed.RelicGeneration) → 红 3（征募只消费 recruit 子流 等），本测试不红——
        // 账本拿错子流名并不改变 RelicGenerator 自己派生的流；真正的共用序列是 M-D3（GameSeed.Stream 返回共享实例）→ 红 13，含本测试与 Determinism/随机子流隔离Tests。
        MapData map = FourPlayerBaseMap.Create();
        RelicGenerationRecord baseline = RelicGenerator.Generate(map, HandFixtures.Seed);

        HandLedger fewer = Play(HandFixtures.Seed, p1FirstTurnPicks: 0);
        RelicGenerationRecord afterFewer = RelicGenerator.Generate(map, HandFixtures.Seed);
        HandLedger more = Play(HandFixtures.Seed, p1FirstTurnPicks: 3);
        RelicGenerationRecord afterMore = RelicGenerator.Generate(map, HandFixtures.Seed);

        Assert.Equal(baseline.Placements, afterFewer.Placements);
        Assert.Equal(baseline.Placements, afterMore.Placements);
        Assert.NotEqual(fewer.Records[1].PickedIndices, more.Records[1].PickedIndices);
        // 选取决策不消费随机数：两条脚本的 recruit 消费次数相同，候选序列也逐轮相同
        Assert.Equal(fewer.RecruitStreamConsumed, more.RecruitStreamConsumed);
        Assert.Equal(fewer.Records.Select(r => r.Candidates), more.Records.Select(r => r.Candidates));
    }

    [Fact]
    public void 征募只消费recruit子流()
    {
        // 面板候选 == 对 GameSeed.Recruit 子流直接做 WeightedPick 的结果；用 relic-gen 或 setup 子流都对不上。
        // 变异验证 M-R23（见上）→ 红 3，含本测试；M-R24：EnterRecruit 用 Random.Shared.Next → 红 11，含本测试与源码扫描；M-R4：等概率 NextInt → 红 5，含本测试。
        // more-pieces-relics 段 A：期望序列用六档字面量表独立抽取 → 写死内容集 v1（v1 棋池与引入新棋子之前相同）。
        HandLedger ledger = HandFixtures.Ledger(ContentSet.V1);
        PlayerHandAccess access = HandFixtures.Begin(ledger, HandFixtures.P0, reveal: 8);
        RecruitPanelView panel = access.EnterRecruit();

        int[] table = [160, 80, 72, 48, 40, 40];
        PieceType[] Direct(string name)
        {
            RandomStream s = HandFixtures.Seed.Stream(name);
            return [.. Enumerable.Range(0, 8).Select(_ => RecruitWeights.Order[s.WeightedPick(table)])];
        }

        Assert.Equal(Direct(GameSeed.Recruit), panel.CandidateTypes);
        Assert.NotEqual(Direct(GameSeed.RelicGeneration), panel.CandidateTypes);
        Assert.Equal(ConsumedBy(8), ledger.RecruitStreamConsumed);

        static long ConsumedBy(int picks)
        {
            RandomStream s = HandFixtures.Seed.Stream(GameSeed.Recruit);
            for (int i = 0; i < picks; i++)
            {
                s.WeightedPick([160, 80, 72, 48, 40, 40]);
            }

            return s.Consumed;
        }
    }

    [Fact]
    public void 征募路径不含浮点与非受控随机()
    {
        // 规范「禁止浮点」与「并列必须确定性打破」：Recruit/ 源码不得出现 double / float / decimal / Math.Round / System.Random / Random.Shared。
        // 变异验证 M-R25：在 HandLedger 加一行 `double _ = 0.75;` → 红 1（本测试）。
        string dir = Path.Combine(随机子流隔离Tests.SourceRoot(), "src", "Siege.Core", "Recruit");
        string[] files = Directory.GetFiles(dir, "*.cs");
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string token in new[] { "double", "float", "decimal", "Math.Round", "Math.Floor", "Math.Ceiling", "System.Random", "Random.Shared", "new Random" })
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} 含 {token}");
            }
        }
    }
}
