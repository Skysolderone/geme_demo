using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Presentation.Camera;
using Siege.Presentation.Text;

namespace Siege.Godot;

/// <summary>
/// 十种棋子轮廓对照图（more-pieces-relics 段 D，tasks 4.2 / visual-style-baseline「轮廓可辨」「易混对可辨」的人工检查清单用）：
/// <c>--piece-gallery=&lt;PNG 路径&gt;</c> 时不建局，只把十种棋子排成两行、同光照同阵营色拍一张图后退出。
/// </summary>
/// <remarks>
/// 纯渲染：棋子节点取 <see cref="BoardView.BuildPiece"/>（与对局盘面同一份画法，类型 → 轮廓只经 PieceStyleTable），不读任何对局状态、不做规则判断。
/// 三组易混对左右相邻排列（连珠 | 铁链、堡垒 | 界碑、匠人 | 旗手），灰度缩略图由 art/more-pieces/README.md 里的 PIL 命令从本图生成。
/// 光照与环境取对局棋盘的主光 / 补光 / 环境光参数；相机俯角 60°、视场角与对局相机相同。
/// </remarks>
public sealed partial class PieceGallery : Node3D
{
    /// <summary>排列：两行各五枚，易混对相邻。</summary>
    private static readonly PieceType[][] Rows =
    [
        [PieceType.Line, PieceType.Chain, PieceType.Fortress, PieceType.Boundary, PieceType.Basic],
        [PieceType.Artisan, PieceType.Bannerman, PieceType.Multiplier, PieceType.Synergy, PieceType.Sentry],
    ];

    private const float Spacing = 1.15f;
    private const float RowGap = 1.7f;
    private readonly string _path;
    private int _frame;
    private bool _pending;

    public PieceGallery(string path)
    {
        _path = path;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52f, -38f, 0f),
            LightColor = Color.Color8(255, 240, 208),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            ShadowBlur = 1.6f,
            ShadowOpacity = 0.55f,
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-20f, 140f, 0f),
            LightColor = Color.Color8(168, 190, 226),
            LightEnergy = 0.35f,
        });
        AddChild(new WorldEnvironment
        {
            Environment = new global::Godot.Environment
            {
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = Color.Color8(206, 228, 246),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Color.Color8(186, 200, 222),
                AmbientLightEnergy = 0.62f,
                TonemapMode = global::Godot.Environment.ToneMapper.Filmic,
                TonemapWhite = 1.4f,
            },
        });

        // 地面：一块草地色平板，与对局可落子地砖同色。
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(Spacing * 6f, RowGap * 2.4f) },
            MaterialOverride = Visuals.Matte(Visuals.TilePlayable),
        });

        var owner = new PlayerId(0);
        for (int r = 0; r < Rows.Length; r++)
        {
            float z = (r - 0.5f) * RowGap;
            for (int i = 0; i < Rows[r].Length; i++)
            {
                PieceType type = Rows[r][i];
                float x = (i - 2) * Spacing;
                Node3D piece = BoardView.BuildPiece(new Occupant(owner, type), 100);
                piece.Position = new Vector3(x, 0f, z);
                AddChild(piece);
                AddChild(new Label3D
                {
                    Text = Labels.Piece(type),
                    Position = new Vector3(x, 0.01f, z + 0.55f),
                    RotationDegrees = new Vector3(-90f, 0f, 0f),
                    FontSize = 64,
                    PixelSize = 0.004f,
                    Modulate = Visuals.CoordinateLabel,
                    OutlineModulate = Visuals.CoordinateLabelOutline,
                    OutlineSize = 10,
                });
            }
        }

        // 俯角 60°，与对局相机一致；注视两行中心。
        const float distance = 5.4f;
        var focus = new Vector3(0f, 0.3f, 0.1f);
        float pitch = Mathf.DegToRad(60f);
        AddChild(new Camera3D
        {
            Fov = CameraPose.FovDegrees,
            Position = focus + new Vector3(0f, distance * Mathf.Sin(pitch), distance * Mathf.Cos(pitch)),
            RotationDegrees = new Vector3(-60f, 0f, 0f),
            Current = true,
        });

        GD.Print("[piece-gallery] 十种棋子（两行，易混对相邻）：" + string.Join(" / ", Rows.Select(row => string.Join("、", row.Select(Labels.Piece)))));
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_pending || ++_frame < 5)
        {
            return;
        }

        _pending = true;
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("[piece-gallery] 无头模式没有可截取的画面，已跳过截图。");
            GetTree().Quit(0);
            return;
        }

        CaptureWhenDrawn();
    }

    private async void CaptureWhenDrawn()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(_path);
        GD.Print($"[piece-gallery] 截图 {_path}：{error}（{image.GetWidth()}×{image.GetHeight()}，第 {_frame} 帧）");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
