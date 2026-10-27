using Siege.Core.Board;

namespace Siege.Core.Recruit;

/// <summary>
/// 小回合效果快照（设计文档 §5.1）：小回合开始时生成一次的<b>不可变值对象</b>，小回合内所有流程只读它。
/// </summary>
/// <remarks>
/// <para>relic-redesign D4：旧十类地图信物（探勘 / 征召 / 兵站 / 军令 / 徽记 / 驿站 / 工坊……）随旧信物系统删除，快照只剩默认基础值——
/// 展示数 5、选取数 3、类型槽 5、分阶段部署上限（<see cref="BaseDeployLimitFor"/>）。挂在棋子上的新信物只动计分与覆盖，不进快照。</para>
/// <para>仍保留快照这一层：手牌账本、批次上下文与弃赛留档都按"小回合开始时读一次"的口径消费它，测试也经调试接缝改写它。</para>
/// </remarks>
public sealed record EffectSnapshot
{
    /// <summary>默认基础值（设计文档 §5.2–§5.4）。</summary>
    public const int BaseRevealCount = 5;

    public const int BaseFreePickCount = 3;

    public const int BaseTypeSlots = 5;

    /// <summary>
    /// 分阶段基础部署上限（growth-pass-1 D1/D2，裁决 1）：第 1–3 大回合 3、第 4–6 大回合 4、第 7 大回合及以后 5。
    /// 阶段表全项目只在此定义一次；快照生成与遥测都经由本函数读取。
    /// </summary>
    /// <remarks>按<b>生成快照时</b>的大回合读取一次（裁决 2）：快照是不可变值对象，本小回合内部署上限不再随大回合变化。</remarks>
    public static int BaseDeployLimitFor(int majorRound) => majorRound switch
    {
        <= 3 => 3,
        <= 6 => 4,
        _ => 5,
    };

    public EffectSnapshot(PlayerId player, int majorRound, int revealCount, int freePickCount, int typeSlots, int deployLimit, int heldTypeCount)
    {
        if (heldTypeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heldTypeCount), heldTypeCount, "持有类型数不得为负。");
        }

        Player = player;
        MajorRound = majorRound;
        RevealCount = revealCount;
        FreePickCount = freePickCount;
        TypeSlots = typeSlots;
        DeployLimit = deployLimit;
        HeldTypeCount = heldTypeCount;
    }

    /// <summary>快照所属玩家。</summary>
    public PlayerId Player { get; }

    /// <summary>生成时所在的大回合。</summary>
    public int MajorRound { get; }

    /// <summary>征募展示数（默认 5）。</summary>
    public int RevealCount { get; }

    /// <summary>免费选取数（默认 3）。</summary>
    public int FreePickCount { get; }

    /// <summary>手牌类型槽（默认 5）。</summary>
    public int TypeSlots { get; }

    /// <summary>部署上限（分阶段基础 <see cref="BaseDeployLimitFor"/>）。</summary>
    public int DeployLimit { get; }

    /// <summary>快照生成时玩家持有的棋子类型数（由手牌层提供的输入，原样携带）。</summary>
    public int HeldTypeCount { get; }

    /// <summary>超限种数：持有类型数超出槽位的部分，0 即合法。大于 0 时征募流程在整类弃牌前不得进行。</summary>
    public int OverflowTypeCount => Math.Max(0, HeldTypeCount - TypeSlots);

    /// <summary>默认快照（relic-redesign 起即唯一的快照来源）。</summary>
    public static EffectSnapshot Defaults(PlayerId player, int majorRound, int heldTypeCount) =>
        new(player, majorRound, BaseRevealCount, BaseFreePickCount, BaseTypeSlots, BaseDeployLimitFor(majorRound), heldTypeCount);
}
