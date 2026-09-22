using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// map-generator tasks 2.4（design D5）：日志首部记地图内容摘要；回放重建地图后先比摘要，不同即在首部报"地图不一致"并停止；
/// 旧日志缺该字段跳过比对；内置图同样写摘要。规格：simulation-harness「各入口支持生成图」—— Scenario: 回放重建生成图 / 生成器变了。
/// </summary>
public class 日志首部地图摘要Tests
{
    private static readonly Lazy<MatchLog> Gen12345 = new(() =>
        MatchSession.Create(SimFixtures.Config(turnLimit: 8) with { MapId = "gen:12345" }, seed: 5).Run());

    [Fact]
    public void 真实跑局把开局地图的摘要写进首部并经文本往返_内置图同样写()
    {
        // 测试内独立算式：导出文本（行尾归一）UTF-8 的 SHA-256 大写十六进制。
        // 变异 MB-5：MatchSession.BuildHeader 不写 MapDigest → 本测试红。
        static string Expect(MapData map) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(MapFile.ToJson(map).Replace("\r\n", "\n", StringComparison.Ordinal))));

        MatchLog gen = Gen12345.Value;
        Assert.False(gen.IsFailed, gen.Failure?.Message);
        Assert.Equal(Expect(FrontierMapGenerator.Generate(12345)), gen.Header.MapDigest);
        Assert.Equal(gen.Header.MapDigest, MatchLog.Parse(gen.DeterministicText()).Header.MapDigest);
        Assert.Equal(gen.Header.MapDigest, MatchLog.Parse(gen.FullText()).Header.MapDigest);
        Assert.Contains($"\"MapDigest\":\"{gen.Header.MapDigest}\"", gen.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        Assert.All(SimFixtures.Sample.Value, l => Assert.Equal(Expect(FourPlayerBaseMap.Create()), l.Header.MapDigest));
        MatchLog frontier = MatchSession.Create(SimFixtures.Config(turnLimit: 4) with { MapId = FrontierMapV2.Id }, seed: 7).Run();
        Assert.Equal(Expect(FrontierMapV2.Create()), frontier.Header.MapDigest);
        Assert.NotEqual(gen.Header.MapDigest, frontier.Header.MapDigest);
    }

    [Fact]
    public void 回放生成图的日志_按标识重新生成地图_逐步一致()
    {
        // 规格 Scenario「回放重建生成图」：日志经文本往返后与内存里的地图对象彻底脱钩，回放只凭首部。
        MatchLog original = MatchLog.Parse(Gen12345.Value.FullText());

        ReplayResult replay = Replayer.Replay(original);

        Assert.True(replay.Identical, replay.ToString());
        Assert.Null(replay.MapMismatch);
        Assert.Equal("gen:12345", replay.Replayed.Header.MapId);
        Assert.Equal(original.Turns.Count, replay.Replayed.Turns.Count);
        Assert.True(original.Turns.Count > 4);
    }

    [Fact]
    public void 重建出的地图与首部摘要不同_在首部报地图不一致并停止_不重跑对局()
    {
        // 规格 Scenario「生成器变了」。两条腿：
        // ① 首部记录的摘要与现在按标识生成的图不同（模拟"这份日志是旧版生成器跑出来的"）；
        // ② 调用方给的地图被人改了一格（标识没变）。
        // 变异 MB-6：Replayer 去掉摘要比对 → 本测试红（①只在首部分歧但重跑了整局；②带着另一张图跑出中途分歧）。
        MatchLog original = MatchLog.Parse(Gen12345.Value.DeterministicText());
        string otherDigest = MapFile.Digest(FrontierMapGenerator.Generate(12346));
        var stale = new MatchLog { Header = original.Header with { MapDigest = otherDigest }, Turns = original.Turns, Events = original.Events, Result = original.Result };

        ReplayResult fromStale = Replayer.Replay(stale);

        Assert.False(fromStale.Identical);
        Assert.Equal(1, fromStale.FirstDivergentLine);
        Assert.NotNull(fromStale.MapMismatch);
        Assert.Contains("地图不一致", fromStale.ToString(), StringComparison.Ordinal);
        Assert.Contains("gen:12345", fromStale.MapMismatch, StringComparison.Ordinal);
        Assert.Contains(otherDigest, fromStale.MapMismatch, StringComparison.Ordinal);
        Assert.Contains(original.Header.MapDigest!, fromStale.MapMismatch, StringComparison.Ordinal);
        Assert.Empty(fromStale.Replayed.Turns);                       // 没有重跑
        Assert.Null(fromStale.Replayed.Result);
        Assert.Contains(otherDigest, fromStale.Expected, StringComparison.Ordinal);
        Assert.Contains(original.Header.MapDigest!, fromStale.Actual, StringComparison.Ordinal);

        // ② 改一格：把一个不是出生区 / 信物的岩石格挖掉（导出文本里把它从 Obstacles 移走），标识仍是 gen:12345。
        MapData map = FrontierMapGenerator.Generate(12345);
        MapData tampered = TamperOneCell(map);
        Assert.Equal(map.Id, tampered.Id);
        Assert.NotEqual(MapFile.Digest(map), MapFile.Digest(tampered));

        ReplayResult fromTampered = Replayer.Replay(original, tampered);

        Assert.False(fromTampered.Identical);
        Assert.Equal(1, fromTampered.FirstDivergentLine);
        Assert.Contains("地图不一致", fromTampered.ToString(), StringComparison.Ordinal);
        Assert.Empty(fromTampered.Replayed.Turns);

        // 对照：给的是同一张图 → 一致。
        Assert.True(Replayer.Replay(original, map).Identical);
    }

    [Fact]
    public void 旧日志缺摘要字段_跳过比对_只在首部分歧对局逐步相同()
    {
        // 与 ZoneCount、候选格上限同一口径：旧日志读入为 null、不抛、不回填；回放照常重跑，重建的首部多出摘要 → 第 1 行分歧，其余逐行相同。
        // 变异 MB-7：Replayer 把"首部没有摘要"当成不一致 → 本测试红（Replayed 没有小回合）。
        MatchLog current = SimFixtures.Sample.Value[0];
        string text = current.DeterministicText();
        string stripped = Regex.Replace(text, "\"MapDigest\":\"[0-9A-F]{64}\",", string.Empty);
        Assert.NotEqual(text, stripped);
        MatchLog old = MatchLog.Parse(stripped);
        Assert.Null(old.Header.MapDigest);

        ReplayResult replay = Replayer.Replay(old);

        Assert.Null(replay.MapMismatch);
        Assert.False(replay.Identical);
        Assert.Equal(1, replay.FirstDivergentLine);
        Assert.Equal(current.Header.MapDigest, replay.Replayed.Header.MapDigest);
        Assert.Equal(old.DeterministicText().Split('\n')[1..], replay.Replayed.DeterministicText().Split('\n')[1..]);

        // 对照：带摘要的新日志回放逐行一致。
        Assert.True(Replayer.Replay(MatchLog.Parse(text)).Identical);
    }

    [Fact]
    public void 标准批次的首部不多出换图相关字段()
    {
        // v4 / 内置图上首部只新增 MapDigest 一项：每局换图的开关为缺省值时不写出，平台边长只在换图批次里写。
        string header = SimFixtures.Sample.Value[0].DeterministicText().Split('\n')[0];
        Assert.DoesNotContain("MapPerMatch", header, StringComparison.Ordinal);
        Assert.DoesNotContain("ZoneSides", header, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPerMatch", SimFixtures.Config().Effective().ToJson(), StringComparison.Ordinal);
    }

    /// <summary>经导出文本改一格：从岩石表里拿掉按坐标序的第一个岩石格（它变成可落子的空地）。不过校验也无妨——这里只比摘要。</summary>
    private static MapData TamperOneCell(MapData map)
    {
        Coord victim = map.Obstacles.Order().First();
        string json = MapFile.ToJson(map);
        string pattern = $"\"{victim.ToNotation()}\",";
        int at = json.IndexOf(pattern, StringComparison.Ordinal);
        Assert.True(at > 0, $"导出文本里找不到岩石格 {victim.ToNotation()}。");
        return MapFile.FromJson(json.Remove(at, pattern.Length));
    }
}
