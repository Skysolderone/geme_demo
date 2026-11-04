using System;
using Godot;
using Siege.Core.Board;
using Siege.Presentation.Hud;
using Siege.Presentation.Layers;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 个人势力卡片（左上回合信息下方）与确认落子后的数值构筑演出。卡片的每一处文字取自 <see cref="PowerCard.Build"/>，
/// 构筑的数值与分段取自 <see cref="PowerCardBuild"/>；本文件只负责摆放、取色与特效（飞入放大、滚动、粒子、亮环、轻震、缩回）。
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

    // ---------- 数值构筑 ----------
    private Control _buildRoot = null!;
    private ColorRect _buildDim = null!;
    private PowerBuildFx _buildFx = null!;
    private PowerBuildFx _buildFxBack = null!;
    private PanelContainer _buildCard = null!;
    private StyleBoxFlat _buildCardStyle = null!;
    private EmblemIcon _buildEmblem = null!;
    private Label _buildTitle = null!;
    private Label _buildValue = null!;
    private Label _buildStage = null!;
    private Label _buildDelta = null!;
    private Label _buildRank = null!;

    private PowerChange? _armedBuild;
    private PowerCardBuild? _build;
    private double _buildMs;
    private PowerBuildPhase _lastPhase = PowerBuildPhase.Done;
    private string _lastValueText = string.Empty;
    private double _valuePulse;
    private double _shakeLeft;
    private double _shakeTotal;

    private const float BuildCardWidth = 520f;
    private const float BuildCardHeight = 300f;
    private const int BuildValueFontPx = 88;
    private const int CardValueFontPx = 30;

    /// <summary>数值构筑是否正在播放（截图与测试自证用）。</summary>
    public bool PowerBuildPlaying => _build is not null;

    /// <summary>数值构筑此刻的画面；没在播为 <c>null</c>（截图取景用）。</summary>
    public PowerBuildFrame? PowerBuildFrameNow => _build?.At((int)_buildMs);

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

    /// <summary>刷新卡片：<paramref name="mask"/> 里有本机玩家的势力显示时与排名栏同步滚动；构筑播放期间卡片让位给中央的大卡片。</summary>
    private void RefreshPowerCard(ViewerWorld world, ShowMask mask)
    {
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

        // 构筑播放中：小卡片透明，由中央大卡片代表它（缩回时落到它的位置再显形）。
        _cardPanel.Modulate = new Color(1f, 1f, 1f, _build is null ? 1f : 0f);
    }

    // ---------- 数值构筑：搭建 ----------

    private void BuildPowerBuildOverlay()
    {
        _buildRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _buildRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_buildRoot);

        _buildDim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.06f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _buildDim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _buildRoot.AddChild(_buildDim);

        _buildFxBack = new PowerBuildFx { MouseFilter = Control.MouseFilterEnum.Ignore, Back = true };
        _buildFxBack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _buildRoot.AddChild(_buildFxBack);

        (PanelContainer panel, VBoxContainer body) = Ui.Panel(PanelTier.Primary, UiTheme.Space5, UiTheme.Space1);
        _buildCard = panel;
        // 大卡片自带一只样式盒：边框与外发光逐帧取本段档位色（白 → 金 → 橙 → 红）。
        _buildCardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.09f, 0.13f, 0.94f),
            ShadowSize = 26,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 18,
            ContentMarginBottom = 18,
        };
        _buildCardStyle.SetBorderWidthAll(3);
        _buildCardStyle.SetCornerRadiusAll(12);
        panel.AddThemeStyleboxOverride("panel", _buildCardStyle);
        panel.Size = new Vector2(BuildCardWidth, BuildCardHeight);
        panel.CustomMinimumSize = new Vector2(BuildCardWidth, BuildCardHeight);
        panel.PivotOffset = new Vector2(BuildCardWidth, BuildCardHeight) * 0.5f;
        panel.TooltipText = "点击跳过";
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                SkipPowerBuild();
            }
        };
        body.Alignment = BoxContainer.AlignmentMode.Center;

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        head.AddThemeConstantOverride("separation", UiTheme.Space2);
        _buildEmblem = new EmblemIcon { CustomMinimumSize = new Vector2(26f, 26f), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        head.AddChild(_buildEmblem);
        _buildTitle = Ui.Text("势力构筑", Ui.PanelBorder, UiTheme.BodyFontPx + 4);
        head.AddChild(_buildTitle);
        body.AddChild(head);

        _buildValue = CenteredLabel(BuildValueFontPx, 14);
        _buildValue.CustomMinimumSize = new Vector2(0f, BuildValueFontPx + 16f);
        body.AddChild(_buildValue);
        _buildStage = CenteredLabel(UiTheme.BodyFontPx * 2, 8);
        body.AddChild(_buildStage);
        _buildDelta = CenteredLabel(UiTheme.BodyFontPx + 8, 6);
        body.AddChild(_buildDelta);
        _buildRank = CenteredLabel(UiTheme.BodyFontPx + 4, 5);
        body.AddChild(_buildRank);

        _buildRoot.AddChild(panel);

        // 前层：火花、亮环、闪白盖在大卡片之上。
        _buildFx = new PowerBuildFx { MouseFilter = Control.MouseFilterEnum.Ignore };
        _buildFx.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _buildRoot.AddChild(_buildFx);
    }

    private static Label CenteredLabel(int size, int outline)
    {
        Label label = Ui.Text(string.Empty, Ui.InfoText, size);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
    }

    // ---------- 数值构筑：触发 ----------

    /// <summary>本机确认落子、结算节拍里自己的势力上涨：先挂上，等结算演出播到势力节拍（或演出结束）再开演。</summary>
    public void ArmPowerBuild(PowerChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _armedBuild = PowerCardBuild.Wants(change) ? change : null;
    }

    /// <summary>结算演出播完：挂着的构筑若还没开演（零时长或没有走到势力节拍）此刻开演。</summary>
    public void ShowFinishedForPowerBuild()
    {
        if (_armedBuild is { } armed)
        {
            StartPowerBuild(armed);
        }
    }

    /// <summary>演出被截断（弃赛）：构筑一并取消。</summary>
    public void CancelPowerBuild()
    {
        _armedBuild = null;
        EndPowerBuild();
    }

    private void TryStartArmedBuild(ShowMask mask)
    {
        if (_armedBuild is not { } armed || !mask.Power.TryGetValue(armed.Player, out PowerDisplay? display) || display.Stage == PowerStage.Pending)
        {
            return;
        }

        StartPowerBuild(armed);
    }

    private void StartPowerBuild(PowerChange change)
    {
        _armedBuild = null;
        _build = new PowerCardBuild(change);
        _buildMs = 0d;
        _lastPhase = PowerBuildPhase.Done;
        _lastValueText = string.Empty;
        _shakeLeft = 0d;

        FactionStyle faction = FactionTable.For(change.Player);
        Color factionColor = Visuals.ToColor(faction.Primary);
        _buildEmblem.Set(faction.Emblem, factionColor);
        _buildTitle.Text = $"{faction.Name} · 势力构筑";
        _buildFx.Begin(factionColor);
        _buildFxBack.Begin(factionColor);
        _buildRoot.Visible = true;
        _cardPanel.Modulate = new Color(1f, 1f, 1f, 0f);
        StepPowerBuild(0d);
    }

    private void SkipPowerBuild()
    {
        if (_build is { } build && _buildMs < build.ExitStartMs)
        {
            _buildMs = build.ExitStartMs;
        }
    }

    private void EndPowerBuild()
    {
        _build = null;
        if (_buildRoot is not null)
        {
            _buildRoot.Visible = false;
            _buildFx.Clear();
            _buildFxBack.Clear();
        }

        if (_cardPanel is not null)
        {
            _cardPanel.Modulate = Colors.White;
        }
    }

    public override void _Process(double delta)
    {
        if (_build is not null)
        {
            StepPowerBuild(delta);
        }
    }

    // ---------- 数值构筑：逐帧 ----------

    private void StepPowerBuild(double delta)
    {
        PowerCardBuild build = _build!;
        _buildMs += delta * 1000d;
        PowerBuildFrame frame = build.At((int)_buildMs);
        if (frame.Phase == PowerBuildPhase.Done)
        {
            EndPowerBuild();
            return;
        }

        float p = frame.PhasePermille / 1000f;
        Vector2 screen = _root.Size;
        Vector2 center = screen * 0.5f - new Vector2(0f, 40f);
        Rect2 small = _cardPanel.GetGlobalRect();
        Vector2 smallCenter = small.Size.X > 1f ? small.GetCenter() : new Vector2(160f, 160f);
        float smallScale = small.Size.X > 1f ? Mathf.Clamp(small.Size.X / BuildCardWidth, 0.3f, 1f) : 0.6f;

        bool entering = frame.Phase != _lastPhase;
        NumberTierStyle tierStyle = NumberTierStyle.For(frame.StageTier);
        Color tierColor = Visuals.ToColor(tierStyle.RevealColor);

        // 位置与缩放：飞入（缓出）、缩回（缓入），其余停在中央。
        Vector2 at = center;
        float scale = 1f;
        float dim = 0.5f;
        float alpha = 1f;
        switch (frame.Phase)
        {
            case PowerBuildPhase.Enter:
                float e = EaseOutBack(p);
                at = smallCenter.Lerp(center, Mathf.Clamp(e, 0f, 1.1f));
                scale = Mathf.Lerp(smallScale, 1f, e);
                dim = 0.5f * p;
                break;
            case PowerBuildPhase.Exit:
                float x = p * p;
                at = center.Lerp(smallCenter, x);
                scale = Mathf.Lerp(1f, smallScale, x);
                dim = 0.5f * (1f - p);
                alpha = 1f - Mathf.Clamp((p - 0.7f) / 0.3f, 0f, 1f);
                break;
        }

        // 轻震：收束段起按档位震，四五档更猛。
        Vector2 shake = Vector2.Zero;
        if (_shakeLeft > 0d)
        {
            _shakeLeft -= delta;
            float k = (float)(_shakeLeft / Math.Max(_shakeTotal, 0.001d));
            float amp = 4f + (frame.StageTier * 2.5f);
            shake = new Vector2((float)GD.RandRange(-amp, amp), (float)GD.RandRange(-amp, amp)) * k;
        }

        _buildCard.Scale = new Vector2(scale, scale);
        _buildCard.Position = at - (_buildCard.Size * 0.5f) + shake;
        _buildCard.Modulate = new Color(1f, 1f, 1f, alpha);
        _buildDim.Color = new Color(0.02f, 0.03f, 0.06f, dim);

        // 数字：每次变化弹一下；颜色随本段档位（白 → 金 → 橙 → 红），收束后整体取总增量档位。
        if (frame.ValueText != _lastValueText)
        {
            _lastValueText = frame.ValueText;
            _valuePulse = 1d;
        }

        _valuePulse = Math.Max(0d, _valuePulse - (delta * 9d));
        _buildValue.Text = frame.ValueText;
        _buildValue.PivotOffset = _buildValue.Size * 0.5f;
        float pop = frame.Phase is PowerBuildPhase.Territory or PowerBuildPhase.Group
            ? 1f + (0.06f * (float)_valuePulse) + ((tierStyle.PopPermille - 1000) / 1000f * (1f - p) * 0.35f)
            : frame.Phase == PowerBuildPhase.Finale ? 1f + ((tierStyle.PopPermille - 1000) / 1000f * (1f - EaseOut(p))) : 1f;
        _buildValue.Scale = new Vector2(pop, pop);
        Color valueColor = frame.Phase is PowerBuildPhase.Enter ? Ui.InfoText : tierColor;
        if (frame.Phase == PowerBuildPhase.Finale)
        {
            valueColor = Colors.White.Lerp(tierColor, Mathf.Clamp(p * 2f, 0f, 1f));
        }

        _buildValue.AddThemeColorOverride("font_color", valueColor);

        // 本段增量：从下方飞入、段内上浮淡出。
        if (frame.StageText is { } stageText)
        {
            _buildStage.Text = stageText;
            float rise = EaseOut(Mathf.Clamp(p * 3f, 0f, 1f));
            _buildStage.Modulate = new Color(tierColor.R, tierColor.G, tierColor.B, Mathf.Clamp(rise * (1.3f - p), 0f, 1f));
            _buildStage.PivotOffset = _buildStage.Size * 0.5f;
            float s = 1.5f - (0.5f * rise);
            _buildStage.Scale = new Vector2(s, s);
        }
        else
        {
            _buildStage.Modulate = new Color(1f, 1f, 1f, 0f);
        }

        // 收束起：总增量与名次变化。
        bool settled = frame.Phase is PowerBuildPhase.Finale or PowerBuildPhase.Hold or PowerBuildPhase.Exit;
        _buildDelta.Text = settled ? $"本手 {build.DeltaText}" : string.Empty;
        _buildDelta.AddThemeColorOverride("font_color", Visuals.ToColor(NumberTierStyle.For(build.Tier).RevealColor));
        _buildRank.Text = settled && build.RankText is { } rank ? $"{rank}{(build.RankUp ? "  ▲" : "  ▼")}" : string.Empty;
        _buildRank.AddThemeColorOverride("font_color", build.RankUp ? Ui.PanelBorder : Ui.DangerText);
        if (frame.Phase == PowerBuildPhase.Finale)
        {
            float a = Mathf.Clamp(p * 4f, 0f, 1f);
            _buildDelta.Modulate = new Color(1f, 1f, 1f, a);
            _buildRank.Modulate = new Color(1f, 1f, 1f, a);
        }
        else if (settled)
        {
            _buildDelta.Modulate = Colors.White;
            _buildRank.Modulate = Colors.White;
        }

        // 特效：每段开头一簇火花；收束爆一大团 + 亮环 + 轻震；数字滚动时持续冒细小火花。
        Vector2 burstAt = at + new Vector2(0f, -20f);
        if (entering)
        {
            switch (frame.Phase)
            {
                case PowerBuildPhase.Territory:
                case PowerBuildPhase.Group:
                    _buildFx.Burst(burstAt, 14 + (frame.StageTier * 8), tierColor, 320f + (frame.StageTier * 60f));
                    _buildFx.Ring(burstAt, tierColor, 0.45f, 180f);
                    break;
                case PowerBuildPhase.Finale:
                    int tier = build.Tier;
                    NumberTierStyle finale = NumberTierStyle.For(tier);
                    Color finaleColor = Visuals.ToColor(finale.RevealColor);
                    _buildFx.Burst(burstAt, 40 + (tier * 22), finaleColor, 420f + (tier * 90f));
                    _buildFx.Burst(burstAt, 24, Colors.White, 260f);
                    for (int i = 0; i <= finale.RingCount + 1; i++)
                    {
                        _buildFx.Ring(burstAt, finaleColor, 0.7f + (i * 0.22f), 320f + (i * 140f), i * 0.12f);
                    }

                    _buildFx.Flash(finaleColor);
                    _shakeTotal = (Math.Max(finale.ShakeMs, tier >= 3 ? 160 : 0)) / 1000d;
                    _shakeLeft = _shakeTotal;
                    break;
            }

            _lastPhase = frame.Phase;
        }

        if (frame.Phase is PowerBuildPhase.Territory or PowerBuildPhase.Group && GD.Randf() < 0.55f)
        {
            _buildFx.Burst(burstAt + new Vector2((float)GD.RandRange(-120d, 120d), 0f), 2, tierColor, 180f);
        }

        float glow = frame.Phase switch
        {
            PowerBuildPhase.Enter => p,
            PowerBuildPhase.Exit => 1f - p,
            PowerBuildPhase.Finale => 1f + (0.6f * (1f - p)),
            _ => 1f,
        };
        // 一档的档位色近白，光晕改用阵营色，免得低档一片灰白。
        Color glowColor = frame.Phase is PowerBuildPhase.Enter or PowerBuildPhase.Exit || frame.StageTier <= NumberTier.Lowest ? _buildFxBack.BaseColor : tierColor;
        _buildFxBack.SetGlow(at, glow, glowColor, scale);
        _buildFxBack.Step((float)delta);
        _buildFx.Step((float)delta);

        // 大卡片边框与外发光：随档位色，收束段先亮后收，其余时候轻微呼吸。
        float breathe = 0.55f + (0.15f * Mathf.Sin((float)_buildMs / 180f));
        float shine = frame.Phase == PowerBuildPhase.Finale ? 1f - (0.45f * p) : breathe;
        _buildCardStyle.BorderColor = glowColor.Lerp(Colors.White, frame.Phase == PowerBuildPhase.Finale ? 0.5f * (1f - p) : 0f);
        _buildCardStyle.ShadowColor = new Color(glowColor.R, glowColor.G, glowColor.B, shine * Mathf.Min(glow, 1f) * 0.8f);
        _buildCardStyle.ShadowSize = frame.Phase == PowerBuildPhase.Finale ? (int)(26 + (30 * (1f - p))) : 26;
    }

    private static float EaseOut(float p) => 1f - ((1f - p) * (1f - p));

    private static float EaseOutBack(float p)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float q = p - 1f;
        return 1f + (c3 * q * q * q) + (c1 * q * q);
    }
}
