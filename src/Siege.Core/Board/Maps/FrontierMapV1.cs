using System.Collections.Immutable;

namespace Siege.Core.Board.Maps;

/// <summary>
/// 4 人边疆档验证地图 v1（<c>siege-frontier-v1</c>）：25 列 × 30 行的竖长图，6 个大小不一的 h=2 平台（平台 ≡ 出生区），
/// 平台之间由 h=0 过渡带连成一片，一条南北向主河在中央广场处断开。验证版（frontier-map 裁决 1）：不是任何入口的缺省地图。
/// </summary>
/// <remarks>
/// <para><b>平台编号 → 边长 → 方位</b>（编号即面向人的出生区编号，<see cref="MapData.BirthZones"/> 下标 = 编号 − 1）：</para>
/// <list type="bullet">
/// <item>1 号 → 9×9 → 西北外缘（<c>A21</c> 起）；缓坡：东 <c>K21 K22</c>、南 <c>F20 G20</c>。</item>
/// <item>2 号 → 8×8 → 东南外缘（<c>R2</c> 起）；缓坡：西 <c>Q8 Q9</c>、北 <c>S10 T10</c>。</item>
/// <item>3 号 → 7×7 → 东北（<c>R22</c> 起）；缓坡：西 <c>Q22 Q23</c>、南 <c>U21 V21</c>。</item>
/// <item>4 号 → 6×6 → 西南（<c>D3</c> 起）；缓坡：东 <c>K7 K8</c>、北 <c>F9 G9</c>。</item>
/// <item>5 号 → 5×5 → 中西，紧贴中央广场（<c>E13</c> 起）；缓坡：东 <c>K14–K16</c>（直通广场）、北 <c>F18 G18</c>。</item>
/// <item>6 号 → 5×5 → 中东，紧贴中央广场（<c>R13</c> 起）；缓坡：西 <c>Q14–Q16</c>（直通广场）、南 <c>S12 T12</c>。</item>
/// </list>
/// <para><b>大小即取舍</b>（裁决 2）：两个 5×5 出门三格就是广场（到中央入口 4 步，石碑就在缓坡口），但只有 25 格腾挪、且朝广场的缓坡开了 3 格宽；
/// 大平台空间足，到中央入口要 10–11 步。没人选的平台是中立争夺区，其上的信物格仍按出生区分区取预算（裁决 9）。</para>
/// <para><b>地貌。</b>主河在 <c>N</c> 列：北段 <c>N18–N30</c>、南段 <c>N1–N12</c>，各自流到地图边缘，中间被中央广场（<c>L13–P17</c>，h=0）断开。
/// 河两岸各一条 2 格宽的河岸带（西岸 <c>L M</c> 列、东岸 <c>O P</c> 列），四座预置桥 <c>N22 N19</c>（北段）、<c>N11 N8</c>（南段），
/// 桥头各一枚标准档公共信物（<c>M22 O19 O11 M8</c>，东西交错）。四条东西向的支巷把平台的第二处缓坡接到河岸：
/// 西北巷（第 19–20 行，1 号与 5 号之间）、西南巷（第 10–11 行，4 号北侧）、东南巷（第 10–11 行，6 号与 2 号之间）、东北巷（第 20–21 行，3 号南侧）。
/// 平台四周除缓坡外全是崖壁（与 h=0 直接相邻，高差 2）或岩石；一格宽的主河可隔岸覆盖。其余用岩石（内陆）与深水（外海）填满。</para>
/// <para><b>中央广场。</b>5×5，西北与东南两角是林地、东北与西南两角是岩石；中心 <c>N15</c> 是中央入口兼高档公共信物。
/// 四块石碑按风车形摆在外圈（<c>M17 P16 O13 L14</c>）：没有一块与中心相邻，一枚棋子覆盖不了两块。
/// 东、西两块就在 6 号、5 号的缓坡口；北、南两块各被一段栅栏（<c>M17-M18</c>、<c>O12-O13</c>）挡在河岸带一侧——
/// 隔栏可覆盖但走不进去，北面只能绕林地角 <c>L17</c> 或走东岸 <c>O18</c> 进广场，南面同理。</para>
/// <para><b>据点 10。</b>平台内没有据点（得分点全在平台外，鼓励出门争抢）。篝火 6：四个大 / 中平台通向河岸的缓坡口各 1（<c>L21 P23 L7 P8</c>），
/// 西北巷尽头 <c>E19</c>、东南巷尽头 <c>T11</c> 各 1。石碑 4：见上。均不与信物格重合。</para>
/// <para><b>信物 16。</b>平台内 9（边长 5–6 的各 1、7–9 的各 2，全部出生区分区 / 出生区预算）；公共 7 = 桥头 4 + 西南巷尽头 <c>E10</c> + 东北巷尽头 <c>W20</c>（标准档）+ 中心 <c>N15</c>（高档）。</para>
/// <para><b>栅栏 4。</b>广场两段见上；另两段在支巷入河岸的交汇口各挡住一半宽度：<c>K10-L10</c>（西南巷）、<c>P20-Q20</c>（东北巷）。</para>
/// <para><b>咽喉</b>按 design D3 取全部缓坡格与四座桥，由字符画导出，不另列表。</para>
/// <para><b>本图的校验参数与豁免记录：</b>边疆档距离均衡只报告不拒绝（裁决 8），容差留默认值；两眼最小格数 = 8（默认值）；必死口袋豁免：<b>无</b>——
/// 全部可落子格沿气边连通成一块。不要求对称；确定性：字符画是唯一的数据来源，同一代码永远导出同一文件。</para>
/// <para>规格：openspec/changes/frontier-map/specs/map-definition —— Requirement: 边疆档基准地图</para>
/// </remarks>
public static class FrontierMapV1
{
    /// <summary>地图标识。</summary>
    public const string Id = "siege-frontier-v1";

    private const int Columns = 25;

    /// <summary>
    /// 地形与布点的字符画：上北下南，<b>最后一行是第 1 行</b>（<c>A1</c> 在左下），每行恰 25 个字符，列依次为 <c>A–Z</c>（跳 <c>I</c>）。
    /// <list type="bullet">
    /// <item><c>1</c>–<c>6</c> 平台格（h=2，数字是平台编号）；<c>r</c> 平台内的信物格——所属平台由 <see cref="Platforms"/> 的外接方块决定。平台内没有据点（map-generator 裁决 18：不放营帐）。</item>
    /// <item><c>,</c> 缓坡（h=1）；<c>.</c> 过渡带 / 广场（h=0）；<c>F</c> 林地（h=0）。</item>
    /// <item><c>C</c> 篝火；<c>S</c> 石碑；<c>o</c> 标准档公共信物；<c>R</c> 高档公共信物兼中央入口——都在 h=0 草地上。</item>
    /// <item><c>~</c> 深水；<c>=</c> 预置桥（深水上，h=0）；<c>#</c> 岩石。</item>
    /// </list>
    /// 栅栏在格与格之间，字符画画不了，见 <see cref="Fences"/>。
    /// </summary>
    private static readonly string[] Art =
    [
       //A...E....K....P....U....Z   列标尺（每 5 列一个字母；完整的列字母表只在 Coord 里，见 coordinates.md「映射唯一」）
        "~~~~~~~~~~~~~~~~~~~~~~~~~", // 30
        "111111111###~###~~~~~~~~~", // 29
        "1r1111111###~###3333333~~", // 28
        "111111111###~###3r33333~~", // 27
        "111111111###~###3333333~~", // 26
        "111111111###~###3333333~~", // 25
        "111111111###~###3333333~~", // 24
        "111111r11###~.C,33333r3~~", // 23
        "111111111,.o=..,3333333~~", // 22
        "111111111,C.~...F.#,,##~~", // 21
        "~####,,.....~........o#~~", // 20
        "~###C.......=o.########~~", // 19
        "~####,,###..~..########~~", // 18
        "~###55555#FS..##66666##~~", // 17
        "~###5r555,....S,66666##~~", // 16
        "~###55555,..R..,66666##~~", // 15
        "~###55555,S....,666r6##~~", // 14
        "~###55555##..SF#66666##~~", // 13
        "~#########..~..##,,####~~", // 12
        "~######F....=o....C###~~~", // 11
        "~###o.......~....,,####~~", // 10
        "~~###,,###..~..,22222222~", //  9
        "~~#444444,.o=.C,22222r22~", //  8
        "~~#444444,C.~###22222222~", //  7
        "~~#444444###~###22222222~", //  6
        "~~#44r444###~###22222222~", //  5
        "~~#444444###~###2r222222~", //  4
        "~~#444444###~###22222222~", //  3
        "~~##########~###22222222~", //  2
        "~~~~~~~~~~~~~~~~~~~~~~~~~", //  1
    ];

    /// <summary>平台的外接方块：编号、西南角、边长。字符画里方块内只许出现本平台的数字与 <c>r</c>——平台留白，方块内不许有岩石或别的障碍，也不放营帐（map-generator 裁决 17 / 18）；方块外不许出现平台格。</summary>
    private static readonly (int Number, string SouthWest, int Side)[] Platforms =
    [
        (1, "A21", 9),
        (2, "R2", 8),
        (3, "R22", 7),
        (4, "D3", 6),
        (5, "E13", 5),
        (6, "R13", 5),
    ];

    /// <summary>栅栏（挡气不挡覆盖）：广场北、南两块石碑朝河岸带的一侧，以及西南巷、东北巷入河岸的交汇口各一段。</summary>
    private static readonly (string A, string B)[] Fences =
    [
        ("M17", "M18"),
        ("O12", "O13"),
        ("K10", "L10"),
        ("P20", "Q20"),
    ];

    /// <summary>构建边疆档验证地图。字符画有任何笔误（行宽、未知字符、平台格越出方块、方块内混进障碍、缺中央入口）都当场抛出，不产出半张图。</summary>
    public static MapData Create()
    {
        int rows = Art.Length;
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        var heights = ImmutableDictionary.CreateBuilder<Coord, int>();
        var surfaces = ImmutableDictionary.CreateBuilder<Coord, Surface>();
        var bridges = ImmutableHashSet.CreateBuilder<Coord>();
        var chokes = ImmutableHashSet.CreateBuilder<Coord>();
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var sites = ImmutableDictionary.CreateBuilder<Coord, SiteTier>();
        ImmutableHashSet<Coord>.Builder[] zones = [.. Platforms.Select(_ => ImmutableHashSet.CreateBuilder<Coord>())];
        Coord? entrance = null;

        for (int i = 0; i < rows; i++)
        {
            string line = Art[i];
            int y = rows - 1 - i;
            if (line.Length != Columns)
            {
                throw new InvalidOperationException($"{Id} 字符画第 {y + 1} 行有 {line.Length} 个字符，应为 {Columns}。");
            }

            for (int x = 0; x < Columns; x++)
            {
                var c = new Coord(x, y);
                char ch = line[x];
                int? platform = PlatformAt(c);
                bool platformGlyph = ch is (>= '1' and <= '6') or 'r';
                if (platformGlyph != platform.HasValue)
                {
                    throw new InvalidOperationException($"{Id} 字符画 {c} 的 '{ch}' 与平台外接方块不符。");
                }

                if (platform is { } number && platformGlyph)
                {
                    if (char.IsAsciiDigit(ch) && ch - '0' != number)
                    {
                        throw new InvalidOperationException($"{Id} 字符画 {c} 写的是平台 {ch}，却落在平台 {number} 的方块里。");
                    }

                    zones[number - 1].Add(c);
                    heights[c] = 2;
                }

                switch (ch)
                {
                    case >= '1' and <= '6':
                    case '.':
                        break;
                    case 'r':
                        relics[c] = new RelicCellSpec(RelicZone.BirthZone, BudgetTier.Birth);
                        break;
                    case ',':
                        heights[c] = 1;
                        chokes.Add(c);
                        break;
                    case 'F':
                        surfaces[c] = Surface.Forest;
                        break;
                    case 'C':
                        sites[c] = SiteTier.Campfire;
                        break;
                    case 'S':
                        sites[c] = SiteTier.Stele;
                        break;
                    case 'o':
                        relics[c] = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
                        break;
                    case 'R':
                        relics[c] = new RelicCellSpec(RelicZone.Contested, BudgetTier.High);
                        entrance = entrance is null ? c : throw new InvalidOperationException($"{Id} 字符画里有两个中央入口 R。");
                        break;
                    case '~':
                        surfaces[c] = Surface.DeepWater;
                        break;
                    case '=':
                        surfaces[c] = Surface.DeepWater;
                        bridges.Add(c);
                        chokes.Add(c);
                        break;
                    case '#':
                        obstacles.Add(c);
                        break;
                    default:
                        throw new InvalidOperationException($"{Id} 字符画 {c} 是未知字符 '{ch}'。");
                }
            }
        }

        ImmutableHashSet<FenceEdge> fences = Fences
            .Select(f => new FenceEdge(Coord.Parse(f.A), Coord.Parse(f.B)))
            .ToImmutableHashSet();

        return new MapData
        {
            Id = Id,
            Width = Columns,
            Height = rows,
            MaxPlayers = 4,
            Profile = MapProfile.Frontier,
            Obstacles = obstacles.ToImmutable(),
            TerrainData = new TerrainData(heights.ToImmutable(), surfaces.ToImmutable(), bridges.ToImmutable(), fences),
            BirthZones = [.. zones.Select(z => z.ToImmutable())],
            RelicCells = relics.ToImmutable(),
            Sites = sites.ToImmutable(),
            ChokePoints = chokes.ToImmutable(),
            CentralEntrance = entrance ?? throw new InvalidOperationException($"{Id} 字符画里没有中央入口 R。"),
        };
    }

    /// <summary>该格落在哪个平台的外接方块里（平台编号，1 起）；不在任何方块里为 <c>null</c>。</summary>
    private static int? PlatformAt(Coord c)
    {
        foreach ((int number, string southWest, int side) in Platforms)
        {
            Coord origin = Coord.Parse(southWest);
            if (c.X >= origin.X && c.X < origin.X + side && c.Y >= origin.Y && c.Y < origin.Y + side)
            {
                return number;
            }
        }

        return null;
    }
}
