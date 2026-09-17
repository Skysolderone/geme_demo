using Siege.Core.Board;

namespace Siege.Core.Scoring;

/// <summary>
/// 三档据点分值（对局配置，scoring-sites D-A）。位置与档位是地图静态数据，分值放在这里，使同一张图可以扫多档分值。
/// </summary>
/// <remarks>
/// <para>标准局初值 5 / 15 / 45 是<b>待校准初值</b>（scoring-sites D-D / D-H），扫档拍板后才改。</para>
/// <para>合法性判定（正整数、营帐 ≤ 篝火 ≤ 石碑）只在 <see cref="Validated"/> 一处，对局配置与跑局配置都调它。</para>
/// <para>规格：openspec/changes/scoring-sites/specs/site-control —— Requirement: 据点档位与分值</para>
/// </remarks>
public sealed record SiteValues(int Tent, int Campfire, int Stele)
{
    /// <summary>标准局分值初值：营帐 5、篝火 15、石碑 45。</summary>
    public static readonly SiteValues Standard = new(5, 15, 45);

    /// <summary>某档位的分值。</summary>
    public int Of(SiteTier tier) => tier switch
    {
        SiteTier.Tent => Tent,
        SiteTier.Campfire => Campfire,
        SiteTier.Stele => Stele,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知据点档位。"),
    };

    /// <summary>
    /// 校验：三档均为正整数，且营帐 ≤ 篝火 ≤ 石碑。违规时抛 <see cref="ArgumentException"/>，报文指出违规的档位。返回自身便于链式调用。
    /// </summary>
    public SiteValues Validated()
    {
        if (Tent <= 0)
        {
            throw new ArgumentException($"据点分值非法：营帐分值须为正整数，实际 {Tent}。");
        }

        if (Campfire <= 0)
        {
            throw new ArgumentException($"据点分值非法：篝火分值须为正整数，实际 {Campfire}。");
        }

        if (Stele <= 0)
        {
            throw new ArgumentException($"据点分值非法：石碑分值须为正整数，实际 {Stele}。");
        }

        if (Tent > Campfire)
        {
            throw new ArgumentException($"据点分值非法：营帐分值 {Tent} 大于篝火分值 {Campfire}（须营帐 ≤ 篝火 ≤ 石碑）。");
        }

        if (Campfire > Stele)
        {
            throw new ArgumentException($"据点分值非法：篝火分值 {Campfire} 大于石碑分值 {Stele}（须营帐 ≤ 篝火 ≤ 石碑）。");
        }

        return this;
    }

    public override string ToString() => $"{Tent}/{Campfire}/{Stele}";
}
