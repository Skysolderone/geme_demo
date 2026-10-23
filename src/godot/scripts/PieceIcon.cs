using Godot;
using Siege.Core.Board;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>
/// 20 像素的棋子轮廓铭牌。业务类型只经 <see cref="PieceStyleTable"/> 映到轮廓；
/// 与 3D 棋子使用同一组十种轮廓语言，图元适配紧凑的 HUD 尺寸。
/// </summary>
public sealed partial class PieceIcon : Control
{
    private PieceSilhouette _silhouette;
    private Color _ink = Ui.InfoText;

    /// <summary>把图标叠在已有按钮的左侧；图标不接收鼠标，整行仍由原按钮处理点击。</summary>
    public static void Attach(Button button, PieceType type, bool selected = false, bool disabled = false)
    {
        // 两个全角空格只预留图标的绘制位置；名称、序号、数量及原按钮事件保持原样。
        button.Text = "　　" + button.Text;
        var icon = new PieceIcon
        {
            _silhouette = PieceStyleTable.For(type).Silhouette,
            _ink = disabled ? Ui.MutedText : selected ? Ui.PanelBorder : Ui.InfoText,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = 9f,
            OffsetRight = 29f,
            OffsetTop = -10f,
            OffsetBottom = 10f,
        };
        button.AddChild(icon);
    }

    public override void _Draw()
    {
        float unit = Mathf.Min(Size.X, Size.Y);
        if (unit <= 0f)
        {
            return;
        }

        Vector2 P(float x, float y) => new((Size.X * 0.5f) + (x * unit), (Size.Y * 0.5f) + (y * unit));
        void Line(float ax, float ay, float bx, float by, float width = 0.09f) =>
            DrawLine(P(ax, ay), P(bx, by), _ink, Mathf.Max(1f, width * unit), true);
        void Fill(params Vector2[] shape)
        {
            var points = new Vector2[shape.Length];
            for (int i = 0; i < shape.Length; i++)
            {
                points[i] = P(shape[i].X, shape[i].Y);
            }

            DrawColoredPolygon(points, _ink);
        }

        switch (_silhouette)
        {
            case PieceSilhouette.RoundPawn:
                DrawCircle(P(0f, -0.22f), unit * 0.15f, _ink);
                Fill(new(-0.16f, -0.03f), new(0.16f, -0.03f), new(0.23f, 0.31f), new(-0.23f, 0.31f));
                Line(-0.30f, 0.34f, 0.30f, 0.34f);
                break;
            case PieceSilhouette.Tower:
                Fill(new(-0.23f, -0.22f), new(0.23f, -0.22f), new(0.23f, 0.34f), new(-0.23f, 0.34f));
                Fill(new(-0.30f, -0.38f), new(-0.15f, -0.38f), new(-0.15f, -0.17f), new(-0.30f, -0.17f));
                Fill(new(-0.07f, -0.38f), new(0.07f, -0.38f), new(0.07f, -0.17f), new(-0.07f, -0.17f));
                Fill(new(0.15f, -0.38f), new(0.30f, -0.38f), new(0.30f, -0.17f), new(0.15f, -0.17f));
                break;
            case PieceSilhouette.TwinOrbBar:
                Line(-0.27f, 0.02f, 0.27f, 0.02f, 0.12f);
                DrawCircle(P(-0.27f, 0.02f), unit * 0.13f, _ink);
                DrawCircle(P(0.27f, 0.02f), unit * 0.13f, _ink);
                Line(0f, 0.02f, 0f, 0.33f, 0.10f);
                break;
            case PieceSilhouette.Pyramid:
                Fill(new(0f, -0.39f), new(0.33f, 0.32f), new(-0.33f, 0.32f));
                Line(-0.36f, 0.35f, 0.36f, 0.35f);
                break;
            case PieceSilhouette.CrystalCluster:
                Fill(new(0f, -0.43f), new(0.17f, -0.05f), new(0f, 0.36f), new(-0.17f, -0.05f));
                Fill(new(-0.29f, -0.16f), new(-0.13f, 0.06f), new(-0.22f, 0.31f), new(-0.36f, 0.08f));
                Fill(new(0.29f, -0.16f), new(0.36f, 0.08f), new(0.22f, 0.31f), new(0.13f, 0.06f));
                break;
            case PieceSilhouette.Scaffold:
                Line(-0.22f, 0.36f, 0.10f, -0.28f, 0.13f);
                Line(-0.09f, -0.29f, 0.32f, -0.29f, 0.17f);
                Line(-0.31f, 0.36f, 0.16f, 0.36f);
                break;
            case PieceSilhouette.Pennant:
                Line(-0.18f, -0.42f, -0.18f, 0.36f, 0.10f);
                Fill(new(-0.14f, -0.37f), new(0.32f, -0.27f), new(0.23f, -0.04f), new(-0.14f, -0.13f));
                Line(-0.29f, 0.37f, 0.01f, 0.37f);
                break;
            case PieceSilhouette.ChainLinks:
                Loop([new(-0.31f, -0.16f), new(-0.17f, -0.33f), new(0.02f, -0.18f), new(0.04f, 0.14f), new(-0.12f, 0.31f), new(-0.30f, 0.13f)]);
                Loop([new(-0.03f, -0.18f), new(0.14f, -0.32f), new(0.31f, -0.14f), new(0.31f, 0.17f), new(0.14f, 0.31f), new(-0.02f, 0.13f)]);
                break;
            case PieceSilhouette.CrossedSpears:
                Line(-0.29f, 0.34f, 0.25f, -0.29f, 0.09f);
                Line(0.29f, 0.34f, -0.25f, -0.29f, 0.09f);
                Fill(new(0.25f, -0.45f), new(0.35f, -0.21f), new(0.13f, -0.31f));
                Fill(new(-0.25f, -0.45f), new(-0.13f, -0.31f), new(-0.35f, -0.21f));
                break;
            case PieceSilhouette.Stele:
                Fill(new(-0.33f, -0.18f), new(-0.22f, -0.30f), new(0.22f, -0.30f), new(0.33f, -0.18f), new(0.33f, 0.34f), new(-0.33f, 0.34f));
                Line(-0.22f, 0.03f, 0.22f, 0.03f, 0.055f);
                Line(-0.22f, 0.18f, 0.22f, 0.18f, 0.055f);
                break;
        }

        void Loop(Vector2[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                Line(a.X, a.Y, b.X, b.Y, 0.085f);
            }
        }
    }
}
