using System.Collections.Immutable;

namespace Siege.Core.Scoring;

/// <summary>
/// 计分规则版本（formation-tiers D2，match-setup「计分规则版本」）：决定棋串军势是否计阵型。开局固定、始终公开，入存档、日志首部与批次配置。
/// </summary>
/// <remarks>
/// <para><see cref="V1"/> = 不计阵型：阵型阶数恒为 0，军势与引入阵型之前逐项相同；<see cref="V2"/> = 计阵型（见 <see cref="FormationTiers"/>）。</para>
/// <para>新局缺省 <see cref="V2"/>（<see cref="ScoringVersions.Default"/>）；恢复不含该字段的旧存档、回放不含该字段的旧日志一律按 <see cref="V1"/>
/// （<see cref="ScoringVersions.Legacy"/>）。与对局内容集（<see cref="Board.ContentSet"/>）相互独立，四种组合都合法。</para>
/// <para>显式编号 1 / 2：0 不是合法版本，未初始化的值会被 <see cref="ScoringVersions.RequireValid"/> 响亮拒绝。</para>
/// </remarks>
public enum ScoringVersion
{
    /// <summary>不计阵型（引入阵型之前的计分）。</summary>
    V1 = 1,

    /// <summary>计阵型：棋串棋子数达到 3 / 5 / 8 / 12 枚为一至四阶，阶数加进倍率指数。</summary>
    V2 = 2,
}

/// <summary>计分规则版本的缺省值、旧数据回填值与名称解析。</summary>
public static class ScoringVersions
{
    /// <summary>新开对局的缺省计分规则版本。</summary>
    public const ScoringVersion Default = ScoringVersion.V2;

    /// <summary>旧存档 / 旧日志缺该字段时的回填值：那些对局只可能是不计阵型的。不带版本的计分入口的语义也固定为它。</summary>
    public const ScoringVersion Legacy = ScoringVersion.V1;

    private static readonly string[] Names = Enum.GetNames<ScoringVersion>();

    /// <summary>可用名称，按枚举次序以 <c>|</c> 连接（用法说明与错误信息共用）。</summary>
    public static string Usage { get; } = string.Join("|", Names.Select(n => n.ToLowerInvariant()));

    /// <summary>版本须为已定义的值（v1 / v2）。</summary>
    public static ScoringVersion RequireValid(ScoringVersion version) =>
        Enum.IsDefined(version) ? version : throw new ArgumentOutOfRangeException(nameof(version), version, "未知计分规则版本。");

    /// <summary>按名称解析；不合法返回 <c>false</c>。只比对成员名本身（不区分大小写、不去空白），数字串一律不认。</summary>
    public static bool TryParse(string? text, out ScoringVersion version)
    {
        string? name = text is null ? null : Array.Find(Names, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
        version = name is null ? default : Enum.Parse<ScoringVersion>(name);
        return name is not null;
    }

    /// <summary>按名称解析；不合法抛 <see cref="ArgumentException"/>，消息列出可用名称——未识别的取值 MUST NOT 静默回退到缺省值。</summary>
    public static ScoringVersion Parse(string? text) =>
        TryParse(text, out ScoringVersion version)
            ? version
            : throw new ArgumentException($"未知计分规则版本 {(text is null ? "（空）" : $"\"{text}\"")}：只接受 {Usage}（不区分大小写）。");
}

/// <summary>
/// 阵型阶数（formation-tiers D1，power-score「棋串军势公式」）的<b>唯一</b>实现：阶数 = 棋串棋子数达到的门槛个数。
/// 门槛表 <see cref="Thresholds"/> 全仓只此一处；棋子数按整条棋串计，不分棋子类型。
/// </summary>
/// <remarks>
/// 阵型不是状态：每次势力重算按当前盘面的棋串现算（<see cref="PowerCalculator"/> 是唯一消费者），棋串因被提子或被立栅分裂而变小时立即降阶。
/// 表现层与遥测只读 <see cref="GroupPower.FormationTier"/>，不自己数棋子。
/// </remarks>
public static class FormationTiers
{
    /// <summary>各阶门槛（棋子数，升序）：达到第 i 个门槛即为 i + 1 阶；达到最后一个门槛后不再升阶（四阶封顶）。</summary>
    public static readonly ImmutableArray<int> Thresholds = [3, 5, 8, 12];

    /// <summary>最高阶数（= 门槛个数）。</summary>
    public static int MaxTier => Thresholds.Length;

    /// <summary>计阵型时 <paramref name="stoneCount"/> 枚棋子的棋串的阶数：1–2 枚 0 阶，3–4 枚一阶，5–7 枚二阶，8–11 枚三阶，12 枚及以上四阶。</summary>
    public static int TierOf(int stoneCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stoneCount);
        int tier = 0;
        foreach (int threshold in Thresholds)
        {
            if (stoneCount >= threshold)
            {
                tier++;
            }
        }

        return tier;
    }

    /// <summary>某计分规则版本下的阶数：<see cref="ScoringVersion.V1"/> 恒为 0，<see cref="ScoringVersion.V2"/> 按 <see cref="TierOf(int)"/>。</summary>
    public static int TierOf(ScoringVersion scoring, int stoneCount) => scoring switch
    {
        ScoringVersion.V1 => 0,
        ScoringVersion.V2 => TierOf(stoneCount),
        _ => throw new ArgumentOutOfRangeException(nameof(scoring), scoring, "未知计分规则版本。"),
    };
}
