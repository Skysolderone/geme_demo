using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Play;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 规格：openspec/specs/simulation-harness / match-telemetry（big-map D7）：<c>--map</c> 接受四段标识；大图的日志首部带档位、旗格与落座图摘要，回放先按旗格重做落座再比摘要；
/// 小图首部不多任何字段。只跑到构筑期（小回合数截断 3）——全图阶段的 AI 适配是段 C 的事。终端版在大图上按坐标自由插旗。
/// </summary>
/// <remarks>
/// 变异验证（段 A check 实跑，备份 → 改 → 跑本类 → 逐字节还原）：M-R1 <c>SeatedMap.Of</c> 对大图也原样返回基图 → 「大图首部带档位与旗格_回放摘要一致」红 1（回放在首部即报"地图摘要不一致"），其余绿。
/// </remarks>
public class 大图跑局与回放Tests
{
    [Fact]
    public void 大图终端按坐标插旗()
    {
        // big-map D7：终端版 play 在大图上没有座位清单，改读一个围棋记法坐标；不是坐标、不合规格各提示一次再重读；本机排在最前插旗、必得所选旗格。选定后进入第 1 大回合，再 q 退出。
        MapData map = MapCatalog.Resolve("link:7:4:l");
        Coord cell = FlagRules.LegalFlagCells(map)[0];
        var output = new StringWriter();
        MatchFlow? captured = null;
        int code = PlayCommand.Run(5, null, 2, AiDifficulty.Easy, new StringReader($"xx\nA1\n{cell.ToNotation()}\nq\n"), output, map, null, null, null, null, null, null, m => captured = m);

        Assert.Equal(0, code);
        string text = output.ToString();
        Assert.Contains("自由插旗", text, StringComparison.Ordinal);
        Assert.DoesNotContain("盲选出生座位", text, StringComparison.Ordinal);
        Assert.Contains("\"xx\" 不是坐标", text, StringComparison.Ordinal);
        Assert.Contains("A1 不是合规旗格", text, StringComparison.Ordinal);
        Assert.NotNull(captured);
        Assert.True(captured!.Flags.IsLocked);
        Assert.Equal(cell, captured.Flags.FlagCellOf(new PlayerId(1)));
        Assert.All(captured.Players.Where(p => p != new PlayerId(1)), p => Assert.True(FlagRules.Distance(captured.Flags.FlagCellOf(p)!.Value, cell) >= FlagRules.FlagGap));
    }

    [Fact]
    public void 大图首部带档位与旗格_回放摘要一致()
    {
        RunConfig config = SimFixtures.Config(turnLimit: 3, difficulty: AiDifficulty.Easy) with { MapId = "link:7:4:l" };
        MatchSession session = MatchSession.Create(config, 1);
        Assert.Equal(MapTier.Large, session.Match.Tier);
        Assert.Equal(MatchPhase.InProgress, session.Match.Phase);
        MatchLog log = session.Run();
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.Equal(3, log.Turns.Count);
        Assert.Equal("Large", log.Header.MapTier);
        Assert.Equal(4, log.Header.Flags!.Count);
        // relic-sites 裁决 6 / 8：首部另记遗址清单（格:档位，稀有在前），与落座图的清单一致。
        Assert.Equal(baseMapSites(), log.Header.RelicSites);
        Assert.Equal(8, log.Header.RelicSites!.Count);
        Assert.Equal(2, log.Header.RelicSites.Count(s => s.EndsWith(":Rare", StringComparison.Ordinal)));
        static List<string> baseMapSites() => [.. MapCatalog.Resolve("link:7:4:l").RelicSites.Select(s => $"{s.Cell.ToNotation()}:{s.Tier}")];
        Assert.Equal(4, log.Header.ZoneCount);
        Assert.Equal([0, 1, 2, 3], log.Header.Zones);
        Assert.Equal(60, log.Header.Config.RoundCap);
        Assert.Equal(session.Match.Board.BaseMap.PlayableCount, log.Header.PlayableCells);
        Assert.Equal(MapFile.Digest(session.Match.Board.BaseMap), log.Header.MapDigest);   // 落座图的摘要
        Assert.Equal(4, log.Header.Boards!.Count);
        Assert.All(log.Header.Boards, b => Assert.Equal((13, 13), (b.Width, b.Height)));
        Assert.All(log.Turns, t => Assert.True(t.ElapsedMs >= 0));

        // 按标识重建的是基图：摘要不同；按首部旗格重做落座后相同——Replayer 走的就是这条路，首部核对通过并逐小回合一致。
        MapData baseMap = MapCatalog.Resolve("link:7:4:l");
        Assert.NotEqual(log.Header.MapDigest, MapFile.Digest(baseMap));
        ReplayResult replay = Replayer.Replay(MatchLog.Parse(log.DeterministicText()));
        Assert.Null(replay.MapMismatch);
        Assert.True(replay.Identical, $"首个分歧行 {replay.FirstDivergentLine}：{replay.Expected} vs {replay.Actual}");
    }

    [Fact]
    public void 小图首部不多字段()
    {
        MatchLog log = MatchSession.Create(SimFixtures.Config(turnLimit: 1), 1).Run();
        Assert.Null(log.Header.MapTier);
        Assert.Null(log.Header.Flags);
        Assert.Null(log.Header.RelicSites);
        string header = log.DeterministicText().Split('\n')[0];
        Assert.DoesNotContain("MapTier", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Flags", header, StringComparison.Ordinal);
        Assert.DoesNotContain("RelicSites", header, StringComparison.Ordinal);
        Assert.Equal(100, log.Header.Config.RoundCap);
    }
}
