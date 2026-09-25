using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Relics;

/// <summary>
/// 开局一次性信物生成（设计文档 §7.3 / §8.2）。信物格位置来自地图静态数据，随机只决定每格的类型与强度。
/// </summary>
/// <remarks>
/// <para><b>两阶段</b>（design.md D1）：第一阶段对每个信物格按其分区权重表独立抽类型，公共区再按档位升级概率判定高阶（裁决记录 1：六类统一）；
/// 第二阶段对各出生区做稀有度预算校正——总稀有度（含同区同类型惩罚，裁决记录 3）与均值偏差超过容差时，
/// 挑偏离最大的出生区里偏离最大的那一枚重抽，直到收敛或达到重试上限。</para>
/// <para><b>确定性</b>：全部随机只消费 <see cref="GameSeed.RelicGeneration"/> 子流，信物格按坐标字典序处理，并列按坐标打破。
/// 超出重试上限时放弃约束、保留权重抽取结果并打标未收敛（裁决记录 4），MUST NOT 死循环或抛错。</para>
/// <para>全部运算为整数：稀有度是权重倒数的定点数，容差比较用交叉相乘避免除法。</para>
/// </remarks>
public static class RelicGenerator
{
    /// <summary>
    /// 流派徽记可绑定的棋子类型 = 该对局内容集的全部棋子类型（<see cref="ContentSets.PieceTypesOf"/> 的显式清单：v1 原六种、v2 十种）。
    /// MUST NOT 用 <c>Enum.GetValues</c>：<see cref="PieceType"/> 末尾追加了四种新棋子，v1 若随之变成 10 选 1，同种子的信物分布会整体改变。
    /// </summary>
    private static ImmutableArray<PieceType> EmblemPiecesOf(ContentSet set) => ContentSets.PieceTypesOf(set);

    /// <summary>按默认参数生成。</summary>
    public static RelicGenerationRecord Generate(MapData map, GameSeed seed) => Generate(map, seed, RelicGenerationOptions.Default);

    /// <summary>按默认参数、指定对局内容集生成（more-pieces-relics D8）。</summary>
    public static RelicGenerationRecord Generate(MapData map, GameSeed seed, ContentSet contentSet) =>
        Generate(map, seed, RelicGenerationOptions.Default with { ContentSet = ContentSets.RequireValid(contentSet) });

    /// <summary>按指定参数生成。同一地图 + 同一种子 + 同一参数 → 逐格一致。</summary>
    public static RelicGenerationRecord Generate(MapData map, GameSeed seed, RelicGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        RandomStream stream = seed.Stream(GameSeed.RelicGeneration);
        Coord[] cells = [.. map.RelicCells.Keys.Order()];
        var placements = new RelicPlacement[cells.Length];

        // 第一阶段：按分区权重逐格抽取。
        for (int i = 0; i < cells.Length; i++)
        {
            RelicCellSpec spec = map.RelicCells[cells[i]];
            placements[i] = new RelicPlacement(cells[i], Draw(stream, spec, options), spec);
        }

        // 第二阶段：出生区预算校正。
        (bool converged, int rerolls) = BalanceBirthZones(map, placements, stream, options);

        return new RelicGenerationRecord(seed, map.Id, [.. placements], converged, rerolls) { ContentSet = options.ContentSet };
    }

    /// <summary>抽一枚信物：类型 → 升级判定 → 徽记绑定的棋子类型。三次消费的顺序固定，任何改动都会改变同种子的结果。</summary>
    /// <remarks>
    /// more-pieces-relics D7：升级判定只对有高阶版的类型（<see cref="RelicContent.HasAdvancedTier"/>，即原有六类）执行；
    /// 连营、犄角、驿站、工坊<b>不消费</b>升级抽签——消费顺序为「抽类型 → 仅原有六类做升级判定 → 仅流派徽记抽绑定类型」。
    /// v1 表里没有新四类，这条分支对 v1 不起作用，v1 的消费序列与改动前逐次相同。
    /// </remarks>
    private static RelicContent Draw(RandomStream stream, RelicCellSpec spec, RelicGenerationOptions options)
    {
        ContentSet set = options.ContentSet;
        RelicType type = RelicWeights.OrderOf(set)[stream.WeightedPick(RelicWeights.TableOf(spec.Zone, set))];
        int upgradePermille = spec.Zone == RelicZone.BirthZone || !RelicContent.HasAdvancedTier(type) ? 0 : options.UpgradePermilleOf(spec.Budget);
        int magnitude = upgradePermille > 0 && stream.NextPermille(upgradePermille) ? 2 : 1;
        ImmutableArray<PieceType> emblemPieces = EmblemPiecesOf(set);
        PieceType? emblemPiece = type == RelicType.SchoolEmblem ? emblemPieces[stream.NextInt(emblemPieces.Length)] : null;
        return new RelicContent(type, magnitude, emblemPiece);
    }

    private static (bool Converged, int Rerolls) BalanceBirthZones(
        MapData map, RelicPlacement[] placements, RandomStream stream, RelicGenerationOptions options)
    {
        // 出生区信物按所属出生区分组；出生区信物格不在任何出生区内是地图数据错误，响亮失败。
        var zones = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < placements.Length; i++)
        {
            if (placements[i].Spec.Zone != RelicZone.BirthZone)
            {
                continue;
            }

            int zone = map.BirthZoneOf(placements[i].Coord)
                ?? throw new SiegeRuleException($"信物格 {placements[i].Coord.ToNotation()} 标为出生区信物，却不在任何出生区内。");
            (zones.TryGetValue(zone, out List<int>? list) ? list : zones[zone] = []).Add(i);
        }

        if (zones.Count < 2)
        {
            return (true, 0);
        }

        int rerolls = 0;
        while (true)
        {
            if (IsBalanced(zones, placements, options, out int worstZone))
            {
                return (true, rerolls);
            }

            if (rerolls >= options.MaxRerolls)
            {
                // 裁决记录 4：放弃约束，保留权重抽取结果，打标未收敛。
                return (false, rerolls);
            }

            int victim = MostDeviatingCell(zones, worstZone, placements, options.ContentSet);
            RelicCellSpec spec = placements[victim].Spec;
            placements[victim] = placements[victim] with { Content = Draw(stream, spec, options) };
            rerolls++;
        }
    }

    /// <summary>
    /// 各出生区总稀有度与均值的偏差是否都在容差内；不在时给出偏差最大的出生区（并列取编号最小）。
    /// 同区同类型的惩罚（裁决记录 3）体现为<b>收窄该区的容差</b>：每多一枚同类型，允许偏差减少 <see cref="RelicGenerationOptions.SameTypePenaltyPermille"/>/1000 份。
    /// 惩罚若加在评分上只会平移评分、不一定增大偏差（双徽记 444 加罚后反而更接近均值），所以必须作用在容差侧。
    /// </summary>
    private static bool IsBalanced(
        SortedDictionary<int, List<int>> zones, RelicPlacement[] placements, RelicGenerationOptions options, out int worstZone)
    {
        int count = zones.Count;
        var scores = new Dictionary<int, (long Score, int Duplicates)>(count);
        long total = 0;
        foreach ((int zone, List<int> cells) in zones)
        {
            (long score, int duplicates) = ZoneScore(cells, placements, options.ContentSet);
            scores[zone] = (score, duplicates);
            total += score;
        }

        // |score − total/count| ≤ total/count × tol/1000 × (1000 − dup × pen)/1000
        //   ⇔ |score×count − total| × 1_000_000 ≤ total × tol × (1000 − dup × pen)
        bool balanced = true;
        long worstDeviation = -1;
        worstZone = -1;
        foreach ((int zone, (long score, int duplicates)) in scores)
        {
            long deviation = Math.Abs((score * count) - total);
            long allowedShare = Math.Max(0, 1000 - ((long)duplicates * options.SameTypePenaltyPermille));
            long allowed = total * options.RarityTolerancePermille * allowedShare;
            if (deviation * 1_000_000 > allowed)
            {
                balanced = false;
            }

            // 排序键把惩罚折算回偏差：被收窄的那部分容差视作额外偏差。
            long key = (deviation * 1_000_000) + (total * options.RarityTolerancePermille * (1000 - allowedShare));
            if (key > worstDeviation)
            {
                worstDeviation = key;
                worstZone = zone;
            }
        }

        return balanced;
    }

    /// <summary>出生区评分 = 稀有度之和；同时数出同类型重复枚数（每多一枚同类型计 1）。</summary>
    private static (long Score, int Duplicates) ZoneScore(List<int> cells, RelicPlacement[] placements, ContentSet set)
    {
        long score = 0;
        int duplicates = 0;
        var seen = new HashSet<RelicType>();
        foreach (int i in cells)
        {
            score += placements[i].RarityIn(set);
            if (!seen.Add(placements[i].Content.Type))
            {
                duplicates++;
            }
        }

        return (score, duplicates);
    }

    /// <summary>
    /// 最小改动原则：在偏差最大的出生区里挑稀有度离「单格目标」最远的那一枚。
    /// 单格目标 = 全部出生区信物的平均稀有度；同区重复类型的那一枚优先（它正是惩罚的来源）；并列按坐标字典序取最小。
    /// </summary>
    private static int MostDeviatingCell(SortedDictionary<int, List<int>> zones, int worstZone, RelicPlacement[] placements, ContentSet set)
    {
        long sum = 0;
        int n = 0;
        foreach (List<int> cells in zones.Values)
        {
            foreach (int i in cells)
            {
                sum += placements[i].RarityIn(set);
                n++;
            }
        }

        long target = sum / n;
        List<int> candidates = zones[worstZone];
        int best = -1;
        long bestKey = -1;
        var seen = new HashSet<RelicType>();
        foreach (int i in candidates)
        {
            bool duplicate = !seen.Add(placements[i].Content.Type);
            long key = Math.Abs(placements[i].RarityIn(set) - target) + (duplicate ? RelicWeights.RarityScaleOf(set) : 0);
            if (key > bestKey)
            {
                bestKey = key;
                best = i;
            }
        }

        return best;
    }
}
