using Godot;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// 视觉常量与材质工厂。颜色、旗帜图案、棋子轮廓的<b>取值</b>全部来自 <see cref="Siege.Presentation.Style"/>，
/// 本类只负责把它们变成 Godot 的 <see cref="Color"/> 与 <see cref="StandardMaterial3D"/>。
/// </summary>
public static class Visuals
{
    /// <summary>棋盘底座色（缝隙露出的颜色，即网格线）。</summary>
    public static readonly Color GridInk = Color.Color8(48, 55, 58);

    /// <summary>新棋盘的深色板岩岛缘，金色坐标和浅色地砖在其上形成清楚的内外层次。</summary>
    public static readonly Color IslandRim = Color.Color8(55, 68, 77);

    /// <summary>棋盘四边坐标标注的字色。棋盘外是深色背景，深灰字读不出来，取浅色并配深色描边。</summary>
    public static readonly Color CoordinateLabel = Color.Color8(250, 230, 184);

    /// <summary>坐标标注的描边色：压住浅色字在亮地砖上的反差不足。</summary>
    public static readonly Color CoordinateLabelOutline = Color.Color8(18, 19, 23);

    /// <summary>可落子地砖（草地，terrain-model 缺省地表）。</summary>
    public static readonly Color TilePlayable = Color.Color8(177, 197, 148);

    /// <summary>土路地表：比草地暖、比岩石亮，只作视觉，帮玩家读出"下山的路"。</summary>
    public static readonly Color TileRoad = Color.Color8(226, 196, 149);

    /// <summary>林地地表：深绿，配角落的小树；林地不接收覆盖，靠占据拿。</summary>
    public static readonly Color TileForest = Color.Color8(75, 136, 113);

    /// <summary>
    /// 荒漠地表（terrain-surfaces）：比土路更浅更白的沙色。去色后仍与土路、草地拉开；
    /// 与土路的区分主要靠格角的仙人掌 / 兽骨（段 1 补装饰）。
    /// </summary>
    public static readonly Color TileDesert = Color.Color8(246, 222, 165);

    /// <summary>仙人掌（荒漠装饰）：偏灰的橄榄绿，比林地树冠浅、比沼泽亮；轮廓是竖柱带侧臂，不是锥形树冠。</summary>
    public static readonly Color Cactus = Color.Color8(80, 133, 107);

    /// <summary>兽骨（荒漠装饰）：暖白，贴地横放，灰度下是地砖上最亮的一小块。</summary>
    public static readonly Color Bone = Color.Color8(251, 236, 205);

    /// <summary>沼泽水洼：暗蓝灰的积水斑块，贴地；比深水暗、比浅滩深，读起来是"泥里的水"。</summary>
    public static readonly Color MarshPuddle = Color.Color8(52, 84, 101);

    /// <summary>芦苇（沼泽装饰）：干草黄褐。细长竖条成簇，轮廓与林地锥形树冠、荒漠仙人掌都不同。</summary>
    public static readonly Color Reed = Color.Color8(203, 170, 106);

    /// <summary>岩台的裂纹与碎石：比岩台地砖深一档的冷褐，贴地细线；灰度下是石面上的深色纹理。</summary>
    public static readonly Color CragCrack = Color.Color8(82, 85, 90);

    /// <summary>浅滩的鹅卵石：浅暖灰，比浅滩水色暗一档，灰度下是水面上的一颗颗小斑点。</summary>
    public static readonly Color Pebble = Color.Color8(218, 201, 169);

    /// <summary>浅滩的水纹：近白的细弧线，贴地；深水没有这个纹理，二者靠它与高度一眼分开。</summary>
    public static readonly Color Ripple = Color.Color8(226, 247, 237);

    /// <summary>沼泽地表：暗蓝灰泥色，去色后比林地更暗；形状提示是水洼与芦苇，不用锥形树冠。</summary>
    public static readonly Color TileMarsh = Color.Color8(76, 97, 96);

    /// <summary>岩台地表：灰褐石面，与障碍岩石靠材质和裂纹区分；形状提示是裂纹与加厚边缘。</summary>
    public static readonly Color TileCrag = Color.Color8(161, 151, 135);

    /// <summary>浅滩地表：与地砖齐平的浅青水色；与深水（低于地砖、深蓝）靠高度、水纹与鹅卵石区分。</summary>
    public static readonly Color TileShallows = Color.Color8(121, 198, 201);

    /// <summary>深水：低于地砖的蓝色水面，不可落子。</summary>
    public static readonly Color DeepWater = Color.Color8(39, 112, 159);

    /// <summary>深水的浅色波纹。</summary>
    public static readonly Color WaterRipple = Color.Color8(173, 235, 225);

    /// <summary>预置桥的木板面。</summary>
    public static readonly Color BridgeDeck = Color.Color8(171, 126, 81);

    /// <summary>栅栏与桥墩的深色木料。</summary>
    public static readonly Color Timber = Color.Color8(103, 75, 60);

    /// <summary>h=1 那一层的侧面（缓坡侧面）：土色。</summary>
    public static readonly Color SlopeSide = Color.Color8(172, 135, 102);

    /// <summary>h=2 那一层的侧面（崖壁侧面）：岩灰。Δh=2 的崖壁露出土色 + 岩灰两条色带，Δh=1 的缓坡只露土色一条。</summary>
    public static readonly Color CliffSide = Color.Color8(143, 127, 117);

    /// <summary>林地小树的树冠。</summary>
    public static readonly Color TreeCanopy = Color.Color8(49, 119, 100);

    /// <summary>松树丛里点缀的秋色树冠（装饰层）。</summary>
    public static readonly Color PineAutumn = Color.Color8(224, 176, 86);

    /// <summary>断柱遗迹的浅色石材（装饰层）。</summary>
    public static readonly Color RuinStone = Color.Color8(199, 183, 158);

    /// <summary>
    /// 公共棋盘的边框（board-map D10）：不属于任何阵营的浅石色。与四个阵营主色、插旗阶段的出生区提示色（金）、
    /// 锁定后无主出生平台的褪色（冷灰）都拉得开，出生棋盘与公共棋盘一眼可分。
    /// </summary>
    public static readonly Color PublicBoardFrame = ToColor(BoardPlatePalette.PublicFrame);   // board-link-mode D-b：唯一来源在 Presentation 调色板

    /// <summary>未展开的出生棋盘占位的台面边框（board-link-mode D4，低饱和深色；灰度可辨由 Presentation 的 占位轮廓灰度Tests 守住）。</summary>
    public static readonly Color PlaceholderFrame = ToColor(BoardPlatePalette.PlaceholderFrame);

    /// <summary>占位编号文字。</summary>
    public static readonly Color PlaceholderLabel = ToColor(BoardPlatePalette.PlaceholderLabel);

    /// <summary>棋盘台面的格线（board-map D10）：压暗的墨色细线，只画在棋盘清单里的棋盘之内。</summary>
    public static readonly Color BoardGridLine = Color.Color8(56, 67, 64);

    /// <summary>障碍岩石（装饰层）。</summary>
    public static readonly Color TileObstacle = Color.Color8(122, 143, 119);

    /// <summary>岩石。</summary>
    public static readonly Color Rock = Color.Color8(155, 146, 132);

    /// <summary>旗帜图案 / 棋子高光：高亮度，保证去色后与阵营主色拉开明度差。</summary>
    public static readonly Color EmblemInk = Color.Color8(255, 242, 205);

    /// <summary>插旗阶段的出生区提示色（尚无归属时统一高亮）。</summary>
    public static readonly Color BirthHint = ToColor(BoardPlatePalette.BirthHint);   // board-link-mode D-b：唯一来源在 Presentation 调色板

    /// <summary>未知信物标记。</summary>
    public static readonly Color RelicUnknown = Color.Color8(234, 189, 93);

    /// <summary>已揭示信物标记。</summary>
    public static readonly Color RelicRevealed = Color.Color8(111, 224, 202);

    /// <summary>光标。</summary>
    public static readonly Color Cursor = Color.Color8(255, 246, 210);

    /// <summary>领地层四态。</summary>
    public static readonly Color Contested = Color.Color8(238, 187, 81);

    /// <summary>中立。</summary>
    public static readonly Color Neutral = Color.Color8(110, 112, 120);

    /// <summary>气层标记。</summary>
    public static readonly Color Liberty = Color.Color8(112, 196, 232);

    /// <summary>棋串读法里"贴着的空浅滩：不算气"的标记：暗灰小点（配合更小的尺寸），与亮青的气点在明度和大小两条通道上都分得开。</summary>
    public static readonly Color ShallowNoLiberty = Color.Color8(96, 104, 112);

    /// <summary>危险。</summary>
    public static readonly Color Danger = Color.Color8(236, 150, 60);

    /// <summary>紧急。</summary>
    public static readonly Color Urgent = Color.Color8(236, 76, 60);

    // ---------- 活形（life-shape 3.4）----------

    /// <summary>已活棋串的标记色（翠绿）：只是第二通道，第一通道是悬浮的环形"眼"徽记（<see cref="GroupMarkShape.SolidRingWithEyeBadge"/>）。</summary>
    public static readonly Color Alive = Color.Color8(72, 230, 140);

    /// <summary>禁入格印记的底色（压暗）：配所有者阵营色的叉与方框（<see cref="PlacementMarkStyle.LifeSeal"/>）。</summary>
    public static readonly Color ForbiddenShade = Color.Color8(24, 26, 32);

    // ---------- 改造（artisan-terrain-edit 4.2）----------
    // 三色都不是木料色：候选 / 已选 / 落成都是"标记"，与真设施（<see cref="Timber"/> / <see cref="BridgeDeck"/> 的棕木）一眼分得开。

    /// <summary>可改造目标（候选）：冷青白，配半透明贴地虚线短条。</summary>
    public static readonly Color EditTarget = Color.Color8(150, 230, 240);

    /// <summary>已选中的改造目标：暖亮黄，配立起的实心亮栏 / 实心亮环。</summary>
    public static readonly Color EditChosen = Color.Color8(255, 226, 120);

    /// <summary>改造落成的瞬时反馈：近白高亮，只在演出时长内出现。</summary>
    public static readonly Color EditDone = Color.Color8(255, 250, 220);

    /// <summary><see cref="Rgba"/>（Presentation 的整数色）→ Godot 颜色。</summary>
    public static Color ToColor(Rgba c) => Color.Color8(c.R, c.G, c.B, c.A);

    /// <summary>某阵营主色。</summary>
    public static Color FactionColorOf(Siege.Core.Board.PlayerId player) => ToColor(FactionTable.For(player).Primary);

    /// <summary>哑光材质。</summary>
    public static StandardMaterial3D Matte(Color albedo, float roughness = 0.9f) => new()
    {
        AlbedoColor = albedo,
        Roughness = roughness,
        Metallic = 0f,
    };

    /// <summary>自发光材质（用于标记与高亮，保证在任何光照下都读得出来）。</summary>
    public static StandardMaterial3D Glow(Color albedo, float energy, bool translucent)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            Roughness = 0.6f,
            EmissionEnabled = true,
            Emission = albedo,
            EmissionEnergyMultiplier = energy,
        };
        if (translucent)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        return material;
    }

    private static ShaderMaterial? _waterFlow;
    private static ShaderMaterial? _waterfall;

    /// <summary>
    /// 水面流动（纯装饰）：一张贴在水面上的透明层，按<b>世界坐标</b>画两组漂移的浅色波纹与细碎闪光——相邻水格的纹路自然接上，
    /// 整条河读作一体在流。全图水格共用这一份材质；打开信息层时由 <c>dim</c> 压淡（与装饰对比同步）。不参与拾取、不投影。
    /// </summary>
    public static ShaderMaterial WaterFlow => _waterFlow ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
                uniform vec4 foam : source_color = vec4(0.68, 0.94, 0.88, 0.72);
                uniform float speed = 0.55;
                uniform float dim = 1.0;
                varying vec3 world;
                void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
                void fragment() {
                    float t = TIME * speed;
                    float a = sin(world.x * 3.1 + sin(world.z * 1.7 + t * 0.8) * 1.3 + t);
                    float b = sin(world.z * 4.3 - t * 1.6 + sin(world.x * 2.3 + t * 0.5) * 1.1);
                    float c = sin((world.x + world.z) * 7.0 + t * 2.4) * sin((world.x - world.z) * 5.0 - t * 1.3);
                    float bands = smoothstep(0.90, 1.0, a * 0.5 + 0.5) + 0.7 * smoothstep(0.93, 1.0, b * 0.5 + 0.5);
                    float glint = 0.35 * smoothstep(0.80, 1.0, c);
                    float swell = 0.07 * (0.5 + 0.5 * sin(world.x * 0.9 + world.z * 1.1 + t * 0.7));
                    ALBEDO = foam.rgb;
                    ALPHA = clamp(bands + glint + swell, 0.0, 1.0) * foam.a * dim;
                }
                """,
        },
    };

    /// <summary>瀑布水帘（纯装饰）：沿世界 Y 向下滚动的亮暗条纹，全图共用一份材质。</summary>
    public static ShaderMaterial Waterfall => _waterfall ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, blend_mix, cull_disabled, shadows_disabled;
                uniform vec4 water : source_color = vec4(0.31, 0.73, 0.75, 0.90);
                uniform float speed = 3.2;
                varying vec3 world;
                void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
                void fragment() {
                    float across = world.x * 9.0 + world.z * 9.0;
                    float fall = sin(world.y * 2.2 + TIME * speed + sin(across) * 2.0);
                    float streak = smoothstep(0.55, 1.0, fall * 0.5 + 0.5);
                    float fade = clamp((world.y + 9.5) / 3.0, 0.0, 1.0);
                    ALBEDO = mix(water.rgb, vec3(1.0), streak * 0.75);
                    ALPHA = water.a * fade;
                }
                """,
        },
    };

    /// <summary>无光照的扁平材质：贴在地砖上的标记用，避免被光照吃掉判读性。</summary>
    public static StandardMaterial3D Flat(Color albedo)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (albedo.A < 1f)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        return material;
    }

    /// <summary>按百分比（100 = 原样）把颜色拉向中性灰，用于信息层的装饰对比 / 棋子弱化处理。</summary>
    public static Color Damp(Color color, int percent)
    {
        float t = 1f - (Mathf.Clamp(percent, 0, 100) / 100f);
        var grey = new Color(0.34f, 0.34f, 0.36f, color.A);
        return color.Lerp(grey, t);
    }
}
