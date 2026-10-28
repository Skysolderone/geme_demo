using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Match;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.MatchSetup;

/// <summary>
/// map-expand-25 D1（段 B）：解除"链接模式只有 4 人局"的硬锁后，2 人 / 3 人的浮空岛整局流程跑通——插旗 → 构筑期（第 1–3 大回合只在自己的平台）→
/// 第 4 大回合起低地可落 → 唯一战场整轮 Pass 结算即终局。脚本落子（夹具的 <see cref="LinkModeFixtures.PlayRound"/>），不挂 AI。
/// </summary>
/// <remarks>
/// 变异验证（段 B 实跑，只跑本类）：MB-1 <c>MatchFlow.RequireLinkMatch</c> 的判据改回 <c>players != 4</c> → 本类红 2（两例都在建局时抛出）。
/// </remarks>
public class 按人数开局Tests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void 少于四人的局整局可以开局并推进(int players)
    {
        MapData map = MapCatalog.Resolve(LinkMapId.Format(1, players));
        PlayerId[] seats = [.. All.Take(players)];
        Assert.Equal(players, map.MaxPlayers);   // 样本口径：标识的人数即地图人数
        Assert.Equal(players, map.BirthZones.Length);   // seat-gap-25：恰 n 个出生座位（map-expand-25 时为"平台数多于人数"）

        MatchFlow match = MatchFlow.Create(map, TestMaps.Seed, seats, Options);
        foreach (PlayerId p in seats)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(seats.Select((p, i) => (p, i)));
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(1, match.MajorRound);
        Assert.Equal(players, match.ActionOrder.Length);

        // 构筑期：每名玩家的合法范围非空且都在自己的平台内；全员各落一子推进。
        for (int round = 1; round <= PublicRules.BuildProtectionRounds; round++)
        {
            Assert.Equal(round, match.MajorRound);
            for (int i = 0; i < players; i++)
            {
                IReadOnlySet<Coord> range = match.LegalRangeFor(seats[i]);
                Assert.NotEmpty(range);
                Assert.All(range, c => Assert.True(map.BirthBoard(i).Contains(c), $"第 {round} 大回合 {seats[i]} 的范围含平台外的 {c.ToNotation()}"));
            }

            match.PlayRound(seats);
        }

        // 第 4 大回合：范围越出平台、含低地（不在任何平台外接矩形里的可落子格）。
        Assert.Equal(PublicRules.BuildProtectionRounds + 1, match.MajorRound);
        IReadOnlySet<Coord> open = match.LegalRangeFor(seats[0]);
        Assert.Contains(open, c => !map.Boards.Any(b => b.Contains(c)));
        Assert.Equal(players * PublicRules.BuildProtectionRounds, seats.Sum(match.StoneCount));   // 构筑期的子都在盘上（没有被提）

        // 终局：唯一战场整轮 Pass → 最后战场结算，名次覆盖全部玩家。
        match.PassRound();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.Equal(players, match.Result.Standings.Length);
    }
}
