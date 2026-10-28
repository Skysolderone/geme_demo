using System.Collections.Immutable;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 挂在棋子上的信物种类（relic-redesign R12，design.md D3）。首批五种，只动计分与覆盖，不改气与提子。
/// 旧十类地图信物（<c>Siege.Core.Relics.RelicType</c>）已在接线段 W1 删除。
/// </summary>
/// <remarks>末尾追加式枚举：档案 v2 的库存与盘面序列化按名字 / 类型码写出，既有值的编号与名字 MUST NOT 改动。</remarks>
public enum RelicKind
{
    /// <summary>战鼓（普通）：持物棋子基础军势 +3。</summary>
    WarDrum,

    /// <summary>号令（普通）：所在棋串位置加值 + ⌊棋串棋子数 / 2⌋。</summary>
    Command,

    /// <summary>望台（普通）：持物棋子按岩台规则覆盖（四方向直线距离 2）。</summary>
    Watchtower,

    /// <summary>百变（普通）：所在棋串位置加值 +2；该串协同子计"其他类型种数"时额外 +1 种。</summary>
    Chameleon,

    /// <summary>军旗（稀有）：所在棋串倍率指数 +1（×1.5），可叠加。</summary>
    Banner,
}

/// <summary>信物档位（R12 / R18）：普通可用补给点兑换，稀有只能局内带出。</summary>
public enum RelicTier
{
    /// <summary>普通。</summary>
    Common,

    /// <summary>稀有。</summary>
    Rare,
}

/// <summary>
/// 信物种类的档位、显示名与全部数值常量（design.md D3 / D2 / D7）。
/// 名字带 <c>Uncalibrated</c> 的常量都是<b>未校准的初值</b>（prd.md「不在范围：效果数值校准与扫档」），不是基线，别在上面盖楼。
/// </summary>
public static class RelicKinds
{
    /// <summary>五种信物的固定次序（战鼓、号令、望台、百变、军旗）：档案写出、界面列出都按这个次序。</summary>
    public static readonly ImmutableArray<RelicKind> Order =
        [RelicKind.WarDrum, RelicKind.Command, RelicKind.Watchtower, RelicKind.Chameleon, RelicKind.Banner];

    /// <summary>普通四种（刷新时等权抽取，R12）。</summary>
    public static readonly ImmutableArray<RelicKind> Common =
        [RelicKind.WarDrum, RelicKind.Command, RelicKind.Watchtower, RelicKind.Chameleon];

    /// <summary>稀有档（首批只有军旗）。</summary>
    public static readonly ImmutableArray<RelicKind> Rare = [RelicKind.Banner];

    // ---- 效果（R12，未校准）----

    /// <summary>战鼓：持物棋子基础军势加值，<b>未校准的初值 3</b>。</summary>
    public const int UncalibratedWarDrumBase = 3;

    /// <summary>号令：位置加值 = ⌊棋串棋子数 / 本常量⌋（每件），<b>未校准的初值 2</b>。</summary>
    public const int UncalibratedCommandDivisor = 2;

    /// <summary>百变：所在棋串位置加值（每件），<b>未校准的初值 2</b>。</summary>
    public const int UncalibratedChameleonPosition = 2;

    /// <summary>百变：该串协同子计"除协同子外的类型种数"时额外加的种数（每件），<b>未校准的初值 1</b>。</summary>
    public const int UncalibratedChameleonExtraTypes = 1;

    /// <summary>军旗：所在棋串倍率指数加值（每件），<b>未校准的初值 1</b>（×1.5）。</summary>
    public const int UncalibratedBannerExponent = 1;

    // ---- 刷新（R8–R11，未校准）----

    /// <summary>刷新普通档的整数权重，<b>未校准的初值 85</b>（与稀有档 15 相对）。</summary>
    public const int UncalibratedCommonWeight = 85;

    /// <summary>刷新稀有档的整数权重，<b>未校准的初值 15</b>。</summary>
    public const int UncalibratedRareWeight = 15;

    /// <summary>第一批落位的大回合，<b>未校准的初值 4</b>（R8：开局不刷，第 4 大回合起）。</summary>
    public const int UncalibratedFirstLandingRound = 4;

    /// <summary>两批落位之间相隔的大回合数，<b>未校准的初值 2</b>（R8）。</summary>
    public const int UncalibratedLandingInterval = 2;

    /// <summary>预告比落位提前的大回合数，<b>未校准的初值 1</b>（R11）。</summary>
    public const int UncalibratedForecastLead = 1;

    /// <summary>刷新格与任一棋子的最小切比雪夫距离，<b>未校准的初值 2</b>（R10：离任何棋子 ≥ 2 格）。</summary>
    public const int UncalibratedMinStoneDistance = 2;

    /// <summary>场上未拾取地图信物上限 = 开局人数 + 本常量，<b>未校准的初值 2</b>（R9：n + 2）。</summary>
    public const int UncalibratedCapOverPlayers = 2;

    /// <summary>每批件数 = ⌈开局人数 / 本常量⌉，<b>未校准的初值 2</b>（R9：⌈n / 2⌉）。</summary>
    public const int UncalibratedBatchDivisor = 2;

    // ---- 带入与档案（R14 / R15 / R18）----

    /// <summary>每局至多带入的件数（用户原话"最多 5 个"，规则值）。</summary>
    public const int MaxCarried = 5;

    /// <summary>带入件中至多勾选为保险的件数（用户原话"两个是保险"，规则值）。</summary>
    public const int MaxInsured = 2;

    /// <summary>普通信物的兑换价格（补给点 / 件），<b>未校准的初值 8</b>（R18）。</summary>
    public const int UncalibratedExchangePrice = 8;

    /// <summary>新档案赠送的普通信物，按固定次序战鼓 / 号令 / 望台各 1（design.md D7：不随机，便于说明）。件数 3 是<b>未校准的初值</b>。</summary>
    public static readonly ImmutableArray<RelicKind> UncalibratedStarterGift = [RelicKind.WarDrum, RelicKind.Command, RelicKind.Watchtower];

    /// <summary>某种信物的档位。</summary>
    public static RelicTier TierOf(RelicKind kind) => kind switch
    {
        RelicKind.WarDrum or RelicKind.Command or RelicKind.Watchtower or RelicKind.Chameleon => RelicTier.Common,
        RelicKind.Banner => RelicTier.Rare,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知信物种类。"),
    };

    /// <summary>中文显示名（错误信息、终端输出共用的唯一映射）。</summary>
    public static string DisplayName(this RelicKind kind) => kind switch
    {
        RelicKind.WarDrum => "战鼓",
        RelicKind.Command => "号令",
        RelicKind.Watchtower => "望台",
        RelicKind.Chameleon => "百变",
        RelicKind.Banner => "军旗",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知信物种类。"),
    };

    /// <summary>
    /// 盘面序列化信物段的类型码（<c>GameBoard.Serialize</c> 的唯一映射，反向在 <see cref="KindFromCode"/>）：
    /// 战鼓 D、号令 O、望台 W、百变 V、军旗 G。只在信物段里出现，与棋子类型码不在同一段、互不混淆。
    /// </summary>
    internal static char CodeOf(RelicKind kind) => kind switch
    {
        RelicKind.WarDrum => 'D',
        RelicKind.Command => 'O',
        RelicKind.Watchtower => 'W',
        RelicKind.Chameleon => 'V',
        RelicKind.Banner => 'G',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知信物种类。"),
    };

    /// <summary><see cref="CodeOf"/> 的反向实现；未知类型码抛 <see cref="FormatException"/>。</summary>
    internal static RelicKind KindFromCode(char code) => code switch
    {
        'D' => RelicKind.WarDrum,
        'O' => RelicKind.Command,
        'W' => RelicKind.Watchtower,
        'V' => RelicKind.Chameleon,
        'G' => RelicKind.Banner,
        _ => throw new FormatException($"未知信物类型码：'{code}'。"),
    };
}
