using System.Collections.Immutable;
using System.Text;
using Siege.Core.Board;
using Siege.Core.Determinism;

namespace Siege.Core.Relics;

/// <summary>一个信物格的生成结果：坐标、内容、所属分区与预算档位。</summary>
public sealed record RelicPlacement(Coord Coord, RelicContent Content, RelicCellSpec Spec)
{
    /// <summary>该枚信物在内容集 <paramref name="set"/> 下的稀有度分（按其分区的权重表与该内容集的稀有度刻度）。</summary>
    public int RarityIn(ContentSet set) => RelicWeights.RarityOf(Spec.Zone, Content, set);

    public override string ToString() => $"{Coord.ToNotation()} {Spec.Zone}/{Spec.Budget} {Content}";
}

/// <summary>
/// 信物生成记录：种子 + 地图 + 全部分布 + 收敛标记（设计文档 §17 的离线平衡分析输入）。
/// 仅凭 <see cref="Seed"/> 与同一地图即可复现 <see cref="Placements"/>。
/// </summary>
public sealed record RelicGenerationRecord(
    GameSeed Seed,
    string MapId,
    ImmutableArray<RelicPlacement> Placements,
    bool Converged,
    int Rerolls)
{
    /// <summary>
    /// 生成时的对局内容集（more-pieces-relics D8）。生成器按 <see cref="RelicGenerationOptions.ContentSet"/> 写入；
    /// 测试手工构造的记录缺省为 <see cref="ContentSets.Default"/>。<see cref="Serialize"/> 不输出它（v1 的导出文本与改动前逐字节相同）。
    /// </summary>
    public ContentSet ContentSet { get; init; } = ContentSets.Default;

    /// <summary>某枚信物按本记录内容集计的稀有度分。</summary>
    public int RarityOf(RelicPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return placement.RarityIn(ContentSet);
    }

    /// <summary>值相等：逐格比较分布（<see cref="ImmutableArray{T}"/> 默认是引用相等，对记录没有意义）。</summary>
    public bool Equals(RelicGenerationRecord? other) =>
        other is not null
        && Seed == other.Seed
        && MapId == other.MapId
        && ContentSet == other.ContentSet
        && Converged == other.Converged
        && Rerolls == other.Rerolls
        && Placements.SequenceEqual(other.Placements);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Seed);
        hash.Add(MapId);
        hash.Add(ContentSet);
        hash.Add(Converged);
        hash.Add(Rerolls);
        foreach (RelicPlacement placement in Placements)
        {
            hash.Add(placement);
        }

        return hash.ToHashCode();
    }

    /// <summary>某格的生成结果；不是信物格时抛出。</summary>
    public RelicPlacement At(Coord coord) =>
        Placements.FirstOrDefault(p => p.Coord == coord)
        ?? throw new KeyNotFoundException($"{coord.ToNotation()} 不是信物格。");

    /// <summary>
    /// 可读的文本导出：首行为种子、地图、收敛标记与重抽次数，其后每行一枚信物（围棋记法坐标、分区、档位、内容）。
    /// 供遥测与人工复盘；离线复现只需要首行的种子与地图。
    /// </summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("seed=").Append(Seed).Append(";map=").Append(MapId)
          .Append(";converged=").Append(Converged ? "yes" : "no")
          .Append(";rerolls=").Append(Rerolls).Append('\n');
        foreach (RelicPlacement placement in Placements)
        {
            sb.Append(placement).Append('\n');
        }

        return sb.ToString();
    }
}
