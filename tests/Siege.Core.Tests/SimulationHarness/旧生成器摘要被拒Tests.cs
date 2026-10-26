using System.Text.Json.Nodes;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// link-map-terrain 4.1（M10 / 任务 design §6「兼容与回滚」）：生成器带上主题化地形后，同一个 <c>link:1</c> 会重建出另一张图。
/// 旧生成器（纯平棋盘）产出的日志与存档必须靠现有的地图内容摘要比对被拒——回放报 <see cref="ReplayResult.MapMismatch"/>，存档恢复报"地图不一致"。
/// 规格：simulation-harness「各入口支持生成图」Scenario「生成器变了」；match-setup「对局配置公开完整地图标识」的存档摘要口径。
/// </summary>
/// <remarks>
/// 已有覆盖只用"另一张图的摘要"（<c>日志首部地图摘要Tests</c> 取 <c>link:12346</c>、<c>各入口支持生成图_链接模式Tests.生成器变了</c> 取全 0）
/// 与"改过一格的图"（<c>对局配置公开完整地图标识Tests</c>）模拟改版；本类两侧都用<b>旧生成器真实产出</b>的 <c>link:1</c> 摘要。
/// 变异验证见各测试注释。
/// </remarks>
public class 旧生成器摘要被拒Tests
{
    /// <summary>
    /// 旧生成器（link-map-terrain 之前，提交 7db59ad 时的纯平棋盘）下 <c>link:1</c> 的地图内容摘要。出处：<c>sim-out/ai-pass-review/diag/match-0000000000000001.jsonl</c>
    /// 首部（<c>"MapId":"link:1:4","PlayableCells":157,…,"MapDigest":"7A69…4223"</c>）。<c>sim-out/</c> 不入库，这里抄成常量，不读文件。
    /// </summary>
    private const string OldLink1Digest = "7A6996E5DA18C310E26686B2857550EC518A15E195D0328F326173C216A84223";

    /// <summary>同一份旧首部记的可落子格数（旧生成器边长 5–7、整块棋盘可落子）。</summary>
    private const int OldLink1PlayableCells = 157;

    [Fact]
    public void 旧生成器的link1日志首部_回放在首部报地图不一致且不重跑()
    {
        // 前提：生成器确实变了——旧摘要与现在按标识重建出的 link:1 摘要不同，否则下面的"被拒"恒真恒假都说明不了问题。
        MapData now = MapCatalog.Resolve("link:1:4");
        Assert.NotEqual(OldLink1Digest, MapFile.Digest(now));
        Assert.NotEqual(OldLink1PlayableCells, now.PlayableCount);

        // 用旧摘要构造一个 link:1 日志首部（其余照现在的真实跑局，经文本往返与内存对象脱钩）。
        // 变异 MLT-3（Replayer 首部摘要比对的条件前加 false &&）→ 红 1（本测试，过滤本类）：整局被重跑、MapMismatch 为 null。
        MatchLog current = MatchLog.Parse(SimFixtures.Sample.Value[0].FullText());
        Assert.Equal("link:1:4", current.Header.MapId);
        Assert.Equal(MapFile.Digest(now), current.Header.MapDigest);
        Assert.True(current.Turns.Count > 4, $"样本只有 {current.Turns.Count} 个小回合");   // 下界：确有可重跑的内容，"不重跑"才有意义
        MatchLog old = MatchLog.Parse(new MatchLog
        {
            Header = current.Header with { MapDigest = OldLink1Digest },
            Turns = current.Turns,
            Events = current.Events,
            Result = current.Result,
        }.FullText());
        Assert.Equal(OldLink1Digest, old.Header.MapDigest);

        ReplayResult replay = Replayer.Replay(old);

        Assert.False(replay.Identical);
        Assert.Equal(1, replay.FirstDivergentLine);
        Assert.NotNull(replay.MapMismatch);
        Assert.Contains("地图不一致", replay.ToString(), StringComparison.Ordinal);
        Assert.Contains("link:1:4", replay.MapMismatch, StringComparison.Ordinal);
        Assert.Contains(OldLink1Digest, replay.MapMismatch, StringComparison.Ordinal);
        Assert.Contains(MapFile.Digest(now), replay.MapMismatch, StringComparison.Ordinal);
        Assert.Empty(replay.Replayed.Turns);   // 在首部就停下，没有在新图上重跑旧决策
        Assert.Null(replay.Replayed.Result);

        // 对照：同一份日志带现在的摘要 → 逐步一致（被拒的只是摘要，不是这份日志本身坏了）。
        Assert.True(Replayer.Replay(current).Identical);
    }

    [Fact]
    public void 旧生成器的link1存档_恢复报地图不一致()
    {
        // 存档只存地图标识与内容摘要（不存整张图），恢复时按标识重建 link:1 再比摘要：旧生成器的存档里记的是旧摘要。
        // 变异 MLT-4（MatchFlow.Persistence.RequireMap 摘要比对的条件前加 false &&）→ 红 1（本测试，过滤本类）：旧存档在新图上被静默恢复。
        MapData now = MapCatalog.Resolve("link:1:4");
        Assert.NotEqual(OldLink1Digest, MapFile.Digest(now));

        MatchOptions options = MatchOptions.Immediate;
        MatchFlow match = MatchFlow.Create(now, new GameSeed(11), [new(0), new(1), new(2), new(3)], options);
        match.PlantPrototype();
        string json = match.Serialize();

        JsonNode node = JsonNode.Parse(json)!;
        Assert.Equal(MapFile.Digest(now), (string?)node["MapDigest"]);
        node["MapDigest"] = OldLink1Digest;
        string oldSave = node.ToJsonString();
        Assert.Equal("link:1:4", MatchFlow.SavedMapId(oldSave));

        FormatException mismatch = Assert.Throws<FormatException>(() => MatchFlow.Restore(MapCatalog.Resolve(MatchFlow.SavedMapId(oldSave)), oldSave));
        Assert.Contains("地图不一致", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains("link:1:4", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(OldLink1Digest, mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(MapFile.Digest(now), mismatch.Message, StringComparison.Ordinal);

        // 对照：未改摘要的同一份存档照常恢复。
        Assert.Equal(json, MatchFlow.Restore(now, json).Serialize());
    }
}
