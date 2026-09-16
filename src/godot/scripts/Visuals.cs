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
