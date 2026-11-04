using System;
using System.Collections.Generic;
using Godot;
using Siege.Core.Board;
using Siege.Presentation.Hud;
using Siege.Presentation.Layers;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 个人势力卡片（左上回合信息下方）与结算后的地图势力飘字（power-map）。卡片的每一处文字取自 <see cref="PowerCard.Build"/>，
/// 飘字的数值与分段取自 <see cref="PowerBurst"/>；本文件只负责摆放、取色与特效：飘字跟着落子处在屏幕上的投影走（不占全屏、不压暗），
/// 本机玩家自己的那一手定格后飞一道光进卡片、卡片原位飙升。
/// </summary>
public sealed partial class Hud
{
    private VBoxContainer _leftColumn = null!;
    private PanelContainer _cardPanel = null!;
    private EmblemIcon _cardEmblem = null!;
    private Label _cardTitle = null!;
    private Label _cardValue = null!;
    private Label _cardLive = null!;
    private Label _cardBreakdown = null!;
    private VBoxContainer _cardForecast = null!;
    private Label _cardForecastMain = null!;
    private Label _cardForecastRange = null!;
    private Label _cardForecastRank = null!;
    private ViewerWorld? _cardWorld;

    // ---------- 地图飘字 ----------
    private Control _mapLabels = null!;
    private PowerBuildFx _mapFx = null!;
    private PowerBuildFx _topFx = null!;
    private readonly List<(PowerChange Change, Vector3 Anchor, bool ForViewer)> _armedBursts = [];
    private readonly List<ActiveBurst> _bursts = [];

    private const int CardValueFontPx = 30;

    /// <summary>一条正在播的飘字。</summary>
    private sealed class ActiveBurst
    {
        public required PowerBurst Burst { get; init; }

        public required Vector3 Anchor { get; init; }

        public required Color Faction { get; init; }

        public required Label Value { get; init; }

        public required Label Stage { get; init; }

        public double Ms { get; set; }

        public PowerBurstPhase LastPhase { get; set; } = PowerBurstPhase.Done;

        public string LastText { get; set; } = string.Empty;

        public double Pulse { get; set; }

        public PowerBurstFrame Frame { get; set; }

        public Vector2 LabelAt { get; set; }
    }

    /// <summary>是否有飘字正在播（截图与自检用）。</summary>
    public bool PowerBurstPlaying => _bursts.Count > 0;

    /// <summary>本机玩家飘字此刻的画面；没在播为 <c>null</c>（截图取景用）。</summary>
    public PowerBurstFrame? ViewerBurstFrameNow => _bursts.Find(b => b.Burst.ForViewer) is { } b ? b.Frame : null;

    /// <summary>左列：回合信息、势力卡片、信息层面板自上而下排，回合信息折行变高时下面跟着下移，不互相压住。</summary>
    private void BuildLeftColumn()
    {
        _leftColumn = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _leftColumn.AddThemeConstantOverride("separation", UiTheme.Space2);
        Ui.Anchor(_leftColumn, 0f, 0f, 14f, 14f, 392f, 14f);
        _root.AddChild(_leftColumn);
    }

    private void BuildPowerCard()
    {
        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space4, UiTheme.Space1);
        panel.CustomMinimumSize = new Vector2(308f, 0f);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cardPanel = panel;

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", UiTheme.Space2);
        _cardEmblem = new EmblemIcon { CustomMinimumSize = new Vector2(18f, 18f), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        head.AddChild(_cardEmblem);
        _cardTitle = Ui.Text("—", Ui.PanelBorder, UiTheme.Caption);
        head.AddChild(_cardTitle);
        body.AddChild(head);

        var valueRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        valueRow.AddThemeConstantOverride("separation", UiTheme.Space3);
        valueRow.AddChild(Ui.Text("势力", Ui.MutedText, UiTheme.Caption));
        _cardValue = Ui.Text("0", Ui.InfoText, CardValueFontPx);
        _cardValue.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.6f));
        _cardValue.AddThemeConstantOverride("outline_size", 4);
        _cardValue.VerticalAlignment = VerticalAlignment.Center;
        valueRow.AddChild(_cardValue);
        _cardLive = Ui.Text(string.Empty, Ui.InfoText, UiTheme.BodyFontPx + 2);
        _cardLive.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        valueRow.AddChild(_cardLive);
        body.AddChild(valueRow);

        _cardBreakdown = Ui.Text(string.Empty, Ui.MutedText, UiTheme.Caption);
        body.AddChild(_cardBreakdown);

        _cardForecast = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _cardForecast.AddThemeConstantOverride("separation", UiTheme.Space0);
        _cardForecast.AddChild(Ui.Separator());
        _cardForecastMain = Ui.Text(string.Empty, Ui.InfoText, UiTheme.BodyFontPx + 2);
        _cardForecast.AddChild(_cardForecastMain);
        _cardForecastRange = Ui.Text(string.Empty, Ui.PanelBorder, UiTheme.Body);
        _cardForecast.AddChild(_cardForecastRange);
        _cardForecastRank = Ui.Text(string.Empty, Ui.MutedText, UiTheme.Caption);
        _cardForecast.AddChild(_cardForecastRank);
        body.AddChild(_cardForecast);

        _leftColumn.AddChild(panel);
        panel.Visible = false;
    }

    /// <summary>刷新卡片：<paramref name="mask"/> 里有本机玩家的势力显示时与排名栏同步滚动；本机飘字在播时卡片数字听飘字的（光飞到之前旧值、飙升段滚到新值）。</summary>
    private void RefreshPowerCard(ViewerWorld world, ShowMask mask)
    {
        _cardWorld = world;
        var power = (PowerLayerContent)world.Layer(TacticalLayer.Power);
        PowerDisplay? display = mask.Power.TryGetValue(world.Viewer, out PowerDisplay? shown) ? shown : null;
        if (PowerCard.Build(power, world.Viewer, world.Preview(), display) is not { } card)
        {
            _cardPanel.Visible = false;
            return;
        }

        _cardPanel.Visible = true;
        FactionStyle faction = FactionTable.For(card.Player);
        Color factionColor = Visuals.ToColor(faction.Primary);
        _cardEmblem.Set(faction.Emblem, factionColor);
        _cardTitle.Text = card.StatusText is { } status ? $"{card.TitleText}　{status}" : card.TitleText;
        _cardValue.Text = card.ValueText;
        _cardValue.Scale = Vector2.One;
        _cardValue.AddThemeColorOverride("font_color", Ui.InfoText);
        _cardLive.Text = card.LiveDeltaText ?? string.Empty;
        _cardLive.AddThemeColorOverride("font_color", card.LiveDeltaNegative ? Ui.DangerText : factionColor.Lightened(0.2f));
        _cardBreakdown.Text = card.BreakdownText;
        _cardBreakdown.Visible = card.BreakdownText.Length > 0;

        _cardForecast.Visible = card.Forecast != PowerForecastKind.None;
        _cardForecastMain.Text = card.ForecastText ?? string.Empty;
        Color forecastColor = card.Forecast == PowerForecastKind.Invalid ? Ui.MutedText
            : card.ForecastNegative ? Ui.DangerText
            : Visuals.ToColor(NumberTierStyle.For(card.ForecastTier).RevealColor);
        _cardForecastMain.AddThemeColorOverride("font_color", forecastColor);
        _cardForecastRange.Text = card.ForecastRangeText ?? string.Empty;
        _cardForecastRange.Visible = card.ForecastRangeText is not null;
        _cardForecastRank.Text = card.ForecastRankText ?? string.Empty;
        _cardForecastRank.Visible = card.ForecastRankText is not null;

        if (_bursts.Find(b => b.Burst.ForViewer) is { } mine)
        {
            ApplyBurstToCard(mine, 0d);
        }
    }

    // ---------- 地图飘字：搭建 ----------

    /// <summary>压在面板之下的两层（飘字文字与地图特效）：先于全部面板加入，界面面板永远盖在它们上面。</summary>
    private void BuildPowerBurstUnderlay()
    {
        _mapFx = new PowerBuildFx { MouseFilter = Control.MouseFilterEnum.Ignore };
        _mapFx.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_mapFx);
        _mapLabels = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _mapLabels.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_mapLabels);
    }

    /// <summary>盖在面板之上的一层：飞向卡片的光与卡片周围的火花。</summary>
    private void BuildPowerBurstOverlay()
    {
        _topFx = new PowerBuildFx { MouseFilter = Control.MouseFilterEnum.Ignore };
        _topFx.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_topFx);
    }

    private static Label OutlinedLabel(int size, int outline)
    {
        Label label = Ui.Text(string.Empty, Ui.InfoText, size);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
    }

    // ---------- 地图飘字：触发 ----------

    /// <summary>
    /// 一次正常时长的结算演出开始：行动玩家这一手让自己势力上涨就挂上一条飘字（<paramref name="anchor"/> = 落子处在场景里的中心），
    /// 等演出走到势力节拍（或演出结束）再开演。
    /// </summary>
    public void ArmPowerBurst(PowerChange change, Vector3 anchor, bool forViewer)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (PowerBurst.Wants(change))
        {
            _armedBursts.Add((change, anchor, forViewer));
        }
    }

    /// <summary>结算演出播完：还没开演的飘字此刻开演。</summary>
    public void ShowFinishedForPowerBurst()
    {
        foreach ((PowerChange change, Vector3 anchor, bool forViewer) in _armedBursts)
        {
            StartPowerBurst(change, anchor, forViewer);
        }

        _armedBursts.Clear();
    }

    /// <summary>演出被截断（弃赛）：全部飘字取消。</summary>
    public void CancelPowerBursts()
    {
        _armedBursts.Clear();
        foreach (ActiveBurst b in _bursts)
        {
            b.Value.QueueFree();
            b.Stage.QueueFree();
        }

        _bursts.Clear();
        _mapFx?.Clear();
        _topFx?.Clear();
    }

    private void TryStartArmedBursts(ShowMask mask)
    {
        for (int i = _armedBursts.Count - 1; i >= 0; i--)
        {
            (PowerChange change, Vector3 anchor, bool forViewer) = _armedBursts[i];
            if (mask.Power.TryGetValue(change.Player, out PowerDisplay? display) && display.Stage != PowerStage.Pending)
            {
                _armedBursts.RemoveAt(i);
                StartPowerBurst(change, anchor, forViewer);
            }
        }
    }

    private void StartPowerBurst(PowerChange change, Vector3 anchor, bool forViewer)
    {
        int tier = change.TotalTier;
        Label value = OutlinedLabel(34 + (tier * 6), 8);
        Label stage = OutlinedLabel(UiTheme.BodyFontPx + 4, 5);
        _mapLabels.AddChild(value);
        _mapLabels.AddChild(stage);
        _bursts.Add(new ActiveBurst
        {
            Burst = new PowerBurst(change, forViewer),
            Anchor = anchor,
            Faction = Visuals.ToColor(FactionTable.For(change.Player).Primary),
            Value = value,
            Stage = stage,
        });
    }

    // ---------- 地图飘字：逐帧 ----------

    /// <summary>
    /// 逐帧推进全部飘字（由 GameRoot 在推进对局之后调用，截图冻结时不调用）。<paramref name="project"/> 把场景坐标投影到屏幕（在镜头背后为 <c>null</c>）。
    /// 返回本帧该给镜头的轻震进度（‰）；不震为 <c>null</c>。
    /// </summary>
    public int? StepPowerBursts(double delta, Func<Vector3, Vector2?> project)
    {
        ArgumentNullException.ThrowIfNull(project);
        // 没有飘字时也推进特效层：最后一条播完后火花与亮环还要再飞一会才散尽。
        _mapFx.BeginFrame();
        _topFx.BeginFrame();
        if (_bursts.Count == 0)
        {
            _mapFx.Step((float)delta);
            _topFx.Step((float)delta);
            return null;
        }

        int? shake = null;
        bool restoreCard = false;
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            ActiveBurst b = _bursts[i];
            b.Ms += delta * 1000d;
            b.Frame = b.Burst.At((int)b.Ms);
            if (b.Frame.Phase == PowerBurstPhase.Done)
            {
                b.Value.QueueFree();
                b.Stage.QueueFree();
                _bursts.RemoveAt(i);
                restoreCard |= b.Burst.ForViewer;
                continue;
            }

            StepBurst(b, delta, project(b.Anchor));
            if (b.Burst.ShakePermilleAt(b.Frame) is int s)
            {
                shake = Math.Max(shake ?? 0, s);
            }
        }

        _mapFx.Step((float)delta);
        _topFx.Step((float)delta);
        if (restoreCard && _cardWorld is { } world)
        {
            // 卡片回到视图模型的值（此刻已是结算后快照）；若另一场演出正在播，下一帧 RefreshShow 会再按遮罩刷新。
            RefreshPowerCard(world, ShowMask.Empty);
        }

        return shake;
    }

    private void StepBurst(ActiveBurst b, double delta, Vector2? projected)
    {
        PowerBurstFrame frame = b.Frame;
        float p = frame.PhasePermille / 1000f;
        bool entering = frame.Phase != b.LastPhase;
        b.LastPhase = frame.Phase;
        Color tierColor = Visuals.ToColor(NumberTierStyle.For(frame.StageTier).RevealColor);
        // 一档的档位色近白：光柱与亮环改用阵营色，数字仍按档位色。
        Color accent = frame.StageTier <= NumberTier.Lowest ? b.Faction : tierColor;

        Rect2 view = new(Vector2.Zero, _root.Size);
        bool onScreen = projected is { } ps && view.Grow(120f).HasPoint(ps);
        Vector2 ground = projected ?? b.LabelAt;

        // 飘字从地面往上升：滚动段内升到顶，之后停在那里。
        int rolling = b.Burst.Change.TerritoryDelta.IsZero || b.Burst.Change.GroupDelta.IsZero ? 1 : 2;
        float rise = frame.Phase switch
        {
            PowerBurstPhase.Territory => p / rolling,
            PowerBurstPhase.Group => rolling == 1 ? p : 0.5f + (p / 2f),
            _ => 1f,
        };
        Vector2 labelAt = ground - new Vector2(0f, 60f + (60f * EaseOut(rise)));
        b.LabelAt = labelAt;

        float alpha = frame.Phase switch
        {
            PowerBurstPhase.Fly or PowerBurstPhase.Fade => 1f - p,
            PowerBurstPhase.Soar => 0f,
            _ => 1f,
        };

        // 数字：每次变化弹一下；收束段先放大再回落。
        if (frame.ShownDeltaText != b.LastText)
        {
            b.LastText = frame.ShownDeltaText;
            b.Pulse = 1d;
        }

        b.Pulse = Math.Max(0d, b.Pulse - (delta * 8d));
        float pop = 1f + (0.12f * (float)b.Pulse);
        if (frame.Phase == PowerBurstPhase.Finale)
        {
            pop += (NumberTierStyle.For(b.Burst.Tier).PopPermille - 1000) / 1000f * (1f - EaseOut(p));
        }

        Place(b.Value, frame.ShownDeltaText, labelAt, pop, frame.Phase is PowerBurstPhase.Territory or PowerBurstPhase.Group ? tierColor : Visuals.ToColor(NumberTierStyle.For(b.Burst.Tier).RevealColor), alpha, onScreen);
        string? stageText = frame.StageText
            ?? (frame.Phase is PowerBurstPhase.Finale or PowerBurstPhase.Hold && b.Burst.RankText is { } rank ? rank : null);
        Place(b.Stage, stageText ?? string.Empty, labelAt + new Vector2(0f, 40f + (b.Burst.Tier * 3f)), 1f, b.Faction.Lightened(0.3f), alpha * (stageText is null ? 0f : 1f), onScreen);

        if (onScreen)
        {
            float beam = frame.Phase switch
            {
                PowerBurstPhase.Territory or PowerBurstPhase.Group => 0.55f + (0.45f * p),
                PowerBurstPhase.Finale => 1.3f - (0.3f * p),
                PowerBurstPhase.Hold => 1f,
                PowerBurstPhase.Fly or PowerBurstPhase.Fade => 1f - p,
                _ => 0f,
            };
            if (beam > 0.01f)
            {
                _mapFx.AddBeam(ground, accent, beam, 150f + (b.Burst.Tier * 28f));
            }

            if (entering)
            {
                switch (frame.Phase)
                {
                    case PowerBurstPhase.Territory:
                    case PowerBurstPhase.Group:
                        _mapFx.Burst(ground, 10 + (frame.StageTier * 6), accent, 260f, upward: true);
                        _mapFx.Ring(ground, accent, 0.45f, 70f);
                        break;
                    case PowerBurstPhase.Finale:
                        int tier = b.Burst.Tier;
                        NumberTierStyle style = NumberTierStyle.For(tier);
                        Color final = Visuals.ToColor(style.RevealColor);
                        _mapFx.Burst(labelAt, 26 + (tier * 14), final, 300f + (tier * 45f));
                        _mapFx.Burst(ground, 16 + (tier * 4), b.Faction, 240f, upward: true);
                        for (int r = 0; r <= style.RingCount + 1; r++)
                        {
                            _mapFx.Ring(ground, tier <= NumberTier.Lowest ? b.Faction : final, 0.55f + (r * 0.18f), 110f + (r * 60f) + (tier * 12f), r * 0.1f);
                        }

                        break;
                }
            }

            if (frame.Phase is PowerBurstPhase.Territory or PowerBurstPhase.Group && GD.Randf() < 0.5f)
            {
                _mapFx.Burst(ground + new Vector2((float)GD.RandRange(-24d, 24d), 0f), 1, accent, 200f, upward: true);
            }

            if (frame.Phase == PowerBurstPhase.Finale)
            {
                _mapFx.AddBloom(labelAt, Visuals.ToColor(NumberTierStyle.For(b.Burst.Tier).RevealColor), 50f + (b.Burst.Tier * 10f), 1f - p);
            }
        }

        if (b.Burst.ForViewer)
        {
            StepViewerFlight(b, frame, p, entering, labelAt, accent);
            ApplyBurstToCard(b, delta);
        }
    }

    /// <summary>本机玩家：定格后一道光从飘字飞进卡片的势力数字，落到时卡片周围炸一圈火花。</summary>
    private void StepViewerFlight(ActiveBurst b, PowerBurstFrame frame, float p, bool entering, Vector2 from, Color accent)
    {
        Rect2 card = _cardValue.GetGlobalRect();
        Vector2 target = card.GetCenter();
        if (frame.Phase == PowerBurstPhase.Fly)
        {
            // 光从屏幕外的落子处飞来时，起点夹到屏幕边缘（镜头不为飘字移动，裁决 7）。
            Vector2 start = new(Mathf.Clamp(from.X, 0f, _root.Size.X), Mathf.Clamp(from.Y, 0f, _root.Size.Y));
            const int trail = 12;
            var points = new Vector2[trail];
            for (int k = 0; k < trail; k++)
            {
                float q = Mathf.Clamp(p - ((trail - 1 - k) * 0.035f), 0f, 1f);
                float e = q * q;
                Vector2 straight = start.Lerp(target, e);
                Vector2 normal = (target - start).Orthogonal().Normalized();
                points[k] = straight + (normal * Mathf.Sin(Mathf.Pi * e) * 90f);
            }

            _topFx.AddComet(points, accent.Lightened(0.2f));
        }

        if (entering && frame.Phase == PowerBurstPhase.Soar)
        {
            _topFx.Burst(target, 30 + (b.Burst.Tier * 8), accent, 260f);
            _topFx.Ring(target, accent, 0.5f, 90f);
        }

        if (frame.Phase == PowerBurstPhase.Soar)
        {
            _mapFx.AddGlow(_cardPanel.GetGlobalRect(), accent, 1f - (0.6f * p));
            if (GD.Randf() < 0.6f)
            {
                Rect2 r = _cardPanel.GetGlobalRect();
                Vector2 edge = new((float)GD.RandRange(r.Position.X, r.End.X), r.End.Y - 4f);
                _topFx.Burst(edge, 1, accent, 160f, upward: true);
            }
        }
    }

    /// <summary>本机飘字在播时卡片的数字：光飞到之前保持旧值，飙升段滚到新值、按档位着色并弹动，旁边写本手总增量。</summary>
    private void ApplyBurstToCard(ActiveBurst b, double delta)
    {
        PowerBurstFrame frame = b.Frame;
        _cardValue.Text = frame.CardValueText;
        _cardBreakdown.Visible = false;
        if (frame.Phase == PowerBurstPhase.Soar)
        {
            float p = frame.PhasePermille / 1000f;
            Color tier = Visuals.ToColor(NumberTierStyle.For(b.Burst.Tier).RevealColor);
            _cardValue.AddThemeColorOverride("font_color", tier.Lerp(Ui.InfoText, p * p));
            _cardValue.PivotOffset = new Vector2(0f, _cardValue.Size.Y * 0.5f);
            float s = 1f + (0.35f * (1f - EaseOut(p))) + (0.05f * Mathf.Sin(p * 40f) * (1f - p));
            _cardValue.Scale = new Vector2(s, s);
            _cardLive.Text = b.Burst.Change.DeltaText;
            _cardLive.AddThemeColorOverride("font_color", tier);
        }
        else
        {
            _cardValue.Scale = Vector2.One;
            _cardLive.Text = string.Empty;
        }
    }

    private static void Place(Label label, string text, Vector2 center, float scale, Color color, float alpha, bool visible)
    {
        label.Visible = visible && alpha > 0.01f && text.Length > 0;
        if (!label.Visible)
        {
            return;
        }

        label.Text = text;
        label.ResetSize();
        label.PivotOffset = label.Size * 0.5f;
        label.Position = center - (label.Size * 0.5f);
        label.Scale = new Vector2(scale, scale);
        label.AddThemeColorOverride("font_color", color);
        label.Modulate = new Color(1f, 1f, 1f, alpha);
    }

    private static float EaseOut(float p) => 1f - ((1f - p) * (1f - p));
}
