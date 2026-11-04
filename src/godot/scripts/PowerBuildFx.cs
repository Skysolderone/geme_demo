using System.Collections.Generic;
using Godot;

namespace Siege.Godot;

/// <summary>
/// 数值构筑的特效层：卡片背后的光晕、迸发的火花、向外扩散的亮环与收束时的一下闪白。纯画面，不读任何对局数据。
/// </summary>
public sealed partial class PowerBuildFx : Control
{
    private struct Spark
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Life;
        public float MaxLife;
        public float Size;
        public Color Color;
    }

    private struct RingFx
    {
        public Vector2 Center;
        public Color Color;
        public float Life;
        public float MaxLife;
        public float Radius;
        public float Delay;
    }

    private readonly List<Spark> _sparks = [];
    private readonly List<RingFx> _rings = [];
    private Vector2 _glowAt;
    private float _glow;
    private float _glowScale = 1f;
    private Color _glowColor = Colors.White;
    private float _flash;
    private Color _flashColor = Colors.White;
    private float _time;

    /// <summary>本次构筑的阵营色（飞入 / 缩回时的光晕色）。</summary>
    public Color BaseColor { get; private set; } = Colors.White;

    /// <summary>
    /// 本层画哪一部分：<c>true</c> = 背层（光晕与旋转光芒，压在卡片之下）；<c>false</c> = 前层（火花、亮环、闪白，盖在卡片之上）。
    /// 同一组粒子数据只在前层推进，背层只读光晕参数。
    /// </summary>
    public bool Back { get; init; }

    public void Begin(Color faction)
    {
        Clear();
        BaseColor = faction;
    }

    public void Clear()
    {
        _sparks.Clear();
        _rings.Clear();
        _glow = 0f;
        _flash = 0f;
        QueueRedraw();
    }

    public void Burst(Vector2 at, int count, Color color, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (float)GD.RandRange(0d, Mathf.Tau);
            float v = speed * (0.35f + (GD.Randf() * 0.65f));
            float life = 0.5f + (GD.Randf() * 0.7f);
            _sparks.Add(new Spark
            {
                Pos = at + new Vector2((float)GD.RandRange(-30d, 30d), (float)GD.RandRange(-14d, 14d)),
                Vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * v,
                Life = life,
                MaxLife = life,
                Size = 1.5f + (GD.Randf() * 3f),
                Color = color.Lerp(Colors.White, GD.Randf() * 0.4f),
            });
        }
    }

    public void Ring(Vector2 at, Color color, float seconds, float radius, float delay = 0f) =>
        _rings.Add(new RingFx { Center = at, Color = color, Life = seconds, MaxLife = seconds, Radius = radius, Delay = delay });

    public void Flash(Color color)
    {
        _flash = 1f;
        _flashColor = color;
    }

    public void SetGlow(Vector2 at, float strength, Color color, float scale)
    {
        _glowAt = at;
        _glow = strength;
        _glowColor = color;
        _glowScale = scale;
    }

    public void Step(float dt)
    {
        _time += dt;
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark s = _sparks[i];
            s.Life -= dt;
            if (s.Life <= 0f)
            {
                _sparks.RemoveAt(i);
                continue;
            }

            s.Vel *= 1f - Mathf.Min(1f, dt * 2.2f);
            s.Vel += new Vector2(0f, 140f * dt);
            s.Pos += s.Vel * dt;
            _sparks[i] = s;
        }

        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            RingFx r = _rings[i];
            if (r.Delay > 0f)
            {
                r.Delay -= dt;
            }
            else
            {
                r.Life -= dt;
            }

            if (r.Life <= 0f)
            {
                _rings.RemoveAt(i);
                continue;
            }

            _rings[i] = r;
        }

        _flash = Mathf.Max(0f, _flash - (dt * 3.5f));
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Back)
        {
            DrawFront();
            return;
        }

        // 光晕：多层同心圆叠出柔边，随时间轻微呼吸。
        if (_glow > 0.01f)
        {
            float breathe = 1f + (0.04f * Mathf.Sin(_time * 5f));
            for (int i = 6; i >= 1; i--)
            {
                float radius = (110f + (i * 34f)) * _glowScale * breathe;
                float a = 0.05f * _glow * (7 - i) / 6f;
                DrawCircle(_glowAt, radius, new Color(_glowColor.R, _glowColor.G, _glowColor.B, a));
            }

            // 旋转的光芒：八道细长的三角从光晕中心向外。
            for (int k = 0; k < 8; k++)
            {
                float angle = (_time * 0.6f) + (k * Mathf.Tau / 8f);
                Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 side = new Vector2(-dir.Y, dir.X) * 18f * _glowScale;
                float len = 360f * _glowScale;
                var ray = new[] { _glowAt + side, _glowAt - side, _glowAt + (dir * len) };
                DrawColoredPolygon(ray, new Color(_glowColor.R, _glowColor.G, _glowColor.B, 0.07f * Mathf.Min(_glow, 1f)));
            }
        }
    }

    private void DrawFront()
    {
        foreach (RingFx r in _rings)
        {
            if (r.Delay > 0f)
            {
                continue;
            }

            float t = 1f - (r.Life / r.MaxLife);
            float radius = 40f + (r.Radius * (1f - ((1f - t) * (1f - t))));
            DrawArc(r.Center, radius, 0f, Mathf.Tau, 96, new Color(r.Color.R, r.Color.G, r.Color.B, 0.9f * (1f - t)), 3f + (10f * (1f - t)), true);
        }

        foreach (Spark s in _sparks)
        {
            float a = s.Life / s.MaxLife;
            var color = new Color(s.Color.R, s.Color.G, s.Color.B, a);
            DrawLine(s.Pos, s.Pos - (s.Vel * 0.03f), color, s.Size, true);
            DrawCircle(s.Pos, s.Size * 0.8f, color);
        }

        if (_flash > 0.01f)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(_flashColor.R, _flashColor.G, _flashColor.B, 0.22f * _flash));
        }
    }
}
