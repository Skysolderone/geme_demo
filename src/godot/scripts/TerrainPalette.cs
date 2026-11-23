using Godot;

namespace Siege.Godot;

/// <summary>
/// 地块 + 浮岛色板（chibi-world 段 ②，裁决 5：放弃粉彩，统一为单位那套饱和配色）。七种地表、侧面、河岸、浮岛岩色、云、瀑布的颜色集中在这一张表，
/// 段 ② 负责人看图选定 A（2026-11-21）；调试参数 <c>--palette=A</c> 保留，套名不认识即报错。
/// </summary>
/// <remarks>
/// <para>七种地表底色按 Rec.601 灰度排成七档（沼泽 68 / 林地 86 / 岩台 97 / 土路 121 / 草地 155 / 浅滩 181 / 荒漠 212；亮端档距放宽，抵消卡通分阶顶面全亮与色调映射在亮端的压缩），去色后两两可分；
/// 草地三片（<see cref="GrassPatchA"/> 等）只差色相、灰度同档（map-surface 裁决 3 的口径不变）。
/// 段 ② 裁决 5：土路、岩台、草地第 1 / 2 片避开金、紫、蓝三方的色相，四阵营独占着色叠在任一地表上，色块中心前后 RGB 距离都 ≥ 45（实测最小 46.6，金方叠土路）。</para>
/// <para>取值只在这里；<see cref="Visuals"/> 的地表色、<see cref="TerrainParts"/> 的地块内置材质、<see cref="BoardView"/> 的浮岛材质都读 <see cref="Current"/>。
/// 须在建局前（GameRoot 解析命令行时）定下；之后不再切换。</para>
/// </remarks>
public sealed record TerrainPalette
{
    /// <summary>套名（现只有 A）。</summary>
    public required string Name { get; init; }

    /// <summary>一句话设计意图（启动日志与 README 用）。</summary>
    public required string Intent { get; init; }

    /// <summary>草地第 0 片（岛面主色，原"淡紫石"那一片）。</summary>
    public required Color GrassPatchA { get; init; }

    /// <summary>草地第 1 片（原"淡草绿"）：与第 0 片同灰度、色相偏黄。</summary>
    public required Color GrassPatchB { get; init; }

    /// <summary>草地第 2 片（原"淡泥褐"）：与第 0 片同灰度、色相再偏土。</summary>
    public required Color GrassPatchC { get; init; }

    /// <summary>土路。</summary>
    public required Color Road { get; init; }

    /// <summary>林地。</summary>
    public required Color Forest { get; init; }

    /// <summary>荒漠。</summary>
    public required Color Desert { get; init; }

    /// <summary>沼泽。</summary>
    public required Color Marsh { get; init; }

    /// <summary>岩台。</summary>
    public required Color Crag { get; init; }

    /// <summary>浅滩。</summary>
    public required Color Shallows { get; init; }

    /// <summary>
    /// 岩石（terrain-bonus 段 B 裁决 5：可落子的岩石地表，地砖底色 + 平板岩片 glb 的石面槽都取它）。四阵营独占着色叠上去前后的渲染距离须 ≥ 45；
    /// 追加裁决 Q2：渲染灰度与相邻地表（土路、草地三片、沼泽，及 h=2 提亮后的同档地表）差 ≥ 17——取暗色玄武岩，排在灰度最暗一档（沼泽之下）。
    /// 实测见色块图 art/terrain-bonus-b/palette-rock.png：灰度 68.3（h=2 119.7），色差最小 109.0（h=2 73.2）。
    /// </summary>
    public required Color Rock { get; init; }

    /// <summary>岩石平板的深色石缝 / 碎石（比 <see cref="Rock"/> 深一档）。</summary>
    public required Color RockShade { get; init; }

    /// <summary>深水水体。</summary>
    public required Color DeepWater { get; init; }

    /// <summary>障碍格 / 场景格的地面。</summary>
    public required Color Obstacle { get; init; }

    /// <summary>h=1 缓坡侧面（土）。</summary>
    public required Color SlopeSide { get; init; }

    /// <summary>h=2 崖壁侧面（岩）。</summary>
    public required Color CliffSide { get; init; }

    /// <summary>高台面砖的提亮色：h=1 面砖向它拉 <see cref="HighWashLow"/>，h=2 拉 <see cref="HighWashHigh"/>（改前是向淡紫白拉 30%）。</summary>
    public required Color HighWash { get; init; }

    /// <summary>h=1 面砖向 <see cref="HighWash"/> 拉的比例。</summary>
    public float HighWashLow { get; init; } = 0.07f;

    /// <summary>h=2 面砖向 <see cref="HighWash"/> 拉的比例。</summary>
    public float HighWashHigh { get; init; } = 0.16f;

    /// <summary>河岸泡沫带。</summary>
    public required Color BankFoam { get; init; }

    /// <summary>底座外圈石沿（大图已撤，小图档保留）。</summary>
    public required Color RimStone { get; init; }

    /// <summary>浮岛岛面（压在地砖之下，只在岛缘露一线）。</summary>
    public required Color IslandGround { get; init; }

    /// <summary>浮岛阶梯断崖（顶点色色带乘在它上面）。</summary>
    public required Color IslandCliff { get; init; }

    /// <summary>浮岛根须。</summary>
    public required Color IslandRoot { get; init; }

    /// <summary>浮岛三档岩色：0 土层、1 岩层、2 深岩（垂岩 0 档用深岩、其余用岩层）。</summary>
    public required Color[] IslandTiers { get; init; }

    /// <summary>云团（α 即不透明度）。</summary>
    public required Color Cloud { get; init; }

    /// <summary>瀑布水帘主色（着色器 <c>water</c>，α 即不透明度）。</summary>
    public required Color Waterfall { get; init; }

    /// <summary>
    /// 装饰槽颜色的覆盖（段 ① 裁决 2：与该套色板明显打架时一并微调，只改常量、不重新生成 glb）：键为槽名（Stone / Shade …），只覆盖按槽取的缺省色，
    /// 种类专属色（砂岩、松针等）不动。空表即装饰与段 ① 定稿一致。
    /// </summary>
    public IReadOnlyDictionary<string, Color> ChibiSlots { get; init; } = new Dictionary<string, Color>();

    /// <summary>A：明亮饱和——与单位同一套高饱和取向，草绿 / 赭红土路 / 灰绿岩台 / 冷石板蓝崖，最接近单位参考图。</summary>
    public static readonly TerrainPalette A = new()
    {
        Name = "A",
        Intent = "明亮饱和：与单位同一套高饱和取向，草绿、赭红土路、灰绿岩台、冷石板蓝崖、褐土浮岛，最接近单位参考图",
        GrassPatchA = Color.Color8(110, 194, 73),
        GrassPatchB = Color.Color8(84, 190, 104),
        GrassPatchC = Color.Color8(112, 194, 84),
        Road = Color.Color8(170, 102, 92),
        Forest = Color.Color8(34, 118, 62),
        Desert = Color.Color8(246, 212, 119),
        Marsh = Color.Color8(39, 82, 68),
        Crag = Color.Color8(92, 100, 94),
        Shallows = Color.Color8(107, 212, 216),
        Rock = Color.Color8(50, 44, 40),
        RockShade = Color.Color8(30, 26, 24),
        DeepWater = Color.Color8(32, 133, 199),
        Obstacle = Color.Color8(114, 115, 139),
        SlopeSide = Color.Color8(176, 112, 62),
        CliffSide = Color.Color8(108, 120, 164),
        HighWash = Color.Color8(255, 250, 232),
        BankFoam = Color.Color8(236, 250, 255),
        RimStone = Color.Color8(200, 186, 170),
        IslandGround = Color.Color8(166, 110, 68),
        IslandCliff = Color.Color8(160, 104, 64),
        IslandRoot = Color.Color8(112, 70, 44),
        IslandTiers = [Color.Color8(160, 104, 64), Color.Color8(112, 108, 140), Color.Color8(72, 66, 96)],
        Cloud = new Color(1f, 1f, 1f, 0.9f),
        Waterfall = new Color(0.25f, 0.67f, 0.93f, 0.92f),
    };

    /// <summary>可选的色板（段 ② 负责人选定 A，候选 B / C 已删；留着按套名选的入口供以后再调色）。</summary>
    public static readonly IReadOnlyList<TerrainPalette> All = [A];

    /// <summary>本进程使用的一套（缺省 A）；GameRoot 在建局前按 <c>--palette=</c> 设定。</summary>
    public static TerrainPalette Current { get; private set; } = A;

    /// <summary>按套名（不区分大小写）选定本进程的色板；未知套名返回 false、不改动。</summary>
    public static bool TrySelect(string name)
    {
        foreach (TerrainPalette palette in All)
        {
            if (string.Equals(palette.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Current = palette;
                return true;
            }
        }

        return false;
    }
}
