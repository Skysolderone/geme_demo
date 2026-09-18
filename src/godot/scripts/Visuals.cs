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
    public static readonly Color GridInk = Color.Color8(28, 30, 36);

    /// <summary>棋盘四边坐标标注的字色。棋盘外是深色背景，深灰字读不出来，取浅色并配深色描边。</summary>
    public static readonly Color CoordinateLabel = Color.Color8(206, 202, 190);

    /// <summary>坐标标注的描边色：压住浅色字在亮地砖上的反差不足。</summary>
    public static readonly Color CoordinateLabelOutline = Color.Color8(18, 19, 23);

    /// <summary>可落子地砖（草地，terrain-model 缺省地表）。</summary>
    public static readonly Color TilePlayable = Color.Color8(146, 156, 118);

    /// <summary>土路地表：比草地暖、比岩石亮，只作视觉，帮玩家读出"下山的路"。</summary>
    public static readonly Color TileRoad = Color.Color8(178, 160, 122);

    /// <summary>林地地表：深绿，配角落的小树；林地不接收覆盖，靠占据拿。</summary>
    public static readonly Color TileForest = Color.Color8(92, 128, 84);

    /// <summary>深水：低于地砖的蓝色水面，不可落子。</summary>
    public static readonly Color DeepWater = Color.Color8(58, 104, 148);

    /// <summary>深水的浅色波纹。</summary>
    public static readonly Color WaterRipple = Color.Color8(104, 150, 190);

    /// <summary>预置桥的木板面。</summary>
    public static readonly Color BridgeDeck = Color.Color8(156, 116, 72);

    /// <summary>栅栏与桥墩的深色木料。</summary>
    public static readonly Color Timber = Color.Color8(112, 84, 54);

    /// <summary>h=1 那一层的侧面（缓坡侧面）：土色。</summary>
    public static readonly Color SlopeSide = Color.Color8(134, 108, 84);

    /// <summary>h=2 那一层的侧面（崖壁侧面）：岩灰。Δh=2 的崖壁露出土色 + 岩灰两条色带，Δh=1 的缓坡只露土色一条。</summary>
    public static readonly Color CliffSide = Color.Color8(112, 108, 104);

    /// <summary>林地小树的树冠。</summary>
    public static readonly Color TreeCanopy = Color.Color8(70, 112, 66);

    /// <summary>障碍岩石（装饰层）。</summary>
    public static readonly Color TileObstacle = Color.Color8(96, 98, 104);

    /// <summary>岩石。</summary>
    public static readonly Color Rock = Color.Color8(122, 120, 116);

    /// <summary>营帐帆布（据点地标，浅米色，与岩灰 / 草地在灰度下也拉得开）。</summary>
    public static readonly Color TentCanvas = Color.Color8(222, 204, 164);

    /// <summary>营帐门洞。</summary>
    public static readonly Color TentDoor = Color.Color8(70, 52, 38);

    /// <summary>篝火外焰（S-16 提亮：236,118,40 → 255,140,40，配合自发光 2.0）。</summary>
    public static readonly Color FlameOuter = Color.Color8(255, 140, 40);

    /// <summary>篝火内焰（S-16 提亮：255,214,96 → 255,236,140，配合自发光 2.6）。</summary>
    public static readonly Color FlameInner = Color.Color8(255, 236, 140);

    /// <summary>
    /// 石碑石面（S-16：亮白石色，灰度明度 ≈ 222；岩石 <see cref="Rock"/> 明度 ≈ 120，差约 100，不再与障碍格岩石混淆）。
    /// 原值 168,176,190（明度 ≈ 175）。
    /// </summary>
    public static readonly Color SteleStone = Color.Color8(226, 222, 208);

    /// <summary>石碑基座（S-16：浅石色，明度 ≈ 184；原先直接用岩石色）。</summary>
    public static readonly Color SteleBase = Color.Color8(190, 184, 168);

    /// <summary>石碑刻痕（S-16：石面变亮后刻痕改深，明度 ≈ 113，与石面差约 108）。</summary>
    public static readonly Color SteleCarving = Color.Color8(120, 112, 100);

    // ---------- 据点格底色（S-16）：内嵌边框带，按档位区分，低饱和；灰度明度 = 0.299R + 0.587G + 0.114B ----------
    // 地表参照（反照率明度）：障碍 98、林地 112、桥 123、草地 149、土路 161、出生区插旗前高亮 ≈ 178、
    // 出生区锁定后（草地与阵营主色 0.34 混合）红 132 / 蓝 132 / 金 156 / 紫 132。
    // 营帐全在出生区，篝火与石碑全在草地上，三档各取离所在地砖远的明度，且三档两两相差 ≥ 40。

    /// <summary>营帐格底色：暖炭灰，明度 ≈ 67（与出生区地砖 132–178 差 ≥ 65）。</summary>
    public static readonly Color SiteBandTent = Color.Color8(74, 66, 58);

    /// <summary>篝火格底色：低饱和梅灰红，明度 ≈ 108（与草地 149 差 41；地表没有这一色相）。</summary>
    public static readonly Color SiteBandCampfire = Color.Color8(140, 92, 104);

    /// <summary>石碑格底色：冷象牙白，明度 ≈ 210（与草地 149 差 61）。</summary>
    public static readonly Color SiteBandStele = Color.Color8(214, 210, 196);

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
