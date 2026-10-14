using System.Collections.Concurrent;
using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>拼接时被贴方贴到锚点的哪一侧（裁决 J1）：首轮链接贴东侧，决赛链接贴北侧。由对局流程按阶段决定并传给生成器。</summary>
public enum LinkSide
{
    /// <summary>被贴方最西列 = 锚点外接矩形最东列 + 1；纵向取共享边最长、并列偏南。</summary>
    East,

    /// <summary>被贴方最南行 = 锚点外接矩形最北行 + 1；横向取共享边最长、并列偏西。</summary>
    North,
}

/// <summary>一次拼接里某个战场的整体平移向量（锚点通常为零向量，只在整体越界平移时非零，裁决 J8-3；未参与链接但为腾位置而移动的战场也各记一条）。</summary>
public readonly record struct BattlefieldShift(int Battlefield, int Dx, int Dy)
{
    /// <summary>是否真的移动了。</summary>
    public bool IsZero => Dx == 0 && Dy == 0;

    public override string ToString() => $"{BirthZoneLabel.Of(Battlefield)}({Dx:+#;-#;0},{Dy:+#;-#;0})";
}

/// <summary>
/// 一次拼接的结果（board-link-mode D2 拼接版 / 规格 link-map-generation「链接时的棋盘拼接」）：新地图（版本号 +1、画布可能重裁）、贴边、锚点与被贴方的标识、共享边长度、
/// 拼接前每个战场的平移向量（按战场标识升序，含锚点——通常为零向量），以及一句供日志记录的几何摘要。
/// <see cref="Translate"/> 把拼接前地图上任一可落子格映射到新地图上的坐标——盘面占用、改造列表、信物账本与测试都只经它迁移，不另判"格属于哪个战场"。
/// </summary>
public sealed class LinkResult
{
    private readonly Battlefields _before;
    private readonly ImmutableDictionary<int, (int Dx, int Dy)> _shiftOf;
    private readonly ImmutableArray<BoardPlate> _platesBefore;
    private readonly ImmutableArray<(int Dx, int Dy)> _plateShifts;

    internal LinkResult(
        MapData map,
        Battlefields before,
        ImmutableArray<BoardPlate> platesBefore,
        ImmutableArray<(int Dx, int Dy)> plateShifts,
        LinkSide side,
        int anchor,
        int mover,
        int sharedEdge,
        ImmutableArray<BattlefieldShift> shifts,
        string summary)
    {
        Map = map;
        _before = before;
        _platesBefore = platesBefore;
        _plateShifts = plateShifts;
        Side = side;
        Anchor = anchor;
        Mover = mover;
        SharedEdge = sharedEdge;
        Shifts = shifts;
        Summary = summary;
        _shiftOf = shifts.ToImmutableDictionary(s => s.Battlefield, s => (s.Dx, s.Dy));
    }

    /// <summary>拼接后的地图（版本号 = 拼接前 + 1）。</summary>
    public MapData Map { get; }

    /// <summary>贴边。</summary>
    public LinkSide Side { get; }

    /// <summary>锚点战场标识（两个标识中的小者），坐标不变。</summary>
    public int Anchor { get; }

    /// <summary>被贴方战场标识（两个标识中的大者），整体平移。</summary>
    public int Mover { get; }

    /// <summary>共享边长度：接缝两侧都有可落子格的格数，≥ 1。</summary>
    public int SharedEdge { get; }

    /// <summary>拼接前每个战场的平移向量，按标识升序；锚点通常为零向量（整体越界平移时非零，裁决 J8-3）。</summary>
    public ImmutableArray<BattlefieldShift> Shifts { get; }

    /// <summary>几何摘要（日志用）。</summary>
    public string Summary { get; }

    /// <summary>
    /// 拼接前地图上的可落子格 → 新地图上的坐标；棋盘内的不可落子格（岩石、未架桥深水——搭桥改造的目标就在这里，link-map-terrain）按所在棋盘平移，与 <see cref="LinkLayout"/> 同口径。
    /// 棋盘之外的场景格抛 <see cref="ArgumentException"/>。
    /// </summary>
    public Coord Translate(Coord before)
    {
        if (_before.At(before) is { } field)
        {
            (int dx, int dy) = _shiftOf[field.Id];
            return new Coord(before.X + dx, before.Y + dy);
        }

        return LinkMapGenerator.MoveByPlate(_platesBefore, _plateShifts, before)
            ?? throw new ArgumentException($"{before.ToNotation()} 不是拼接前地图上棋盘内的格，没有平移向量。", nameof(before));
    }
}

/// <summary>链接序列的类别（规格 link-map-generation「全部链接序列都能拼接」的三类）。</summary>
public enum LinkSequenceKind
{
    /// <summary>四人四区：首轮配对两次东贴，再决赛北贴。</summary>
    FourZones,

    /// <summary>2+1+1：两名独占者先配对东贴，胜者再与同区棋盘决赛北贴（出生棋盘 ↔ 合并战场）。</summary>
    TwoPlusOnePlusOne,

    /// <summary>3+1：独占者直接与三人同区棋盘的胜者决赛北贴（出生棋盘 ↔ 出生棋盘）。</summary>
    ThreePlusOne,
}

/// <summary>一次链接步骤：两个战场标识（当时分量内最小的出生棋盘编号）与贴边。</summary>
public readonly record struct LinkStep(int X, int Y, LinkSide Side)
{
    public override string ToString() => $"({X},{Y}){(Side == LinkSide.East ? "东" : "北")}";
}

/// <summary>一条链接序列：类别与依次执行的若干步。</summary>
public sealed record LinkSequence(LinkSequenceKind Kind, ImmutableArray<LinkStep> Steps)
{
    public override string ToString() => $"{Kind}：{string.Join(" → ", Steps)}";
}

/// <summary>
/// 链接档地图生成器（board-link-mode D2 拼接版，负责人 2026-10-12 裁决 A + J1–J7；规格 link-map-generation）。两个入口：
/// <see cref="Generate(ulong)"/> 由地图种子生成<b>初始布局</b>（5 块隔绝的出生棋盘一字排开）；
/// <see cref="Link(MapData, int, int, LinkSide)"/> 在当前物化状态上执行一次<b>拼接</b>（被贴方整体平移到锚点东侧 / 北侧紧贴），产出版本号 +1 的新地图与各战场的平移向量。
/// 全程整数运算，随机只在初始布局（<see cref="MapRandom.ForLinkLayout"/>），拼接不消费任何随机；不读时钟与环境，也拿不到对局种子。
/// </summary>
/// <remarks>
/// <para><b>初始布局</b>（J4 / J2，link-map-terrain D3）：5 块出生棋盘边长各 6–8、次序由地图种子决定，底边对齐在第 <see cref="Margin"/> 行、自第 <see cref="Margin"/> 列起以固定间距 <see cref="Gap"/> 一字排开；
/// 5 个主题两两不同、由地图种子排列；每块按主题生成地形（<c>LinkMapGenerator.Themes.cs</c>），出生区 = 中央 3×3 核心区，核心区内恰 2 个出生区档信物格（互不相邻、尽量不同行不同列），
/// 另在主题的难到达格上至多 1 个公共区档信物格；画布按布局四周各留 <see cref="Margin"/> 格裁出一次。没有槽位、白名单、公共棋盘与通道。</para>
/// <para><b>拼接</b>（J1 / J3）：锚点 = 两个标识中的小者（即含最小出生棋盘编号的战场），坐标不变；被贴方整体平移——东贴时最西列 = 锚点最东列 + 1，北贴时最南行 = 锚点最北行 + 1；
/// 另一分量在"接缝两侧都有可落子格的格数"最多的候选里取偏南 / 偏西者，共享边 MUST ≥ 1。</para>
/// <para><b>画布</b>（J7 / J8-1）：原点优先固定、西 / 南边界不动，只在东 / 北侧按需扩展或收缩（边界外留 <see cref="Margin"/> 格）；未参与链接的战场里整体位于合并战场以西（至少隔一列空列）的原地不动，
/// 其余按原相对次序（当前最西列升序）收拢排到合并战场东侧、间距 <see cref="Gap"/>，底边不动。因此一次链接可能平移多个战场，每个都记一条向量。</para>
/// <para><b>越界</b>（J8-3）：共享边并列时先排除会让被贴方越出画布原点的候选，再取偏南 / 偏西；最长共享边的候选全部越界时，整个布局（含锚点）整体向东 / 北平移到恰好不越界，
/// 锚点此时有非零向量（"锚点不变"降为优先原则）。不抛异常。</para>
/// </remarks>
public static partial class LinkMapGenerator
{
    /// <summary>出生棋盘数，固定。</summary>
    public const int BirthBoards = 5;

    /// <summary>出生棋盘边长区间（link-map-terrain 裁决 M9）。</summary>
    public const int BirthMinSide = 6;
    public const int BirthMaxSide = 8;

    /// <summary>每块出生棋盘的出生区档信物格数（裁决 J2），都在核心区内（link-map-terrain D1）。</summary>
    public const int RelicsPerBirthBoard = 2;

    /// <summary>战场 / 出生棋盘之间的固定间距（裁决 J4 / J7）。</summary>
    public const int Gap = 3;

    /// <summary>画布留白：初始布局四周、拼接后东 / 北侧边界外的空格数。</summary>
    public const int Margin = 2;

    private static readonly ConcurrentDictionary<ulong, MapData> Cache = new();

    /// <summary>
    /// 穷举测试用的全部链接序列（规格「全部链接序列都能拼接」的三类，每步带贴边）：四人四区 15 种首轮配对（规范次序：含最小出生棋盘编号的一对先，裁决 R2）各接决赛；
    /// 2+1+1 的 30 条（独占对东贴后与同区棋盘北贴）；3+1 的 10 条（出生棋盘 ↔ 出生棋盘北贴）。生成期不再逐条核对——拼接由构造保证可行，可行性由测试穷举守住。
    /// </summary>
    public static ImmutableArray<LinkSequence> Sequences { get; } = ComputeSequences();

    /// <summary>按完整标识（<c>link:&lt;种子&gt;</c>）生成初始地图。</summary>
    public static MapData Generate(string mapId) => Generate(LinkMapId.Parse(mapId));

    /// <summary>生成初始地图。<see cref="MapData.Id"/> 为规范化标识，<see cref="MapData.MapVersion"/> 为 0。结果按种子缓存（地图不可变）。</summary>
    public static MapData Generate(ulong mapSeed) => Cache.GetOrAdd(mapSeed, static seed => Layout(seed).Map);

    /// <summary>生成初始地图并给出每块出生棋盘（按编号）的重抽次数（tasks 2.3 的分布统计用；不缓存）。</summary>
    internal static (MapData Map, ImmutableArray<int> Redraws) GenerateWithRedraws(ulong mapSeed) => Layout(mapSeed);

    /// <summary>
    /// 一次布局（link-map-terrain D3），全部随机数来自 <see cref="MapRandom.ForLinkLayout"/> 这一条流，顺序固定：
    /// 次序 → 宽高（6–8）→ 主题排列（Fisher–Yates）→ 按编号逐块生成地形（自检失败在同一条流上重抽该块）→ 出生区信物 → 公共信物。
    /// 布局最后过一遍链接档静态校验，不通过即生成器缺陷（抛 <see cref="MapGenerationException"/>）。
    /// </summary>
    private static (MapData Map, ImmutableArray<int> Redraws) Layout(ulong mapSeed)
    {
        string id = LinkMapId.Format(mapSeed);
        RandomStream rng = MapRandom.ForLinkLayout(mapSeed);

        // 排列次序（Fisher–Yates）：order[k] = 从西到东第 k 块是哪个出生棋盘编号。
        int[] order = [.. Enumerable.Range(0, BirthBoards)];
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // 各棋盘宽高按编号顺序抽取，再按次序摆放。
        var sizes = new (int W, int H)[BirthBoards];
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            sizes[zone] = (BirthMinSide + rng.NextInt(BirthMaxSide - BirthMinSide + 1), BirthMinSide + rng.NextInt(BirthMaxSide - BirthMinSide + 1));
        }

        // 主题排列（Fisher–Yates，在宽高之后）：themes[z] = 出生棋盘 z 的主题，5 块两两不同（裁决 M2）。
        BoardTheme[] themes = [BoardTheme.Plateau, BoardTheme.River, BoardTheme.Forest, BoardTheme.Palisade, BoardTheme.Mixed];
        for (int i = themes.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (themes[i], themes[j]) = (themes[j], themes[i]);
        }

        var plates = new BoardPlate[BirthBoards];
        int x = Margin;
        foreach (int zone in order)
        {
            (int w, int h) = sizes[zone];
            plates[zone] = new BoardPlate(new Coord(x, Margin), w, h, BoardPlateKind.Birth, themes[zone]);
            x += w + Gap;
        }

        // 按编号逐块生成地形（自检失败在同一条流上重抽该块，D3）。
        var boards = new BoardTerrain[BirthBoards];
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            boards[zone] = DraftBoard(plates[zone], rng);
        }

        // 出生区信物：按编号顺序每块 2 个，在核心区内；第二个与第一个不相邻，尽量不同行不同列（没有这样的格时退到"不相邻"）。
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            List<Coord> core = [.. plates[zone].CoreCells()];
            Coord first = core[rng.NextInt(core.Count)];
            List<Coord> apart = [.. core.Where(c => !Adjacency.AreAdjacent(c, first) && c != first)];
            List<Coord> preferred = [.. apart.Where(c => c.X != first.X && c.Y != first.Y)];
            List<Coord> candidates = preferred.Count > 0 ? preferred : apart;
            Coord second = candidates[rng.NextInt(candidates.Count)];
            relics[first] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
            relics[second] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
        }

        // 公共信物（D5 / 裁决 M8 / Q3）：按编号顺序，每块从主题的难到达格里取 1 个，档位 (Contested, Standard)；没有难到达格的这块放 0 个。
        for (int zone = 0; zone < BirthBoards; zone++)
        {
            ImmutableArray<Coord> hard = boards[zone].HardCells;
            if (hard.Length > 0)
            {
                relics[hard[rng.NextInt(hard.Length)]] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
            }
        }

        TerrainData terrain = new(
            boards.SelectMany(b => b.Heights).ToImmutableDictionary(),
            boards.SelectMany(b => b.Surfaces).ToImmutableDictionary(),
            [.. boards.SelectMany(b => b.Bridges)],
            [.. boards.SelectMany(b => b.Fences)]);
        ImmutableHashSet<Coord> rocks = [.. boards.SelectMany(b => b.Rocks)];
        MapData map = Assemble(id, [.. plates], relics.ToImmutable(), terrain, rocks, ImmutableHashSet<Coord>.Empty, mapVersion: 0);
        MapValidationResult validation = MapValidator.Validate(map);
        if (!validation.IsValid)
        {
            throw new MapGenerationException($"地图 {id} 的初始布局未通过链接档静态校验（生成器缺陷）：{string.Join("；", validation.Failures)}");
        }

        return (map, [.. boards.Select(b => b.Redraws)]);
    }

    /// <summary>
    /// 把棋盘清单灌成链接档地图数据：画布 = 全部棋盘外接矩形的东 / 北边界各外扩 <see cref="Margin"/> 格（原点固定在 (0,0)），棋盘之外与棋盘内的岩石一律障碍，
    /// 出生区 = 出生棋盘的核心区（link-map-terrain D1），中央入口取第一块棋盘的中心格（落在核心区内），没有咽喉。
    /// </summary>
    private static MapData Assemble(
        string id,
        ImmutableArray<BoardPlate> plates,
        ImmutableDictionary<Coord, RelicCellSpec> relics,
        TerrainData terrain,
        ImmutableHashSet<Coord> rocks,
        ImmutableHashSet<Coord> chokePoints,
        int mapVersion)
    {
        int width = plates.Max(p => p.Origin.X + p.Width) + Margin;
        int height = plates.Max(p => p.Origin.Y + p.Height) + Margin;
        var playable = new HashSet<Coord>();
        foreach (BoardPlate plate in plates)
        {
            playable.UnionWith(plate.Cells());
        }

        playable.ExceptWith(rocks);
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
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
            Width = width,
            Height = height,
            MaxPlayers = 4,
            Profile = MapProfile.Link,
            Obstacles = obstacles.ToImmutable(),
            TerrainData = terrain,
            BirthZones = [.. plates.Where(p => p.Kind == BoardPlateKind.Birth).Select(p => (ImmutableHashSet<Coord>)[.. p.CoreCells()])],
            Boards = plates,
            RelicCells = relics,
            ChokePoints = chokePoints,
            CentralEntrance = new Coord(first.Origin.X + (first.Width / 2), first.Origin.Y + (first.Height / 2)),
            MapVersion = mapVersion,
        };
    }

    // ---------- 拼接 ----------

    /// <summary>
    /// 在当前物化状态上执行一次拼接：把战场 <paramref name="battlefieldX"/> 与 <paramref name="battlefieldY"/>（标识 = 分量内最小出生棋盘编号）边对边紧贴拼成一块，
    /// 被贴方贴到锚点（标识较小者）的 <paramref name="side"/> 侧；产出版本号 +1 的新地图与各战场平移向量。几何只由 (X, Y, 当前布局) 确定，不消费随机。
    /// 新地图通过链接档静态校验后才返回，不通过抛 <see cref="MapValidationException"/>（生成器缺陷，MUST NOT 静默继续）。
    /// 非链接档地图抛 <see cref="SiegeRuleException"/>（该规格档不允许增长）；标识不存在或相同抛 <see cref="ArgumentException"/>；
    /// 最长共享边的候选全部会让被贴方越出画布原点时整个布局整体平移（裁决 J8-3，见类注释），不抛异常。
    /// </summary>
    public static LinkResult Link(MapData current, int battlefieldX, int battlefieldY, LinkSide side)
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
        int anchorId = Math.Min(battlefieldX, battlefieldY);
        int moverId = Math.Max(battlefieldX, battlefieldY);
        Battlefield anchor = fields.ById(anchorId)
            ?? throw new ArgumentException($"地图 {current.Id}（版本 {current.MapVersion}）上没有标识为 {anchorId} 的战场。", nameof(battlefieldX));
        Battlefield mover = fields.ById(moverId)
            ?? throw new ArgumentException($"地图 {current.Id}（版本 {current.MapVersion}）上没有标识为 {moverId} 的战场。", nameof(battlefieldY));

        var anchorCells = new HashSet<Coord>(anchor.Cells);
        var moverCells = new HashSet<Coord>(mover.Cells);
        Extent a = Extent.Of(anchor.Cells);
        Extent b = Extent.Of(mover.Cells);

        // 被贴方的向量：贴边分量由外接矩形定死，另一分量在候选里取共享边最长者；并列时先排除会让被贴方越出画布原点（负坐标）的候选，
        // 再取偏南 / 偏西（候选按升序枚举）；最长者全部越界时取其中偏南 / 偏西者，随后整个布局整体平移（裁决 J8-3）。
        var counts = new List<(int Candidate, int Count)>();
        int dx = 0;
        int dy = 0;
        if (side == LinkSide.East)
        {
            dx = a.MaxX + 1 - b.MinX;
            for (int candidate = a.MinY - b.MaxY; candidate <= a.MaxY - b.MinY; candidate++)
            {
                int count = 0;
                for (int r = Math.Max(a.MinY, b.MinY + candidate); r <= Math.Min(a.MaxY, b.MaxY + candidate); r++)
                {
                    if (anchorCells.Contains(new Coord(a.MaxX, r)) && moverCells.Contains(new Coord(b.MinX, r - candidate)))
                    {
                        count++;
                    }
                }

                counts.Add((candidate, count));
            }
        }
        else
        {
            dy = a.MaxY + 1 - b.MinY;
            for (int candidate = a.MinX - b.MaxX; candidate <= a.MaxX - b.MinX; candidate++)
            {
                int count = 0;
                for (int c = Math.Max(a.MinX, b.MinX + candidate); c <= Math.Min(a.MaxX, b.MaxX + candidate); c++)
                {
                    if (anchorCells.Contains(new Coord(c, a.MaxY)) && moverCells.Contains(new Coord(c - candidate, b.MinY)))
                    {
                        count++;
                    }
                }

                counts.Add((candidate, count));
            }
        }

        int shared = counts.Max(c => c.Count);
        if (shared < 1)
        {
            throw new MapGenerationException(
                $"地图 {current.Id}（版本 {current.MapVersion}）上战场 {BirthZoneLabel.Of(anchorId)} 与 {BirthZoneLabel.Of(moverId)} 沿{SideName(side)}侧拼接找不到共享边（生成器缺陷）。");
        }

        // 被贴方另一分量的最小坐标：东贴看最南行、北贴看最西列。
        int lowest = side == LinkSide.East ? b.MinY : b.MinX;
        (int Candidate, int Count)[] longest = [.. counts.Where(c => c.Count == shared)];
        int pick = longest.FirstOrDefault(c => lowest + c.Candidate >= 0, longest[0]).Candidate;
        if (side == LinkSide.East)
        {
            dy = pick;
        }
        else
        {
            dx = pick;
        }

        // 平移表（整体平移之前）：锚点零向量、被贴方 (dx, dy)；未参与链接的战场里，外接矩形整体位于合并战场以西（与之至少隔一列空列，不相邻）的原地不动，
        // 其余（以东、与合并战场横向交叠或紧贴）按当前最西列升序收拢排到合并战场东侧，间距 Gap，底边不动（裁决 J7 / J8-1）。
        var shifts = new SortedDictionary<int, (int Dx, int Dy)> { [anchorId] = (0, 0), [moverId] = (dx, dy) };
        int mergedMinX = Math.Min(a.MinX, b.MinX + dx);
        int mergedMaxX = Math.Max(a.MaxX, b.MaxX + dx);
        int cursor = mergedMaxX + 1 + Gap;
        foreach (Battlefield other in fields.All.Where(f => f.Id != anchorId && f.Id != moverId).OrderBy(f => f.Cells.Min(c => c.X)).ThenBy(f => f.Id))
        {
            Extent e = Extent.Of(other.Cells);
            if (e.MaxX + 1 < mergedMinX)
            {
                shifts[other.Id] = (0, 0);
                continue;
            }

            shifts[other.Id] = (cursor - e.MinX, 0);
            cursor += e.MaxX - e.MinX + 1 + Gap;
        }

        // 被贴方越出画布原点（最长共享边的候选全部越界）：整个布局（含锚点）整体向东 / 北平移到恰好不越界，锚点向量一并记录（裁决 J8-3）。
        int globalX = Math.Max(0, -(b.MinX + dx));
        int globalY = Math.Max(0, -(b.MinY + dy));
        ImmutableArray<BattlefieldShift> shiftList = [.. shifts.Select(kv => new BattlefieldShift(kv.Key, kv.Value.Dx + globalX, kv.Value.Dy + globalY))];
        foreach (BattlefieldShift s in shiftList)
        {
            shifts[s.Battlefield] = (s.Dx, s.Dy);
        }

        // 同一份映射搬全部格（link-map-terrain：含棋盘内的岩石与未架桥深水）：按格所在的棋盘平移，棋盘的向量取它所属战场（棋盘内任一可落子格所在的分量，D7）的向量。
        ImmutableArray<(int Dx, int Dy)> plateShifts = PlateShifts(current, fields, shifts);
        Coord Move(Coord c) => MoveByPlate(current.Boards, plateShifts, c)
            ?? throw new MapGenerationException($"地图 {current.Id}（版本 {current.MapVersion}）上的 {c.ToNotation()} 不在任何棋盘内：链接档地图的地形、信物与咽喉都必须在棋盘内，拼接无法迁移它。");

        // 新地图：棋盘清单、出生区、信物格、地形（高度 / 地表 / 桥 / 栅栏）、岩石、咽喉按同一映射逐项迁移；画布按东 / 北边界重裁。
        // 栅栏两端在同一块棋盘内（外圈相关的边不得有栅栏，见校验器），两端的向量相同，边仍几何相邻。
        ImmutableArray<BoardPlate> plates = [.. current.Boards.Select((p, i) => p with { Origin = new Coord(p.Origin.X + plateShifts[i].Dx, p.Origin.Y + plateShifts[i].Dy) })];
        ImmutableDictionary<Coord, RelicCellSpec> relics = current.RelicCells.ToImmutableDictionary(kv => Move(kv.Key), kv => kv.Value);
        TerrainData before = current.TerrainData;
        TerrainData terrain = new(
            before.Heights.ToImmutableDictionary(kv => Move(kv.Key), kv => kv.Value),
            before.Surfaces.ToImmutableDictionary(kv => Move(kv.Key), kv => kv.Value),
            [.. before.Bridges.Select(Move)],
            [.. before.Fences.Select(e => new FenceEdge(Move(e.A), Move(e.B)))]);
        ImmutableHashSet<Coord> rocks = [.. current.Obstacles.Where(c => current.Boards.Any(p => p.Contains(c))).Select(Move)];

        MapData spliced = Assemble(current.Id, plates, relics, terrain, rocks, [.. current.ChokePoints.Select(Move)], current.MapVersion + 1) with
        {
            MaxPlayers = current.MaxPlayers,
            CentralEntrance = current.IsPlayable(current.CentralEntrance) ? Move(current.CentralEntrance) : current.CentralEntrance,
            DistanceTolerance = current.DistanceTolerance,
            ToleranceRelaxReason = current.ToleranceRelaxReason,
            MinTwoEyeArea = current.MinTwoEyeArea,
            PocketExemptions = [.. current.PocketExemptions.Where(current.IsPlayable).Select(Move)],
            PocketExemptionReasons = current.PocketExemptionReasons.Where(kv => current.IsPlayable(kv.Key)).ToImmutableDictionary(kv => Move(kv.Key), kv => kv.Value),
        };

        MapValidationResult validation = MapValidator.Validate(spliced);
        if (!validation.IsValid)
        {
            throw new MapValidationException(validation);
        }

        string others = string.Join("、", shiftList.Where(s => s.Battlefield != anchorId && s.Battlefield != moverId && !s.IsZero));
        string summary =
            $"拼接 {BirthZoneLabel.Of(anchorId)}↔{BirthZoneLabel.Of(moverId)}：战场 {BirthZoneLabel.Of(moverId)} 平移 ({dx:+#;-#;0},{dy:+#;-#;0}) 贴到战场 {BirthZoneLabel.Of(anchorId)} {SideName(side)}侧，"
            + $"共享边 {shared} 格；合并后 {anchor.Cells.Length + mover.Cells.Length} 格"
            + (others.Length > 0 ? $"；其余战场平移 {others}" : string.Empty)
            + $"；画布 {spliced.Width}×{spliced.Height}";
        return new LinkResult(spliced, fields, current.Boards, plateShifts, side, anchorId, moverId, shared, shiftList, summary);
    }

    /// <summary>每块棋盘（按清单下标）的平移向量：取它所属战场（<see cref="Battlefield.Boards"/>）的向量；不属于任何战场的棋盘（棋盘内没有可落子格，只可能来自手写夹具）抛出。</summary>
    private static ImmutableArray<(int Dx, int Dy)> PlateShifts(MapData current, Battlefields fields, IReadOnlyDictionary<int, (int Dx, int Dy)> shifts) =>
        [.. current.Boards.Select((p, i) => fields.All.FirstOrDefault(f => f.Boards.Contains(i)) is { } field
            ? shifts[field.Id]
            : throw new MapGenerationException($"地图 {current.Id}（版本 {current.MapVersion}）的棋盘 {p.Origin.ToNotation()} 不属于任何战场，拼接无法确定它的平移向量。"))];

    /// <summary>按格所在的棋盘平移；不在任何棋盘内为 <c>null</c>。</summary>
    internal static Coord? MoveByPlate(ImmutableArray<BoardPlate> plates, ImmutableArray<(int Dx, int Dy)> plateShifts, Coord c)
    {
        for (int i = 0; i < plates.Length; i++)
        {
            if (plates[i].Contains(c))
            {
                return new Coord(c.X + plateShifts[i].Dx, c.Y + plateShifts[i].Dy);
            }
        }

        return null;
    }

    private static string SideName(LinkSide side) => side == LinkSide.East ? "东" : "北";

    /// <summary>一组格的外接矩形（闭区间）。</summary>
    private readonly record struct Extent(int MinX, int MaxX, int MinY, int MaxY)
    {
        internal static Extent Of(ImmutableArray<Coord> cells) =>
            new(cells.Min(c => c.X), cells.Max(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.Y));
    }

    // ---------- 链接序列清单 ----------

    private static ImmutableArray<LinkSequence> ComputeSequences()
    {
        var list = ImmutableArray.CreateBuilder<LinkSequence>();

        // 四人四区：选 4 块棋盘（5 种），三种配对；首轮两次东贴按规范次序（含最小出生棋盘编号的一对先），再决赛北贴（两个合并战场的标识 = 各自最小编号）。
        foreach (int[] four in Combinations(BirthBoards, 4))
        {
            int a = four[0], b = four[1], c = four[2], d = four[3];
            foreach (((int, int) p, (int, int) q) in new[] { ((a, b), (c, d)), ((a, c), (b, d)), ((a, d), (b, c)) })
            {
                list.Add(new LinkSequence(LinkSequenceKind.FourZones,
                [
                    new LinkStep(p.Item1, p.Item2, LinkSide.East),
                    new LinkStep(q.Item1, q.Item2, LinkSide.East),
                    new LinkStep(Math.Min(p.Item1, p.Item2), Math.Min(q.Item1, q.Item2), LinkSide.North),
                ]));
            }
        }

        // 2+1+1：两名独占者（a, b）配对东贴，再与同区棋盘 c 决赛北贴。
        foreach (int[] two in Combinations(BirthBoards, 2))
        {
            for (int c = 0; c < BirthBoards; c++)
            {
                if (c != two[0] && c != two[1])
                {
                    list.Add(new LinkSequence(LinkSequenceKind.TwoPlusOnePlusOne, [new LinkStep(two[0], two[1], LinkSide.East), new LinkStep(two[0], c, LinkSide.North)]));
                }
            }
        }

        // 3+1：独占者与三人同区棋盘的胜者直接决赛北贴。
        foreach (int[] two in Combinations(BirthBoards, 2))
        {
            list.Add(new LinkSequence(LinkSequenceKind.ThreePlusOne, [new LinkStep(two[0], two[1], LinkSide.North)]));
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
}
