using System.Collections.Generic;
using Godot;

namespace Siege.Godot;

/// <summary>
/// 信物标记对照图（chibi-world 段 ③，段 ② 裁决 7 的取样用）：<c>--marker-swatch=&lt;PNG 路径&gt;</c> 时不建局，拍一张图后退出。
/// 九种可落子地表（草地三片、土路、林地、荒漠、沼泽、岩台、浅滩）各一块，每块格心摆一枚地图信物、格边一圈信物环（信物层的实线环，y 0.032）。三行：
/// <list type="number">
/// <item>改前：晶石色 <see cref="Visuals.RelicMark"/>、哑光底座、无墨线（<see cref="MarkerArt.RelicToken"/> 的 <c>ink: false</c>）。</item>
/// <item>改后：同一晶石色，底座卡通分阶，晶石与底座套墨色外壳，环下垫墨色衬底（对局实际画法）。</item>
/// <item>候选：晶石与环改品红 <see cref="CandidateMark"/>，其余同改后（待负责人裁决，对局里没用）。</item>
/// </list>
/// 灯光与环境同 <see cref="SurfaceSwatch"/>。截图后逐块打印晶石、地面与环的像素坐标，供 README 取样。
/// </summary>
public sealed partial class MarkerSwatch : Node3D
{
    /// <summary>候选信物色（品红）：离九种地表与其它标记色都远（README 段 ③ 有距离表）。</summary>
    public static readonly Color CandidateMark = Color.Color8(236, 84, 188);

    private const float Spacing = 1.2f;
    private readonly string _path;
    private readonly List<(string Name, Vector3 At)> _probes = [];
    private Camera3D? _camera;
    private int _frame;
    private bool _pending;

    /// <param name="path">PNG 输出路径。</param>
    public MarkerSwatch(string path) => _path = path;

    /// <inheritdoc/>
    public override void _Ready()
    {
        SurfaceSwatch.AddStudio(this);
        TerrainPalette palette = TerrainPalette.Current;
        (string Name, Color Color)[] surfaces =
        [
            ("grass", palette.GrassPatchA), ("grass-2", palette.GrassPatchB), ("grass-3", palette.GrassPatchC),
            ("road", palette.Road), ("forest", palette.Forest), ("desert", palette.Desert), ("marsh", palette.Marsh),
            ("crag", palette.Crag), ("shallows", palette.Shallows),
        ];
        (string Row, float Z, Color Mark, bool Ink)[] rows =
        [
            ("before", -1.4f, Visuals.RelicMark, false),
            ("after", 0.1f, Visuals.RelicMark, true),
            ("candidate", 1.6f, CandidateMark, true),
        ];

        foreach ((string row, float z, Color mark, bool ink) in rows)
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                float x = (i - ((surfaces.Length - 1) * 0.5f)) * Spacing;
                (string name, Color color) = surfaces[i];
                var top = new Vector3(x, 0f, z);
                AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(BoardGeometry.TileSize, BoardGeometry.TileHeight, BoardGeometry.TileSize) },
                    MaterialOverride = Visuals.ToonVertex(color),
                    Position = top - new Vector3(0f, BoardGeometry.TileHeight * 0.5f, 0f),
                });

                Node3D token = MarkerArt.RelicToken(mark, ink);
                token.Position = top;
                AddChild(token);

                Vector3 ringCenter = top + new Vector3(0f, 0.032f, 0f);
                if (ink)
                {
                    AddChild(new MeshInstance3D
                    {
                        Mesh = MarkerArt.RingInk(false, 0.055f),
                        MaterialOverride = MarkerArt.InkFlat,
                        Position = ringCenter,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                }

                MarkerArt.Ring(this, ringCenter, mark, false, 0.055f);
                if (row == "before")
                {
                    Label(name, new Vector3(x, 0.01f, z - 0.66f));
                }

                _probes.Add(($"{row}/{name}/crystal", top + new Vector3(0f, 0.09f, 0f)));
                _probes.Add(($"{row}/{name}/ground", top + new Vector3(0.26f, 0f, -0.20f)));
                _probes.Add(($"{row}/{name}/ring", ringCenter + new Vector3(0f, 0.01f, BoardGeometry.TileSize * 0.5f)));
            }
        }

        float pitch = Mathf.DegToRad(60f);
        const float distance = 7.3f;
        var focus = new Vector3(0f, 0f, 0.1f);
        AddChild(_camera = new Camera3D
        {
            Fov = Siege.Presentation.Camera.CameraPose.FovDegrees,
            Position = focus + new Vector3(0f, distance * Mathf.Sin(pitch), distance * Mathf.Cos(pitch)),
            RotationDegrees = new Vector3(-60f, 0f, 0f),
            Current = true,
        });
        GD.Print($"[marker-swatch] 三行：改前（{Hex(Visuals.RelicMark)}，无墨线）/ 改后（{Hex(Visuals.RelicMark)} + 墨线）/ 候选（{Hex(CandidateMark)} + 墨线）；九种地表");
    }

    private static string Hex(Color c) => $"{(int)(c.R8)},{(int)(c.G8)},{(int)(c.B8)}";

    private void Label(string text, Vector3 at) => AddChild(new Label3D
    {
        Text = text,
        Position = at,
        RotationDegrees = new Vector3(-90f, 0f, 0f),
        FontSize = 52,
        PixelSize = 0.004f,
        Modulate = Visuals.CoordinateLabel,
        OutlineModulate = Visuals.CoordinateLabelOutline,
        OutlineSize = 10,
    });

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
            GD.Print("[marker-swatch] 无头模式没有可截取的画面，已跳过截图。");
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
        GD.Print($"[marker-swatch] 截图 {_path}：{error}（{image.GetWidth()}×{image.GetHeight()}，第 {_frame} 帧）");
        foreach ((string name, Vector3 at) in _probes)
        {
            Vector2 p = _camera!.UnprojectPosition(at);
            GD.Print($"[marker-swatch] 取样 {name} {p.X:0} {p.Y:0}");
        }

        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
