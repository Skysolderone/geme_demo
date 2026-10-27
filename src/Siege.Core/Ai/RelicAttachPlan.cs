using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.PieceRelics;

namespace Siege.Core.Ai;

/// <summary>
/// AI 挂出带入件的落点规划（relic-redesign R21 / design.md D5，段 C1）：把尚未挂出的带入件挂到<b>本批势力增量最大的落点</b>。纯函数、零随机，
/// 由 <see cref="HeuristicTurnController"/> 在选定批次、复摆之后调用；势力一律经调用方给的 <paramref name="myPower"/>（评价器同一口径的唯一势力计算）。
/// </summary>
public static class RelicAttachPlan
{
    /// <summary>
    /// 对复摆后的预演盘面 <paramref name="projected"/>（本批棋子已落、提子已提、尚无挂出）规划挂出：逐（件 × 落点）Clone 一份、挂上一件同种信物、取
    /// <paramref name="myPower"/>（己方总势力）相对当前基准的增量，贪心取最大者；挂上后把该落点与该件移出候选、以挂上后的盘面为新基准再取下一对，
    /// 直到落点或件用尽（每子 1 件，R13；前几批就挂完）。增量为 0 也挂（未挂出的不生效，R14）。
    /// <para>并列打破（零随机）：增量相同 → 非保险件优先 → 清单下标小者优先 → 落点坐标字典序。非保险件优先的理由（R16 / R17）：保险件无论结局都回原主，
    /// 未挂出的非保险件在出局 / 弃赛时立即掉到地图——先把有丢失风险的件挂出去，让它尽早生效、也让掉落少一件。</para>
    /// <paramref name="cells"/> 应只含本批落点里<b>没有</b>地图信物的格（落子即拾取，不能再挂）；<paramref name="nextId"/> 是本局下一个信物编号（临时信物只在副本里出现）。
    /// 返回按挂出先后的（落点、清单下标）。
    /// </summary>
    public static ImmutableArray<(Coord Cell, int Index)> Plan(
        GameBoard projected,
        PlayerId me,
        IEnumerable<Coord> cells,
        IReadOnlyDictionary<int, CarriedRelic> carried,
        int nextId,
        Func<GameBoard, BigInteger> myPower)
    {
        ArgumentNullException.ThrowIfNull(projected);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(myPower);

        List<Coord> open = [.. cells.Distinct().Order()];
        List<KeyValuePair<int, CarriedRelic>> pending = [.. carried.OrderBy(kv => kv.Value.Insured).ThenBy(kv => kv.Key)];
        if (open.Count == 0 || pending.Count == 0)
        {
            return [];
        }

        GameBoard board = projected.Clone();
        BigInteger baseline = myPower(board);
        ImmutableArray<(Coord, int)>.Builder plan = ImmutableArray.CreateBuilder<(Coord, int)>();
        while (open.Count > 0 && pending.Count > 0)
        {
            KeyValuePair<int, CarriedRelic> bestItem = default;
            Coord bestCell = default;
            BigInteger? bestGain = null;
            foreach (KeyValuePair<int, CarriedRelic> item in pending)
            {
                foreach (Coord cell in open)
                {
                    GameBoard trial = board.Clone();
                    trial.AttachRelic(cell, RelicToken.Carried(nextId, item.Value.Kind, me, item.Value.Insured));
                    BigInteger gain = myPower(trial) - baseline;
                    // 严格大于才换：遍历顺序即并列打破顺序（非保险 → 下标 → 坐标）。
                    if (bestGain is null || gain > bestGain.Value)
                    {
                        (bestItem, bestCell, bestGain) = (item, cell, gain);
                    }
                }
            }

            plan.Add((bestCell, bestItem.Key));
            board.AttachRelic(bestCell, RelicToken.Carried(nextId++, bestItem.Value.Kind, me, bestItem.Value.Insured));
            baseline = myPower(board);
            open.Remove(bestCell);
            pending.Remove(bestItem);
        }

        return plan.ToImmutable();
    }
}
