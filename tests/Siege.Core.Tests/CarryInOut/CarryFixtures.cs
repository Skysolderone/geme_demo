using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Carry;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>带入带出测试的公共夹具：开启带入带出的对局选项、手牌文本投影、纯函数用的弃赛快照与终局名次。</summary>
internal static class CarryFixtures
{
    internal static readonly CarryIn Spare = new(SupplyKind.SpareStone);

    internal static readonly CarryIn Draft = new(SupplyKind.DraftLot);

    internal static CarryIn Commission(PieceType type) => new(SupplyKind.Commission, type);

    /// <summary>开启带入带出、指定各玩家带入的立即插旗选项（内容集缺省 v2）。</summary>
    internal static MatchOptions On(params (int Player, CarryIn Carry)[] carries) => On(ContentSets.Default, carries);

    /// <summary>同上，指定内容集。</summary>
    internal static MatchOptions On(ContentSet contentSet, params (int Player, CarryIn Carry)[] carries) => MatchOptions.Immediate with
    {
        ContentSet = contentSet,
        CarryInOut = true,
        CarryIns = carries.ToImmutableSortedDictionary(c => new PlayerId(c.Player), c => c.Carry),
    };

    /// <summary>手牌的确定性文本：按类型次序 "类型×回合前基数+本轮新增"，如 "Basic×4+0 Fortress×1+0"。</summary>
    internal static string HandText(MatchFlow match, PlayerId player) =>
        string.Join(" ", match.Hands.Debug.PrivateViewOf(player).Entries.Select(kv => $"{kv.Key}×{kv.Value.Carried}+{kv.Value.Gained}"));

    /// <summary>带入的确定性文本：按玩家编号 "P1:DraftLot>Sentry"，无类型写 "-"。</summary>
    internal static string CarryText(IReadOnlyDictionary<PlayerId, CarryIn> carries) =>
        string.Join(" ", carries.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value.Kind}>{kv.Value.Type?.ToString() ?? "-"}"));

    /// <summary>纯函数用的弃赛快照：只有玩家、大回合与弃赛时名次有意义，其余字段取空值。</summary>
    internal static ResignationSnapshot Resignation(PlayerId player, int majorRound, int? rankAtResign) =>
        new(player, majorRound, string.Empty,
            new HandPrivateView(player, ImmutableSortedDictionary<PieceType, HandEntry>.Empty, TurnPhase.Idle, null),
            EffectSnapshot.Defaults(player, majorRound, 0), [], BigInteger.Zero, rankAtResign);

    /// <summary>纯函数用的名次输入：只给状态与势力（出局者给出局序号），其余并列链项取 0。</summary>
    internal static StandingInput Input(int player, PlayerStatus status, int power, int? eliminationOrder = null) =>
        new(new PlayerId(player), status, power, 0, 0, 0, eliminationOrder);

    /// <summary>按终局名次比较链（唯一实现）算出的整局结果。</summary>
    internal static MatchResult Result(params StandingInput[] inputs) =>
        new(EndReason.AllPassed, 9, FinalStandings.Compute(inputs));

    internal static readonly PlayerId[] Four = [new(0), new(1), new(2), new(3)];
}
