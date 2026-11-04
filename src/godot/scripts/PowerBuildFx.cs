using System.Collections.Generic;
using Godot;

namespace Siege.Godot;

/// <summary>
/// 势力飘字的特效层（power-map）：火花与亮环是持续粒子（自己推进寿命）；光柱、卡片外发光、飞行光束与局部亮光是逐帧绘制的即时图元
/// （每帧 <see cref="BeginFrame"/> 清空，由 HUD 按当帧画面重新加入）。纯画面，不读任何对局数据。
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
        public float Gravity;
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

    private readonly record struct Beam(Vector2 Base, Color Color, float Strength, float Height);

    private readonly record struct Glow(Rect2 Rect, Color Color, float Strength);

    private readonly record struct Comet(Vector2[] Trail, Color Color);

    private readonly record struct Bloom(Vector2 Center, Color Color, float Radius, float Strength);

    private readonly List<Spark> _sparks = [];
    private readonly List<RingFx> _rings = [];
    private readonly List<Beam> _beams = [];
    private readonly List<Glow> _glows = [];
    private readonly List<Comet> _comets = [];
    private readonly List<Bloom> _blooms = [];
    private float _time;

    /// <summary>清掉全部粒子与即时图元。</summary>
    public void Clear()
    {
        _sparks.Clear();
        _rings.Clear();
        BeginFrame();
        QueueRedraw();
    }

    /// <summary>新的一帧：清空即时图元（粒子保留）。</summary>
    public void BeginFrame()
    {
        _beams.Clear();
        _glows.Clear();
        _comets.Clear();
        _blooms.Clear();
    }

    /// <summary>一簇火花：从 <paramref name="at"/> 向四周（<paramref name="upward"/> 为真时偏向上方）迸出。</summary>
    public void Burst(Vector2 at, int count, Color color, float speed, bool upward = false)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = upward ? (float)GD.RandRange(-Mathf.Pi * 0.95f, -Mathf.Pi * 0.05f) : (float)GD.RandRange(0d, Mathf.Tau);
            float v = speed * (0.35f + (GD.Randf() * 0.65f));
            float life = 0.45f + (GD.Randf() * 0.6f);
            _sparks.Add(new Spark
            {
                Pos = at + new Vector2((float)GD.RandRange(-16d, 16d), (float)GD.RandRange(-8d, 8d)),
                Vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * v,
                Life = life,
                MaxLife = life,
                Size = 1.5f + (GD.Randf() * 2.5f),
                Color = color.Lerp(Colors.White, GD.Randf() * 0.45f),
                Gravity = upward ? 60f : 160f,
            });
        }
    }

    /// <summary>一圈向外扩散的亮环。</summary>
    public void Ring(Vector2 at, Color color, float seconds, float radius, float delay = 0f) =>
        _rings.Add(new RingFx { Center = at, Color = color, Life = seconds, MaxLife = seconds, Radius = radius, Delay = delay });

    /// <summary>本帧：从 <paramref name="baseAt"/> 向上的一道阵营色光柱。</summary>
    public void AddBeam(Vector2 baseAt, Color color, float strength, float height) => _beams.Add(new Beam(baseAt, color, strength, height));

    /// <summary>本帧：矩形外发光（个人卡片飙升时贴着卡片外缘）。</summary>
    public void AddGlow(Rect2 rect, Color color, float strength) => _glows.Add(new Glow(rect, color, strength));

    /// <summary>本帧：一道飞行光束（头在 <paramref name="trail"/> 末尾，越往后越细越淡）。</summary>
    public void AddComet(Vector2[] trail, Color color) => _comets.Add(new Comet(trail, color));

    /// <summary>本帧：一团局部亮光（收束时飘字背后）。</summary>
    public void AddBloom(Vector2 center, Color color, float radius, float strength) => _blooms.Add(new Bloom(center, color, radius, strength));

    /// <summary>推进粒子寿命并请求重画。</summary>
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

            s.Vel *= 1f - Mathf.Min(1f, dt * 2.4f);
            s.Vel += new Vector2(0f, s.Gravity * dt);
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

        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (Glow g in _glows)
        {
            for (int i = 5; i >= 1; i--)
            {
                float a = 0.11f * g.Strength * (6 - i) / 5f;
                DrawRect(g.Rect.Grow(i * 5f), new Color(g.Color, a));
            }
        }

        foreach (Beam b in _beams)
        {
            // 光柱：底宽顶窄、底亮顶透明的四边形，外加一道更宽更淡的外晕；底部一个压扁的光斑贴在地面上。
            float breathe = 1f + (0.08f * Mathf.Sin(_time * 7f));
            for (int layer = 0; layer < 2; layer++)
            {
                float w = (layer == 0 ? 34f : 14f) * breathe;
                float alpha = (layer == 0 ? 0.18f : 0.45f) * b.Strength;
                Vector2 top = b.Base - new Vector2(0f, b.Height);
                Vector2[] points = [b.Base + new Vector2(-w, 0f), b.Base + new Vector2(w, 0f), top + new Vector2(w * 0.35f, 0f), top - new Vector2(w * 0.35f, 0f)];
                Color[] colors = [new Color(b.Color, alpha), new Color(b.Color, alpha), new Color(b.Color, 0f), new Color(b.Color, 0f)];
                DrawPolygon(points, colors);
            }

            DrawSetTransform(b.Base, 0f, new Vector2(1f, 0.38f));
            DrawCircle(Vector2.Zero, 46f * breathe, new Color(b.Color, 0.22f * b.Strength));
            DrawCircle(Vector2.Zero, 24f * breathe, new Color(b.Color.Lightened(0.4f), 0.35f * b.Strength));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        foreach (Bloom bl in _blooms)
        {
            for (int i = 4; i >= 1; i--)
            {
                DrawCircle(bl.Center, bl.Radius * (0.4f + (i * 0.25f)), new Color(bl.Color, 0.08f * bl.Strength * (5 - i)));
            }
        }

        foreach (RingFx r in _rings)
        {
            if (r.Delay > 0f)
            {
                continue;
            }

            float t = 1f - (r.Life / r.MaxLife);
            float radius = 12f + (r.Radius * (1f - ((1f - t) * (1f - t))));
            DrawArc(r.Center, radius, 0f, Mathf.Tau, 72, new Color(r.Color, 0.9f * (1f - t)), 2f + (7f * (1f - t)), true);
        }

        foreach (Comet c in _comets)
        {
            int n = c.Trail.Length;
            for (int i = 1; i < n; i++)
            {
                float k = i / (float)(n - 1);
                DrawLine(c.Trail[i - 1], c.Trail[i], new Color(c.Color, 0.15f + (0.75f * k)), 2f + (9f * k), true);
            }

            if (n > 0)
            {
                Vector2 head = c.Trail[n - 1];
                DrawCircle(head, 16f, new Color(c.Color, 0.25f));
                DrawCircle(head, 8f, new Color(c.Color.Lightened(0.5f), 0.9f));
                DrawCircle(head, 4f, Colors.White);
            }
        }

        foreach (Spark s in _sparks)
        {
            var color = new Color(s.Color, s.Life / s.MaxLife);
            DrawLine(s.Pos, s.Pos - (s.Vel * 0.03f), color, s.Size, true);
            DrawCircle(s.Pos, s.Size * 0.8f, color);
        }
    }
}
