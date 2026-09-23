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
    public static readonly Color GridInk = Color.Color8(58, 66, 78);

    /// <summary>浮空岛的岛缘（棋盘外圈、坐标标注所在的那一圈）：压暗的暖色岩石，浅色标注字在上面读得清。</summary>
    public static readonly Color IslandRim = Color.Color8(92, 82, 74);

    /// <summary>棋盘四边坐标标注的字色。棋盘外是深色背景，深灰字读不出来，取浅色并配深色描边。</summary>
    public static readonly Color CoordinateLabel = Color.Color8(206, 202, 190);

    /// <summary>坐标标注的描边色：压住浅色字在亮地砖上的反差不足。</summary>
    public static readonly Color CoordinateLabelOutline = Color.Color8(18, 19, 23);

    /// <summary>可落子地砖（草地，terrain-model 缺省地表）。</summary>
    public static readonly Color TilePlayable = Color.Color8(138, 190, 88);

    /// <summary>土路地表：比草地暖、比岩石亮，只作视觉，帮玩家读出"下山的路"。</summary>
    public static readonly Color TileRoad = Color.Color8(222, 192, 132);

    /// <summary>林地地表：深绿，配角落的小树；林地不接收覆盖，靠占据拿。</summary>
    public static readonly Color TileForest = Color.Color8(86, 150, 78);

    /// <summary>
    /// 荒漠地表（terrain-surfaces）：比土路更浅更白的沙色。去色后亮度约 224，与土路（≈194）、草地（≈163）拉开；
    /// 与土路的区分主要靠格角的仙人掌 / 兽骨（段 1 补装饰）。
    /// </summary>
    public static readonly Color TileDesert = Color.Color8(242, 226, 170);

    /// <summary>沼泽地表：暗橄榄泥色，去色后（≈93）比林地（≈123）更暗；形状提示是水洼与芦苇，不用锥形树冠（段 2 补装饰）。</summary>
    public static readonly Color TileMarsh = Color.Color8(92, 98, 70);

    /// <summary>岩台地表：灰褐石面，去色后 ≈140，与障碍岩石的冷灰区分；形状提示是裂纹与加厚边缘（段 3 补装饰）。</summary>
    public static readonly Color TileCrag = Color.Color8(150, 138, 120);

    /// <summary>浅滩地表：与地砖齐平的浅青水色，去色后 ≈181；与深水（低于地砖、深蓝）靠高度与鹅卵石区分（段 4 补装饰）。</summary>
    public static readonly Color TileShallows = Color.Color8(130, 200, 215);

    /// <summary>深水：低于地砖的蓝色水面，不可落子。</summary>
    public static readonly Color DeepWater = Color.Color8(46, 138, 204);

    /// <summary>深水的浅色波纹。</summary>
    public static readonly Color WaterRipple = Color.Color8(170, 222, 246);

    /// <summary>预置桥的木板面。</summary>
    public static readonly Color BridgeDeck = Color.Color8(176, 128, 78);

    /// <summary>栅栏与桥墩的深色木料。</summary>
    public static readonly Color Timber = Color.Color8(112, 84, 54);

    /// <summary>h=1 那一层的侧面（缓坡侧面）：土色。</summary>
    public static readonly Color SlopeSide = Color.Color8(158, 112, 72);

    /// <summary>h=2 那一层的侧面（崖壁侧面）：岩灰。Δh=2 的崖壁露出土色 + 岩灰两条色带，Δh=1 的缓坡只露土色一条。</summary>
    public static readonly Color CliffSide = Color.Color8(136, 124, 116);

    /// <summary>林地小树的树冠。</summary>
    public static readonly Color TreeCanopy = Color.Color8(48, 128, 72);

    /// <summary>松树丛里点缀的秋色树冠（装饰层）。</summary>
    public static readonly Color PineAutumn = Color.Color8(222, 176, 58);

    /// <summary>断柱遗迹的浅色石材（装饰层）。</summary>
    public static readonly Color RuinStone = Color.Color8(160, 152, 138);

    /// <summary>障碍岩石（装饰层）。</summary>
    public static readonly Color TileObstacle = Color.Color8(118, 150, 96);

    /// <summary>岩石。</summary>
    public static readonly Color Rock = Color.Color8(142, 138, 134);

    /// <summary>旗帜图案 / 棋子高光：高亮度，保证去色后与阵营主色拉开明度差。</summary>
    public static readonly Color EmblemInk = Color.Color8(242, 240, 232);

    /// <summary>插旗阶段的出生区提示色（尚无归属时统一高亮）。</summary>
    public static readonly Color BirthHint = Color.Color8(232, 198, 96);

    /// <summary>未知信物标记。</summary>
    public static readonly Color RelicUnknown = Color.Color8(196, 180, 120);

    /// <summary>已揭示信物标记。</summary>
    public static readonly Color RelicRevealed = Color.Color8(120, 208, 176);

    /// <summary>光标。</summary>
    public static readonly Color Cursor = Color.Color8(248, 248, 240);

    /// <summary>领地层四态。</summary>
    public static readonly Color Contested = Color.Color8(216, 176, 72);

    /// <summary>中立。</summary>
    public static readonly Color Neutral = Color.Color8(110, 112, 120);

    /// <summary>气层标记。</summary>
    public static readonly Color Liberty = Color.Color8(112, 196, 232);

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
                uniform vec4 foam : source_color = vec4(0.78, 0.92, 1.0, 0.85);
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
                uniform vec4 water : source_color = vec4(0.55, 0.82, 0.96, 0.88);
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
