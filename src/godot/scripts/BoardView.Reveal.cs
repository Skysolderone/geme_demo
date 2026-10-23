using System;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Presentation.Camera;
using Siege.Presentation.Layers;
using Siege.Presentation.Show;
using Siege.Presentation.Style;

namespace Siege.Godot;

/// <summary>结算数字揭示、棋串常驻标注和相机轻震的绘制。</summary>
public sealed partial class BoardView
{
    private Node3D _groupLabelLayer = null!;
    private ImmutableArray<GroupPowerLabel> _groupLabels = [];
    private bool _groupLabelsMasked;
    private int _groupLabelShowRedraws;
    private Label3D?[] _farColumnLabels = [];
    private Label3D?[] _leftRowLabels = [];
    private Label3D?[] _rightRowLabels = [];
    private bool _coordinatesYielded;
    private Vector3 _shake = Vector3.Zero;
    private const float LabelPixelSize = 0.00035f;
    private const float RingStagger = 0.3f;

    private void WriteCamera(CameraPose pose)
    {
        Camera.Position = new Vector3(pose.Eye.X, pose.Eye.Y, pose.Eye.Z) + _shake;
        Camera.LookAt(new Vector3(pose.Target.X, pose.Target.Y, pose.Target.Z) + _shake, Vector3.Up);
    }

    public const float ShakeAmplitude = 0.06f * BoardGeometry.CellSize;

    public static Vector3 ShakeOffsetOf(int permille)
    {
        float t = Math.Clamp(permille, 0, 1000) / 1000f;
        float decay = (1f - t) * (1f - t);
        return new Vector3(ShakeAmplitude * decay * Mathf.Sin(Mathf.Tau * 3f * t), 0f,
            0.5f * ShakeAmplitude * decay * Mathf.Sin(Mathf.Tau * 5f * t));
    }

    public Vector3 ShakeOffset => _shake;

    public float CameraOffsetFromPose => _appliedPose is { } pose
        ? Camera.Position.DistanceTo(new Vector3(pose.Eye.X, pose.Eye.Y, pose.Eye.Z)) : 0f;

    public void SetShake(int? permille)
    {
        Vector3 offset = permille is { } p ? ShakeOffsetOf(p) : Vector3.Zero;
        if (offset == _shake)
        {
            return;
        }

        _shake = offset;
        if (_appliedPose is { } pose)
        {
            WriteCamera(pose);
        }
    }

    private void DrawGroupPower(ShowMask mask)
    {
        Clear(_groupLabelLayer);
        _groupLabelsMasked = !mask.GroupLabelPermille.IsEmpty;
        foreach (GroupPowerLabel item in _groupLabels)
        {
            int shown = mask.GroupLabelPermille.TryGetValue(item.Coord, out int permille) ? permille : 1000;
            if (shown <= 0)
            {
                continue;
            }

            float alpha = shown / 1000f;
            NumberTierStyle style = NumberTierStyle.For(item.Tier);
            (Rgba text, Rgba outline) = BoardLabelStyle.GroupLabelColors(item.Owner);
            float x = item.Corner == LabelCorner.FrontLeft ? -0.33f : 0.33f;
            Label3D label = Label(item.Text, CenterOf(item.Coord) + new Vector3(x, 0.12f, 0.34f),
                style.GroupLabelFontSize, BoardLabelStyle.GroupLabelOutlineFor(style.GroupLabelOutlineSize));
            label.Modulate = new Color(Visuals.ToColor(text), alpha);
            label.OutlineModulate = new Color(Visuals.ToColor(outline), alpha);
            label.NoDepthTest = true;
            _groupLabelLayer.AddChild(label);
        }
    }

    public string GroupLabelRedrawReadout() => $"演出帧里这一层重画 {_groupLabelShowRedraws} 次";

    private void DrawCoordinateYield(ShowMask mask)
    {
        CoordinateYieldView yielded = CoordinateLabelYield.Of(mask, _width, _height);
        if (yielded.IsEmpty && !_coordinatesYielded)
        {
            return;
        }

        _coordinatesYielded = !yielded.IsEmpty;
        for (int x = 0; x < _farColumnLabels.Length; x++)
        {
            ShowCoordinateLabel(_farColumnLabels[x], yielded.FarColumn(x));
        }

        for (int y = 0; y < _leftRowLabels.Length; y++)
        {
            ShowCoordinateLabel(_leftRowLabels[y], yielded.LeftRow(y));
            ShowCoordinateLabel(_rightRowLabels[y], yielded.RightRow(y));
        }
    }

    private static void ShowCoordinateLabel(Label3D? label, int permille)
    {
        if (label is null)
        {
            return;
        }

        float alpha = permille / 1000f;
        label.Modulate = new Color(Visuals.CoordinateLabel, alpha);
        label.OutlineModulate = new Color(Visuals.CoordinateLabelOutline, alpha);
    }

    private void DrawRings(ShowMask mask)
    {
        foreach (ImpactRing ring in mask.Rings)
        {
            RevealDisplay? at = mask.Reveals.FirstOrDefault(r => r.Coord == ring.Coord);
            Color color = at is null ? Visuals.EditDone : Visuals.ToColor(NumberTierStyle.For(at.FinalTier).RevealColor);
            for (int i = 0; i < ring.Count; i++)
            {
                float t = ((ring.ProgressPermille / 1000f) - (i * RingStagger)) / (1f - (i * RingStagger));
                if (t <= 0f)
                {
                    continue;
                }

                float eased = 1f - ((1f - t) * (1f - t));
                float radius = BoardGeometry.CellSize * (0.30f + (1.05f * eased));
                float half = 0.5f * (0.20f - (0.14f * t));
                _show.AddChild(new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = radius - half, OuterRadius = radius + half, Rings = 40, RingSegments = 4 },
                    MaterialOverride = Visuals.Flat(new Color(color.Lightened(0.35f), 0.95f * (1f - (t * t)))),
                    Position = CenterOf(ring.Coord) + new Vector3(0f, 0.06f, 0f),
                    Scale = new Vector3(1f, 0.1f, 1f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }
    }

    private void DrawReveals(ShowMask mask)
    {
        foreach (RevealDisplay reveal in mask.Reveals)
        {
            NumberTierStyle style = NumberTierStyle.For(reveal.StepTier);
            float age = reveal.ResultAgePermille / 1000f;
            float alpha = age < 0.5f ? 1f : 1f - ((age - 0.5f) * 2f);
            Color color = Visuals.ToColor(style.RevealColor);
            string formula = reveal.AtFinal && reveal.RunningText.EndsWith(reveal.StepText, StringComparison.Ordinal)
                ? reveal.RunningText[..^reveal.StepText.Length].TrimEnd() : string.Empty;
            string main = reveal.AtFinal && formula.Length > 0 ? reveal.StepText : reveal.RunningText;

            Vector3 foot = CenterOf(reveal.Coord) + new Vector3(0f, 0.95f + (0.5f * age), -0.05f);
            float unit = LabelPixelSize * Mathf.Max(0.1f, -(Camera.GlobalTransform.AffineInverse() * foot).Z);
            float mainHeight = style.RevealFontSize * unit;
            Vector3 at = foot + new Vector3(0f, 0f, -0.5f * mainHeight);
            Label3D label = Label(main, at, style.RevealFontSize, style.RevealOutlineSize);
            label.Scale = Vector3.One * PopScaleOf(reveal);
            label.Modulate = new Color(color, alpha);
            label.OutlineModulate = new Color(Visuals.CoordinateLabelOutline, alpha);
            label.NoDepthTest = true;
            label.RenderPriority = 4;
            label.OutlineRenderPriority = 3;
            _show.AddChild(label);

            if (formula.Length > 0)
            {
                int fontSize = BoardLabelStyle.RevealFormulaFontSizeOf(reveal.StepTier);
                Label3D trail = Label(formula, at + new Vector3(0f, 0f, -0.5f * (mainHeight + (fontSize * unit))),
                    fontSize, BoardLabelStyle.RevealFormulaOutlineOf(reveal.StepTier));
                trail.Modulate = new Color(Visuals.ToColor(BoardLabelStyle.RevealFormulaColorOf(reveal.StepTier)), alpha);
                trail.OutlineModulate = new Color(Visuals.CoordinateLabelOutline, alpha);
                trail.NoDepthTest = true;
                trail.RenderPriority = 4;
                trail.OutlineRenderPriority = 3;
                _show.AddChild(trail);
            }
        }
    }

    public static float PopScaleOf(RevealDisplay reveal)
    {
        ArgumentNullException.ThrowIfNull(reveal);
        float rest = 1f - (Math.Clamp(reveal.StepPermille, 0, 1000) / 1000f);
        return 1f + (((NumberTierStyle.For(reveal.StepTier).PopPermille - 1000) / 1000f) * rest * rest);
    }
}
