using System.Collections.Concurrent;
using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>一条链接通道：左下角格、宽、高（直条矩形；横向通道高 = 通道宽 3–4，纵向通道宽 = 通道宽）。</summary>
public readonly record struct LinkCorridor(Coord Origin, int Width, int Height)
{
    /// <summary>通道格，先行后列。</summary>
    public IEnumerable<Coord> Cells()
    {
        for (int y = Origin.Y; y < Origin.Y + Height; y++)
        {
            for (int x = Origin.X; x < Origin.X + Width; x++)
            {
                yield return new Coord(x, y);
            }
        }
    }

    /// <summary>通道长度（沿通道方向的格数）。</summary>
    public int Length => Math.Max(Width, Height);

    public override string ToString() =>
        $"{Origin.ToNotation()}–{new Coord(Origin.X + Width - 1, Origin.Y + Height - 1).ToNotation()}（{Width}×{Height}）";
}

/// <summary>
/// 一次链接的结果（board-link-mode D2 / 规格 battlefield「链接事件」）：新地图（版本号 +1）、新增的公共棋盘（退化链接为 <c>null</c>）、新增的通道、
/// 是否退化、实际相连的两块棋盘在清单里的下标、以及一句供日志记录的几何摘要。信物<b>内容</b>不在这里生成——那是对局层按对局种子派生的子流（段 B）。
/// </summary>
public sealed record LinkResult(
    MapData Map,
    BoardPlate? PublicBoard,
    ImmutableArray<LinkCorridor> Corridors,
    bool Degraded,
    int FromBoard,
    int ToBoard,
    bool ClosestPair,
    string Summary);

/// <summary>一次链接档初始布局生成的结果：地图、实际采用的尝试序号（只供诊断）、作废尝试的原因、以及生成期对全部链接序列的核对统计。</summary>
public sealed record GeneratedLinkMap(MapData Map, int Attempt, ImmutableArray<string> Discarded, LinkVerification Verification);

/// <summary>生成期链接核对的统计：三类序列各执行了多少次链接、其中退化（无公共棋盘）与备选（不是最近两块棋盘）各多少。</summary>
public sealed record LinkVerification(
    int FourZoneLinks, int FourZoneDegraded, int FourZoneAlternate,
    int TwoPlusLinks, int TwoPlusDegraded, int TwoPlusAlternate,
    int ThreePlusLinks, int ThreePlusDegraded, int ThreePlusAlternate)
{
    public int Links => FourZoneLinks + TwoPlusLinks + ThreePlusLinks;

    public int Degraded => FourZoneDegraded + TwoPlusDegraded + ThreePlusDegraded;
}

/// <summary>链接序列的类别（规格 link-map-generation「全部链接序列都能生成」的三类）。</summary>
public enum LinkSequenceKind
{
    /// <summary>四人四区：首轮配对两次链接，再决赛链接（公共 ↔ 公共）。</summary>
    FourZones,

    /// <summary>2+1+1：两名独占者先配对链接，胜者再与同区棋盘决赛（出生棋盘 ↔ 合并战场）。</summary>
    TwoPlusOnePlusOne,

    /// <summary>3+1：独占者直接与三人同区棋盘的胜者决赛（出生棋盘 ↔ 出生棋盘）。</summary>
    ThreePlusOne,
}

/// <summary>一条链接序列：类别与依次执行的若干次 (X, Y) 链接。战场标识即当时分量内最小的出生棋盘编号。</summary>
public sealed record LinkSequence(LinkSequenceKind Kind, ImmutableArray<(int X, int Y)> Steps)
{
    public override string ToString() => $"{Kind}：{string.Join(" → ", Steps.Select(s => $"({s.X},{s.Y})"))}";
}

/// <summary>
/// 链接档地图生成器（board-link-mode D2、实现裁决 A1–A3；规格 link-map-generation）。两个入口：
/// <see cref="Generate(ulong, int)"/> 由地图种子生成<b>初始布局</b>（5 块隔绝的出生棋盘）；
/// <see cref="Link(MapData, int, int, ulong)"/> 在当前物化状态上执行一次<b>链接</b>（公共棋盘 + 两条通道，放不下即退化为直通道），产出版本号 +1 的新地图。
/// 全程整数运算，随机只来自 <see cref="MapRandom.ForLinkAttempt"/> / <see cref="MapRandom.ForLink"/>，不读时钟与环境，也拿不到对局种子。
/// </summary>
/// <remarks>
/// <para><b>画布与槽位</b>：画布固定为 <see cref="CanvasSide"/>×<see cref="CanvasSide"/>（整局不变，占用数组与盘面序列化的行宽因此不受链接影响，A2），
/// 由 <see cref="Lanes"/>×<see cref="Lanes"/> 个槽位组成，槽位间距 <see cref="LanePitch"/> = 出生棋盘最大边长 + 预留间距
/// （<see cref="ReserveGap"/> = 一块 11×11 公共棋盘 + 两条最短通道），于是任意两块出生棋盘之间都放得下公共棋盘与两条 3 格宽通道。
/// 5 块出生棋盘占 5 个槽位，图案取自白名单 <see cref="SlotPatterns"/>（"每一对棋盘都能用一块公共棋盘 + 两条直通道接上"的 536 个图案里，
/// 全部链接序列都走得通的 32 个）。</para>
/// <para><b>为什么不是 50×50</b>：规格要求任意两块出生棋盘 Chebyshev 间隔 ≥ 15；50 列里一轴最多排 3 个带（5+15+5+15+5 = 45），5 块棋盘只能落在 3×3 槽位，
/// 而 3×3 的全部 5 槽图案都存在至少一对棋盘无法用直通道接上（已穷举）。4×4 槽位需要 3×22 + 7 = 73 列。负责人 2026-10-09 裁决 R1 / R6 接受 73×73 与 32 种图案白名单，规格「初始布局」已同步为 73×73。</para>
/// <para><b>校验闭环</b>：第 k 次尝试的随机源只由（地图种子, k）决定；每次尝试先过链接档静态校验，再把<see cref="Sequences"/>（15 种四人四区配对各接决赛、
/// 全部 2+1+1 与 3+1 序列；四人四区的首轮两次链接只按规范次序——含最小出生棋盘编号的一对先，裁决 R2）逐条执行一遍——任一步链接失败（放不下也铺不出直通道、或校验不过）即本次尝试作废。
/// 通过的图保证"全部链接序列都能生成"；退化次数记入 <see cref="GeneratedLinkMap.Verification"/>。</para>
/// <para><b>链接几何</b>：在 X、Y 两战场最近的两块棋盘之间放公共棋盘（边长 9–11，与任何已有棋盘至少隔 <see cref="BoardGap"/> 格、与任何已物化格不相邻），
/// 各铺一条 3–4 宽直通道（两端各整端贴一块棋盘、侧面不贴任何已物化格、不覆盖已物化格）。候选按（两条通道总长, 偏好边长差, 偏好宽度差, 行, 列）取最小，
/// 偏好值由 (地图种子, X, Y) 的随机源给出；最近的一对放不下时按距离次序试其余棋盘对（备选位置），全部放不下时退化为一条直通道。
/// 通道长度不设上限：非相邻槽位之间的链接通道可以很长，这是槽位模型下"任意配对都能接上"的代价（见任务报告）。</para>
/// </remarks>
public static class LinkMapGenerator
{
    /// <summary>出生棋盘数，固定。</summary>
    public const int BirthBoards = 5;

    /// <summary>出生棋盘边长区间。</summary>
    public const int BirthMinSide = 5;
    public const int BirthMaxSide = 7;

    /// <summary>公共棋盘边长区间。</summary>
    public const int PublicMinSide = 9;
    public const int PublicMaxSide = 11;

    /// <summary>通道宽度区间与最短长度。</summary>
    public const int CorridorMinWidth = 3;
    public const int CorridorMaxWidth = 4;
    public const int MinCorridorLength = 2;

    /// <summary>公共棋盘与任何已有棋盘的最小间隔。</summary>
    public const int BoardGap = 2;

    /// <summary>任意两块出生棋盘之间的预留间距：一块最大公共棋盘加两条最短通道。</summary>
    public const int ReserveGap = PublicMaxSide + (2 * MinCorridorLength);

    /// <summary>槽位间距：出生棋盘最大边长 + 预留间距。</summary>
    public const int LanePitch = BirthMaxSide + ReserveGap;

    /// <summary>每轴槽位数。</summary>
    public const int Lanes = 4;

    /// <summary>画布边长（列数 = 行数），整局不变。</summary>
    public const int CanvasSide = ((Lanes - 1) * LanePitch) + BirthMaxSide;

    /// <summary>公共棋盘上边长（宽高较小者）不超过此值的放 2 个信物格，更大的放 3 个（与棋盘档同口径）。</summary>
    public const int SmallPublicSide = 10;

    /// <summary>同一块公共棋盘上两个信物格至少相隔几格（两个方向上距离的较大者）。</summary>
    private const int RelicSpacing = 3;

    /// <summary>尝试上限的缺省值。</summary>
    public const int DefaultMaxAttempts = 64;

    private static readonly ConcurrentDictionary<ulong, GeneratedLinkMap> Cache = new();

    /// <summary>
    /// 全部"逐对可接"的槽位图案（按字典序）：4×4 槽位里 5 个槽位的组合中，每一对棋盘都能"用一块公共棋盘 + 两条直通道"接上的那些
    /// （同行 / 同列相邻，或同行 / 同列隔一个空槽，或对角且至少一个拐角槽为空且拐角到两端的中间槽全空）。只在类型初始化时算一次；
    /// 它是必要条件，不是充分条件——首轮两次链接会互相挡路。生成只从 <see cref="SlotPatterns"/> 里选。
    /// </summary>
    internal static ImmutableArray<ImmutableArray<(int Col, int Row)>> AllSlotFeasiblePatterns { get; } = ComputeSlotPatterns();

    /// <summary>
    /// 生成用的槽位图案白名单：<see cref="AllSlotFeasiblePatterns"/>（536 个）里用统一边长 7、零偏移、种子 1 跑完 <see cref="Sequences"/> 全部走得通的 32 个
    /// （诊断测试 <c>槽位图案诊断Tests</c> 列出；结构上都是"主对角线 / 反对角线三块 + 第 0 行一块 + 第 0 / 3 列一块"这一族及其镜像）。
    /// 边长与槽内偏移随种子变化后仍逐次核对，核对不过就换下一次尝试。
    /// </summary>
    internal static ImmutableArray<ImmutableArray<(int Col, int Row)>> SlotPatterns { get; } =
    [
        [(0, 0), (1, 0), (1, 1), (2, 2), (3, 3)],
        [(0, 0), (2, 0), (1, 1), (2, 2), (3, 3)],
        [(0, 0), (3, 0), (2, 1), (1, 2), (0, 3)],
        [(0, 0), (3, 0), (2, 1), (1, 2), (1, 3)],
        [(0, 0), (3, 0), (2, 1), (1, 2), (2, 3)],
        [(0, 0), (3, 0), (2, 1), (1, 2), (3, 3)],
        [(0, 0), (0, 1), (1, 1), (2, 2), (3, 3)],
        [(0, 0), (1, 1), (0, 2), (2, 2), (3, 3)],
        [(0, 0), (1, 1), (2, 2), (0, 3), (3, 3)],
        [(0, 0), (2, 1), (3, 1), (1, 2), (0, 3)],
        [(0, 0), (2, 1), (1, 2), (3, 2), (0, 3)],
        [(0, 0), (2, 1), (1, 2), (0, 3), (3, 3)],
        [(1, 0), (3, 0), (2, 1), (1, 2), (0, 3)],
        [(1, 0), (0, 1), (1, 1), (2, 2), (3, 3)],
        [(1, 0), (1, 1), (0, 2), (2, 2), (3, 3)],
        [(1, 0), (1, 1), (2, 2), (0, 3), (3, 3)],
        [(1, 0), (2, 1), (3, 1), (1, 2), (0, 3)],
        [(1, 0), (2, 1), (1, 2), (3, 2), (0, 3)],
        [(1, 0), (2, 1), (1, 2), (0, 3), (3, 3)],
        [(2, 0), (3, 0), (2, 1), (1, 2), (0, 3)],
        [(2, 0), (0, 1), (1, 1), (2, 2), (3, 3)],
        [(2, 0), (1, 1), (0, 2), (2, 2), (3, 3)],
        [(2, 0), (1, 1), (2, 2), (0, 3), (3, 3)],
        [(2, 0), (2, 1), (3, 1), (1, 2), (0, 3)],
        [(2, 0), (2, 1), (1, 2), (3, 2), (0, 3)],
        [(2, 0), (2, 1), (1, 2), (0, 3), (3, 3)],
        [(3, 0), (0, 1), (1, 1), (2, 2), (3, 3)],
        [(3, 0), (1, 1), (0, 2), (2, 2), (3, 3)],
        [(3, 0), (1, 1), (2, 2), (0, 3), (3, 3)],
        [(3, 0), (2, 1), (3, 1), (1, 2), (0, 3)],
        [(3, 0), (2, 1), (1, 2), (3, 2), (0, 3)],
        [(3, 0), (2, 1), (1, 2), (0, 3), (3, 3)],
    ];

    /// <summary>
    /// 生成期与穷举测试共用的全部链接序列（规格「全部链接序列都能生成」的三类）。四人四区的首轮两次链接按规范次序（含最小出生棋盘编号的一对先）执行——
    /// 两种次序都要求走得通的话没有任何槽位图案能过（已穷举），因此配对事件（段 B）MUST 按此规范次序逐对链接。
    /// </summary>
    public static ImmutableArray<LinkSequence> Sequences { get; } = ComputeSequences();

    /// <summary>按完整标识（<c>link:&lt;种子&gt;</c>）生成初始地图。</summary>
    public static MapData Generate(string mapId) => Generate(LinkMapId.Parse(mapId));

    /// <summary>生成初始地图。<see cref="MapData.Id"/> 为规范化标识，<see cref="MapData.MapVersion"/> 为 0。结果按种子缓存（地图不可变）。</summary>
    public static MapData Generate(ulong mapSeed, int maxAttempts = DefaultMaxAttempts) => GenerateDetailed(mapSeed, maxAttempts).Map;

    /// <summary>生成初始地图，并给出实际采用的尝试序号、作废原因与链接核对统计。缺省尝试上限的结果按种子缓存。</summary>
    public static GeneratedLinkMap GenerateDetailed(ulong mapSeed, int maxAttempts = DefaultMaxAttempts)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "尝试上限至少为 1。");
        }

        if (maxAttempts == DefaultMaxAttempts && Cache.TryGetValue(mapSeed, out GeneratedLinkMap? cached))
        {
            return cached;
        }

        string id = LinkMapId.Format(mapSeed);
        ImmutableArray<string>.Builder discarded = ImmutableArray.CreateBuilder<string>();
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            MapData map = Layout(id, MapRandom.ForLinkAttempt(mapSeed, attempt));
            MapValidationResult validation = MapValidator.Validate(map);
            if (!validation.IsValid)
            {
                discarded.Add($"第 {attempt} 次尝试未通过链接档静态校验：{string.Join("；", validation.Failures)}");
                continue;
            }

            if (!TryVerify(map, mapSeed, out LinkVerification verification, out string failure))
            {
                discarded.Add($"第 {attempt} 次尝试有链接序列走不通：{failure}");
                continue;
            }

            var made = new GeneratedLinkMap(map, attempt, discarded.ToImmutable(), verification);
            if (maxAttempts == DefaultMaxAttempts)
            {
                Cache.TryAdd(mapSeed, made);
            }

            return made;
        }

        throw new MapGenerationException(
            $"地图 {id} 在 {maxAttempts} 次尝试内没有得到全部链接序列都能生成的地图。最后一次的失败原因——{discarded[^1]}");
    }

    /// <summary>
    /// 在当前物化状态上执行一次链接（地图种子取自 <see cref="MapData.Id"/>）。
    /// </summary>
    public static LinkResult Link(MapData current, int battlefieldX, int battlefieldY)
    {
        ArgumentNullException.ThrowIfNull(current);
        return Link(current, battlefieldX, battlefieldY, LinkMapId.Parse(current.Id));
    }

    /// <summary>
    /// 在当前物化状态上执行一次链接：把战场 <paramref name="battlefieldX"/> 与 <paramref name="battlefieldY"/>（标识 = 分量内最小出生棋盘编号）合并，
    /// 产出版本号 +1 的新地图；几何由 (<paramref name="mapSeed"/>, X, Y) 与当前物化状态确定性派生，不改写任何已有格。
    /// 新地图通过链接档静态校验后才返回，不通过抛 <see cref="MapValidationException"/>（生成器缺陷，MUST NOT 静默继续）。
    /// 非链接档地图抛 <see cref="SiegeRuleException"/>（该规格档不允许增长）；标识不存在或相同抛 <see cref="ArgumentException"/>；
    /// 公共棋盘与直通道都放不下抛 <see cref="MapGenerationException"/>。
    /// </summary>
    public static LinkResult Link(MapData current, int battlefieldX, int battlefieldY, ulong mapSeed)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Profile != MapProfile.Link)
        {
            throw new SiegeRuleException($"地图 {current.Id} 的规格档为 {current.Profile}，不允许增长：只有链接档地图可以执行链接。");
        }

        if (battlefieldX == battlefieldY)
        {
            throw new ArgumentException($"链接的两个战场必须不同，实际都是 {battlefieldX}。", nameof(battlefieldY));
        }

        Battlefields fields = Battlefields.Of(current);
        Battlefield fx = fields.ById(battlefieldX)
            ?? throw new ArgumentException($"地图 {current.Id}（版本 {current.MapVersion}）上没有标识为 {battlefieldX} 的战场。", nameof(battlefieldX));
        Battlefield fy = fields.ById(battlefieldY)
            ?? throw new ArgumentException($"地图 {current.Id}（版本 {current.MapVersion}）上没有标识为 {battlefieldY} 的战场。", nameof(battlefieldY));
        if (fx.Boards.IsEmpty || fy.Boards.IsEmpty)
        {
            throw new ArgumentException($"两个战场都必须至少含一块棋盘：战场 {BirthZoneLabel.Of(battlefieldX)} 有 {fx.Boards.Length} 块、战场 {BirthZoneLabel.Of(battlefieldY)} 有 {fy.Boards.Length} 块。");
        }

        RandomStream rng = MapRandom.ForLink(mapSeed, battlefieldX, battlefieldY);
        int preferWidth = PublicMinSide + rng.NextInt(PublicMaxSide - PublicMinSide + 1);
        int preferHeight = PublicMinSide + rng.NextInt(PublicMaxSide - PublicMinSide + 1);
        int preferCorridor = CorridorMinWidth + rng.NextInt(CorridorMaxWidth - CorridorMinWidth + 1);

        var grid = new Occupancy(current);
        Rect[] plates = [.. current.Boards.Select(Rect.Of)];

        // 棋盘对按间隔升序（并列按下标），最近的一对优先；放不下再试下一对（备选位置）。
        var pairs = new List<(int Gap, int A, int B)>();
        foreach (int a in fx.Boards)
        {
            foreach (int b in fy.Boards)
            {
                pairs.Add((plates[a].GapTo(plates[b]), a, b));
            }
        }

        pairs.Sort();

        var newCells = new List<Coord>();
        BoardPlate? publicBoard = null;
        ImmutableArray<LinkCorridor> corridors = [];
        bool degraded = false;
        int from = -1;
        int to = -1;
        foreach ((int _, int a, int b) in pairs)
        {
            if (TryPlacePublic(grid, plates, plates[a], plates[b], preferWidth, preferHeight, preferCorridor, out Rect p, out Rect c1, out Rect c2))
            {
                publicBoard = new BoardPlate(new Coord(p.X, p.Y), p.W, p.H, BoardPlateKind.Public);
                corridors = [c1.ToCorridor(), c2.ToCorridor()];
                newCells.AddRange(p.Cells());
                newCells.AddRange(c1.Cells());
                newCells.AddRange(c2.Cells());
                from = a;
                to = b;
                break;
            }
        }

        if (publicBoard is null)
        {
            foreach ((int _, int a, int b) in pairs)
            {
                if (TryCorridor(grid, plates[a], plates[b], preferCorridor, out Rect c))
                {
                    corridors = [c.ToCorridor()];
                    newCells.AddRange(c.Cells());
                    degraded = true;
                    from = a;
                    to = b;
                    break;
                }
            }
        }

        if (from < 0)
        {
            throw new MapGenerationException(
                $"地图 {current.Id}（版本 {current.MapVersion}）上战场 {BirthZoneLabel.Of(battlefieldX)} 与 {BirthZoneLabel.Of(battlefieldY)} 之间放不下公共棋盘，也铺不出直通道。");
        }

        // 信物格：公共棋盘按边长 2–3 个，最外一圈以内、两两至少相隔 RelicSpacing；第一个高档、其余标准档；内容由对局层另派子流生成。
        ImmutableDictionary<Coord, RelicCellSpec>.Builder relics = current.RelicCells.ToBuilder();
        if (publicBoard is { } plate)
        {
            int want = Math.Min(plate.Width, plate.Height) <= SmallPublicSide ? 2 : 3;
            var placed = new List<Coord>();
            for (int i = 0; i < want; i++)
            {
                var candidates = new List<Coord>();
                for (int y = plate.Origin.Y + 1; y < plate.Origin.Y + plate.Height - 1; y++)
                {
                    for (int x = plate.Origin.X + 1; x < plate.Origin.X + plate.Width - 1; x++)
                    {
                        var c = new Coord(x, y);
                        if (placed.All(r => Math.Max(Math.Abs(r.X - x), Math.Abs(r.Y - y)) >= RelicSpacing))
                        {
                            candidates.Add(c);
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    break;
                }

                Coord pick = candidates[rng.NextInt(candidates.Count)];
                placed.Add(pick);
                relics[pick] = new RelicCellSpec(RelicZone.Contested, i == 0 ? BudgetTier.High : BudgetTier.Standard);
            }
        }

        MapData grown = current with
        {
            Obstacles = current.Obstacles.Except(newCells),
            Boards = publicBoard is { } added ? current.Boards.Add(added) : current.Boards,
            RelicCells = relics.ToImmutable(),
            ChokePoints = current.ChokePoints.Union(corridors.SelectMany(c => c.Cells())),
            MapVersion = current.MapVersion + 1,
        };

        MapValidationResult validation = MapValidator.Validate(grown);
        if (!validation.IsValid)
        {
            throw new MapValidationException(validation);
        }

        bool closest = from == pairs[0].A && to == pairs[0].B;
        string summary = publicBoard is { } pb
            ? $"链接 {BirthZoneLabel.Of(battlefieldX)}↔{BirthZoneLabel.Of(battlefieldY)}：{Describe(current.Boards[from])} 与 {Describe(current.Boards[to])} 之间新增公共棋盘 {Describe(pb)}，通道 {corridors[0]}、{corridors[1]}{(closest ? string.Empty : "（备选棋盘对）")}"
            : $"退化链接 {BirthZoneLabel.Of(battlefieldX)}↔{BirthZoneLabel.Of(battlefieldY)}：{Describe(current.Boards[from])} 与 {Describe(current.Boards[to])} 之间只铺一条直通道 {corridors[0]}，没有公共棋盘";
        return new LinkResult(grown, publicBoard, corridors, degraded, from, to, closest, summary);
    }

    // ---------- 初始布局 ----------

    /// <summary>
    /// 诊断入口：用指定图案、统一边长 <paramref name="side"/>、零偏移、信物在中心摆一张初始图并跑完整核对；返回是否通过、失败说明与统计。
    /// 只供测试与图案筛选，不进生产路径。
    /// </summary>
    internal static (bool Ok, string Failure, LinkVerification? Verification) VerifyPattern(ImmutableArray<(int Col, int Row)> pattern, int side, ulong mapSeed)
    {
        var plates = ImmutableArray.CreateBuilder<BoardPlate>(BirthBoards);
        var zones = ImmutableArray.CreateBuilder<ImmutableHashSet<Coord>>(BirthBoards);
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var playable = new HashSet<Coord>();
        foreach ((int col, int row) in pattern)
        {
            var plate = new BoardPlate(new Coord(col * LanePitch, row * LanePitch), side, side, BoardPlateKind.Birth);
            plates.Add(plate);
            zones.Add([.. plate.Cells()]);
            playable.UnionWith(plate.Cells());
            relics[new Coord(plate.Origin.X + (side / 2), plate.Origin.Y + (side / 2))] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        MapData map = Assemble(LinkMapId.Format(mapSeed), plates.MoveToImmutable(), zones.MoveToImmutable(), relics.ToImmutable(), playable);
        MapValidationResult validation = MapValidator.Validate(map);
        if (!validation.IsValid)
        {
            return (false, validation.ToString(), null);
        }

        bool ok = TryVerify(map, mapSeed, out LinkVerification verification, out string failure);
        return (ok, failure, ok ? verification : null);
    }

    /// <summary>一次尝试的布局：选图案、抽各棋盘的宽高与槽内偏移、各放 1 个信物格；其余格一律障碍。</summary>
    private static MapData Layout(string id, RandomStream rng)
    {
        ImmutableArray<(int Col, int Row)> pattern = SlotPatterns[rng.NextInt(SlotPatterns.Length)];
        var plates = ImmutableArray.CreateBuilder<BoardPlate>(BirthBoards);
        var zones = ImmutableArray.CreateBuilder<ImmutableHashSet<Coord>>(BirthBoards);
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var playable = new HashSet<Coord>();
        foreach ((int col, int row) in pattern)
        {
            int w = BirthMinSide + rng.NextInt(BirthMaxSide - BirthMinSide + 1);
            int h = BirthMinSide + rng.NextInt(BirthMaxSide - BirthMinSide + 1);
            int x = (col * LanePitch) + rng.NextInt(BirthMaxSide - w + 1);
            int y = (row * LanePitch) + rng.NextInt(BirthMaxSide - h + 1);
            var plate = new BoardPlate(new Coord(x, y), w, h, BoardPlateKind.Birth);
            plates.Add(plate);
            zones.Add([.. plate.Cells()]);
            playable.UnionWith(plate.Cells());

            // 信物格在最外一圈以内（边长 ≥ 5 → 内圈至少 3×3）。
            var relic = new Coord(x + 1 + rng.NextInt(w - 2), y + 1 + rng.NextInt(h - 2));
            relics[relic] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        return Assemble(id, plates.MoveToImmutable(), zones.MoveToImmutable(), relics.ToImmutable(), playable);
    }

    /// <summary>把 5 块出生棋盘灌成初始地图数据：其余格一律障碍，没有咽喉，中央入口取第一块棋盘的中心格。</summary>
    private static MapData Assemble(
        string id, ImmutableArray<BoardPlate> plates, ImmutableArray<ImmutableHashSet<Coord>> zones, ImmutableDictionary<Coord, RelicCellSpec> relics, HashSet<Coord> playable)
    {
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = 0; y < CanvasSide; y++)
        {
            for (int x = 0; x < CanvasSide; x++)
            {
                var c = new Coord(x, y);
                if (!playable.Contains(c))
                {
                    obstacles.Add(c);
                }
            }
        }

        BoardPlate first = plates[0];
        return new MapData
        {
            Id = id,
            Width = CanvasSide,
            Height = CanvasSide,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Obstacles = obstacles.ToImmutable(),
            BirthZones = zones,
            Boards = plates,
            RelicCells = relics,
            ChokePoints = ImmutableHashSet<Coord>.Empty,
            CentralEntrance = new Coord(first.Origin.X + (first.Width / 2), first.Origin.Y + (first.Height / 2)),
            MapVersion = 0,
        };
    }

    /// <summary>把 <see cref="Sequences"/> 逐条跑一遍；任一步抛异常即失败并说明哪一条、哪一步。</summary>
    private static bool TryVerify(MapData initial, ulong mapSeed, out LinkVerification verification, out string failure)
    {
        int[] links = new int[3];
        int[] degraded = new int[3];
        int[] alternate = new int[3];

        // 同一前缀的序列共享中间地图：按步骤前缀记忆几何，避免把同一次链接算多遍；但每条序列的每一步都计入它所属类别的统计。
        var memo = new Dictionary<string, LinkResult>(StringComparer.Ordinal);
        foreach (LinkSequence sequence in Sequences)
        {
            MapData map = initial;
            string key = string.Empty;
            foreach ((int x, int y) in sequence.Steps)
            {
                key += $"({x},{y});";
                if (!memo.TryGetValue(key, out LinkResult? result))
                {
                    try
                    {
                        result = Link(map, x, y, mapSeed);
                    }
                    catch (Exception ex) when (ex is MapGenerationException or MapValidationException)
                    {
                        verification = default!;
                        failure = $"{sequence} 在链接 ({x},{y}) 时失败：{ex.Message.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}";
                        return false;
                    }

                    memo[key] = result;
                }

                int kind = (int)sequence.Kind;
                links[kind]++;
                if (result.Degraded)
                {
                    degraded[kind]++;
                }
                else if (!result.ClosestPair)
                {
                    alternate[kind]++;
                }

                map = result.Map;
            }
        }

        verification = new LinkVerification(
            links[0], degraded[0], alternate[0],
            links[1], degraded[1], alternate[1],
            links[2], degraded[2], alternate[2]);
        failure = string.Empty;
        return true;
    }

    // ---------- 链接几何 ----------

    /// <summary>
    /// 在棋盘 <paramref name="a"/> 与 <paramref name="b"/> 之间找公共棋盘的最佳放置：枚举全部边长与位置，要求与任何已物化格不相邻、与任何棋盘隔 ≥ 2，
    /// 且 a→P、b→P 各有一条直通道；按（两条通道总长, 偏好边长差, 偏好宽度差, 行, 列）取最小。
    /// </summary>
    private static bool TryPlacePublic(
        Occupancy grid, Rect[] plates, Rect a, Rect b, int preferWidth, int preferHeight, int preferCorridor,
        out Rect best, out Rect bestC1, out Rect bestC2)
    {
        best = default;
        bestC1 = default;
        bestC2 = default;
        (int, int, int, int, int)? bestCost = null;
        for (int h = PublicMinSide; h <= PublicMaxSide; h++)
        {
            for (int w = PublicMinSide; w <= PublicMaxSide; w++)
            {
                int sizeCost = Math.Abs(w - preferWidth) + Math.Abs(h - preferHeight);
                for (int y = 0; y + h <= grid.Height; y++)
                {
                    for (int x = 0; x + w <= grid.Width; x++)
                    {
                        var p = new Rect(x, y, w, h);
                        if (!p.Faces(a) || !p.Faces(b))
                        {
                            continue;
                        }

                        if (grid.AnyPlayable(p.Grow(1)) || grid.AnyPlate(p.Grow(BoardGap)))
                        {
                            continue;
                        }

                        grid.Push(p);
                        foreach ((Rect c1, int widthCost1) in CorridorOptions(grid, a, p, preferCorridor))
                        {
                            grid.Push(c1);
                            foreach ((Rect c2, int widthCost2) in CorridorOptions(grid, b, p, preferCorridor))
                            {
                                var cost = (c1.Length + c2.Length, sizeCost, widthCost1 + widthCost2, y, x);
                                if (bestCost is null || cost.CompareTo(bestCost.Value) < 0)
                                {
                                    bestCost = cost;
                                    best = p;
                                    bestC1 = c1;
                                    bestC2 = c2;
                                }

                                break;   // 同一条 c1 下 c2 已按偏好次序给出，第一条就是该 c1 的最优
                            }

                            grid.Pop(c1);
                        }

                        grid.Pop(p);
                    }
                }
            }
        }

        return bestCost is not null;
    }

    /// <summary>退化：a 与 b 之间一条直通道（最窄的偏好宽度优先）。</summary>
    private static bool TryCorridor(Occupancy grid, Rect a, Rect b, int preferCorridor, out Rect corridor)
    {
        foreach ((Rect c, int _) in CorridorOptions(grid, a, b, preferCorridor))
        {
            corridor = c;
            return true;
        }

        corridor = default;
        return false;
    }

    /// <summary>
    /// 棋盘 <paramref name="a"/> 与矩形 <paramref name="p"/> 之间全部可铺的直通道，按（偏好宽度差, 居中偏差, 位置）排序：
    /// 两者在一轴上投影重叠 ≥ 通道宽、另一轴上相隔 ≥ 最短长度；通道格及其两侧各一格都不得是已物化格（两端贴的就是 a 与 p）。
    /// </summary>
    private static IEnumerable<(Rect Corridor, int WidthCost)> CorridorOptions(Occupancy grid, Rect a, Rect p, int preferCorridor)
    {
        var found = new List<((int WidthCost, int Center, int Pos) Key, Rect Corridor)>();
        // 横向：a 在 p 的左或右。
        if (a.X1 < p.X || p.X1 < a.X)
        {
            int lo = Math.Max(a.Y, p.Y);
            int hi = Math.Min(a.Y1, p.Y1);
            int x0 = a.X1 < p.X ? a.X1 + 1 : p.X1 + 1;
            int length = a.X1 < p.X ? p.X - a.X1 - 1 : a.X - p.X1 - 1;
            if (length >= MinCorridorLength)
            {
                for (int cw = CorridorMinWidth; cw <= CorridorMaxWidth; cw++)
                {
                    for (int y0 = lo; y0 + cw - 1 <= hi; y0++)
                    {
                        var c = new Rect(x0, y0, length, cw);
                        if (!grid.AnyPlayable(new Rect(x0, y0 - 1, length, cw + 2)))
                        {
                            int center = Math.Abs((2 * y0) + cw - 1 - (lo + hi));
                            found.Add(((Math.Abs(cw - preferCorridor), center, y0), c));
                        }
                    }
                }
            }
        }

        // 纵向：a 在 p 的下或上。
        if (a.Y1 < p.Y || p.Y1 < a.Y)
        {
            int lo = Math.Max(a.X, p.X);
            int hi = Math.Min(a.X1, p.X1);
            int y0 = a.Y1 < p.Y ? a.Y1 + 1 : p.Y1 + 1;
            int length = a.Y1 < p.Y ? p.Y - a.Y1 - 1 : a.Y - p.Y1 - 1;
            if (length >= MinCorridorLength)
            {
                for (int cw = CorridorMinWidth; cw <= CorridorMaxWidth; cw++)
                {
                    for (int x0 = lo; x0 + cw - 1 <= hi; x0++)
                    {
                        var c = new Rect(x0, y0, cw, length);
                        if (!grid.AnyPlayable(new Rect(x0 - 1, y0, cw + 2, length)))
                        {
                            int center = Math.Abs((2 * x0) + cw - 1 - (lo + hi));
                            found.Add(((Math.Abs(cw - preferCorridor), center, x0), c));
                        }
                    }
                }
            }
        }

        found.Sort((l, r) => l.Key.CompareTo(r.Key));
        return found.Select(f => (f.Corridor, f.Key.WidthCost));
    }

    private static string Describe(BoardPlate plate)
    {
        string kind = plate.Kind == BoardPlateKind.Birth ? "出生棋盘" : "公共棋盘";
        return $"{kind} {plate.Origin.ToNotation()}–{new Coord(plate.Origin.X + plate.Width - 1, plate.Origin.Y + plate.Height - 1).ToNotation()}（{plate.Width}×{plate.Height}）";
    }

    // ---------- 槽位图案与链接序列 ----------

    private static ImmutableArray<ImmutableArray<(int Col, int Row)>> ComputeSlotPatterns()
    {
        var slots = new List<(int Col, int Row)>();
        for (int row = 0; row < Lanes; row++)
        {
            for (int col = 0; col < Lanes; col++)
            {
                slots.Add((col, row));
            }
        }

        var patterns = ImmutableArray.CreateBuilder<ImmutableArray<(int Col, int Row)>>();
        foreach (int[] pick in Combinations(slots.Count, BirthBoards))
        {
            (int Col, int Row)[] chosen = [.. pick.Select(i => slots[i])];
            var empty = new HashSet<(int, int)>(slots);
            empty.ExceptWith(chosen);
            bool ok = true;
            for (int i = 0; i < chosen.Length && ok; i++)
            {
                for (int j = i + 1; j < chosen.Length && ok; j++)
                {
                    ok = SlotPairLinkable(empty, chosen[i], chosen[j]);
                }
            }

            if (ok)
            {
                patterns.Add([.. chosen]);
            }
        }

        return patterns.ToImmutable();
    }

    /// <summary>槽位 a、b 之间能否放公共棋盘并用两条直通道接上：同行 / 同列时中间槽全空；对角时某个拐角槽为空且拐角到两端的中间槽全空。</summary>
    private static bool SlotPairLinkable(HashSet<(int, int)> empty, (int Col, int Row) a, (int Col, int Row) b)
    {
        static IEnumerable<(int, int)> Between((int Col, int Row) p, (int Col, int Row) q)
        {
            if (p.Row == q.Row)
            {
                for (int c = Math.Min(p.Col, q.Col) + 1; c < Math.Max(p.Col, q.Col); c++)
                {
                    yield return (c, p.Row);
                }
            }
            else
            {
                for (int r = Math.Min(p.Row, q.Row) + 1; r < Math.Max(p.Row, q.Row); r++)
                {
                    yield return (p.Col, r);
                }
            }
        }

        if (a.Row == b.Row || a.Col == b.Col)
        {
            return Between(a, b).All(empty.Contains);
        }

        foreach ((int, int) corner in new[] { (b.Col, a.Row), (a.Col, b.Row) })
        {
            if (empty.Contains(corner) && Between(a, corner).All(empty.Contains) && Between(corner, b).All(empty.Contains))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<LinkSequence> ComputeSequences()
    {
        var list = ImmutableArray.CreateBuilder<LinkSequence>();

        // 四人四区：选 4 块棋盘（5 种），三种配对；首轮两次链接按**规范次序**执行（含最小出生棋盘编号的一对先，段 B 的配对事件 MUST 照此次序逐对链接），
        // 再决赛（两个合并战场的标识 = 各自最小编号）。
        foreach (int[] four in Combinations(BirthBoards, 4))
        {
            int a = four[0], b = four[1], c = four[2], d = four[3];
            foreach (((int, int) p, (int, int) q) in new[] { ((a, b), (c, d)), ((a, c), (b, d)), ((a, d), (b, c)) })
            {
                list.Add(new LinkSequence(LinkSequenceKind.FourZones, [p, q, (Math.Min(p.Item1, p.Item2), Math.Min(q.Item1, q.Item2))]));
            }
        }

        // 2+1+1：两名独占者（a, b）配对，再与同区棋盘 c 决赛。
        foreach (int[] two in Combinations(BirthBoards, 2))
        {
            for (int c = 0; c < BirthBoards; c++)
            {
                if (c != two[0] && c != two[1])
                {
                    list.Add(new LinkSequence(LinkSequenceKind.TwoPlusOnePlusOne, [(two[0], two[1]), (two[0], c)]));
                }
            }
        }

        // 3+1：独占者与三人同区棋盘的胜者直接决赛。
        foreach (int[] two in Combinations(BirthBoards, 2))
        {
            list.Add(new LinkSequence(LinkSequenceKind.ThreePlusOne, [(two[0], two[1])]));
        }

        return list.ToImmutable();
    }

    /// <summary>n 选 k 的全部组合，字典序，每个组合内升序。</summary>
    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var pick = new int[k];
        for (int i = 0; i < k; i++)
        {
            pick[i] = i;
        }

        while (true)
        {
            yield return (int[])pick.Clone();
            int j = k - 1;
            while (j >= 0 && pick[j] == n - k + j)
            {
                j--;
            }

            if (j < 0)
            {
                yield break;
            }

            pick[j]++;
            for (int i = j + 1; i < k; i++)
            {
                pick[i] = pick[i - 1] + 1;
            }
        }
    }

    // ---------- 工作态 ----------

    /// <summary>画布里的矩形：左下角与宽高（全部闭区间运算）。</summary>
    internal readonly record struct Rect(int X, int Y, int W, int H)
    {
        internal int X1 => X + W - 1;

        internal int Y1 => Y + H - 1;

        internal int Length => Math.Max(W, H);

        internal static Rect Of(BoardPlate plate) => new(plate.Origin.X, plate.Origin.Y, plate.Width, plate.Height);

        internal Rect Grow(int by) => new(X - by, Y - by, W + (2 * by), H + (2 * by));

        internal LinkCorridor ToCorridor() => new(new Coord(X, Y), W, H);

        /// <summary>与另一矩形的间隔：两个方向上空格数的较大者（投影重叠的方向为负），与校验器 <c>ValidatePlateGaps</c> 同口径。</summary>
        internal int GapTo(Rect other) =>
            Math.Max(Math.Max(X - (other.X + other.W), other.X - (X + W)), Math.Max(Y - (other.Y + other.H), other.Y - (Y + H)));

        /// <summary>是否与另一矩形在某一轴上投影重叠至少一条通道的最小宽度（直通道的必要条件）。</summary>
        internal bool Faces(Rect other) =>
            Math.Min(Y1, other.Y1) - Math.Max(Y, other.Y) + 1 >= CorridorMinWidth
            || Math.Min(X1, other.X1) - Math.Max(X, other.X) + 1 >= CorridorMinWidth;

        internal IEnumerable<Coord> Cells()
        {
            for (int y = Y; y <= Y1; y++)
            {
                for (int x = X; x <= X1; x++)
                {
                    yield return new Coord(x, y);
                }
            }
        }
    }

    /// <summary>
    /// 当前物化状态的占用表：可落子格与棋盘格各一张二维前缀和，矩形查询 O(1)；候选公共棋盘与通道用 <see cref="Push"/> / <see cref="Pop"/> 临时压入
    /// （压入期间对应矩形直接计入"可落子"，查询先看临时层）。越界部分按不占用处理（画布边缘就是边缘）。
    /// </summary>
    private sealed class Occupancy
    {
        private readonly int[,] _playable;
        private readonly int[,] _plate;
        private readonly List<Rect> _pushed = [];

        internal Occupancy(MapData map)
        {
            Width = map.Width;
            Height = map.Height;
            _playable = new int[Width + 1, Height + 1];
            _plate = new int[Width + 1, Height + 1];
            var plateCell = new bool[Width, Height];
            foreach (BoardPlate plate in map.Boards)
            {
                foreach (Coord c in plate.Cells())
                {
                    if (map.Contains(c))
                    {
                        plateCell[c.X, c.Y] = true;
                    }
                }
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    int playable = map.IsPlayable(new Coord(x, y)) ? 1 : 0;
                    _playable[x + 1, y + 1] = playable + _playable[x, y + 1] + _playable[x + 1, y] - _playable[x, y];
                    _plate[x + 1, y + 1] = (plateCell[x, y] ? 1 : 0) + _plate[x, y + 1] + _plate[x + 1, y] - _plate[x, y];
                }
            }
        }

        internal int Width { get; }

        internal int Height { get; }

        internal void Push(Rect r) => _pushed.Add(r);

        internal void Pop(Rect r)
        {
            if (_pushed.Count == 0 || _pushed[^1] != r)
            {
                throw new InvalidOperationException("占用表的临时层必须按压入的逆序弹出。");
            }

            _pushed.RemoveAt(_pushed.Count - 1);
        }

        internal bool AnyPlayable(Rect r)
        {
            foreach (Rect p in _pushed)
            {
                if (Overlaps(p, r))
                {
                    return true;
                }
            }

            return Sum(_playable, r) > 0;
        }

        internal bool AnyPlate(Rect r)
        {
            foreach (Rect p in _pushed)
            {
                if (Overlaps(p, r))
                {
                    return true;
                }
            }

            return Sum(_plate, r) > 0;
        }

        private static bool Overlaps(Rect a, Rect b) => a.X <= b.X1 && b.X <= a.X1 && a.Y <= b.Y1 && b.Y <= a.Y1;

        private int Sum(int[,] prefix, Rect r)
        {
            int x0 = Math.Max(r.X, 0);
            int y0 = Math.Max(r.Y, 0);
            int x1 = Math.Min(r.X1, Width - 1);
            int y1 = Math.Min(r.Y1, Height - 1);
            if (x0 > x1 || y0 > y1)
            {
                return 0;
            }

            return prefix[x1 + 1, y1 + 1] - prefix[x0, y1 + 1] - prefix[x1 + 1, y0] + prefix[x0, y0];
        }
    }
}
