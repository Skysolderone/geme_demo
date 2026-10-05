using System.Collections.Immutable;
using System.Linq;
using Godot;
using Siege.Core.Board;
using Siege.Core.Scoring;
using Siege.Presentation.Camera;
using Siege.Presentation.Show;
using Siege.Presentation.Style;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 数值分档呈现的展示入口与自证读数（tiered-number-show D9 / 2.3–2.5）：<c>--reveal-preview=play|ladder</c>、<c>--reveal-at=&lt;毫秒&gt;</c>，
/// 以及演出期间的音效提示行、轻震起止行与截图时的揭示 / 亮环 / 轻震 / 势力栏放大读数。
/// </summary>
/// <remarks>
/// <para>预览只做展示（与 <c>--carry-preview=</c> 同一做法）：用内存里的示例节拍（五条棋串各落在一档）在未插旗的预览盘面上播放，
/// 不建真正的对局、不读写档案、不接受落子与插旗。示例棋串的军势是写死的字面量，引擎层不算军势；档位由 Presentation 在生成条目时给出，引擎层不取档。</para>
/// <para><c>play</c>：按真实时间线播放——每一档各播一拍军势揭示，拍与拍之间隔一拍空的势力节拍（0.9 秒，结果在这段时间里停留，与真实结算里紧跟势力到账的节奏相同），
/// 五档依次、循环播放；有人在看时带揭示音效，可按住空格 / 鼠标左键提速。配 <c>--screenshot=</c> 时须再给 <c>--reveal-at=</c>：以固定 16 ms 一步推进到该时刻取图。
/// 相机只把注视点移到示例棋串、缩放不动；截近景另给 <c>--shot-cell=</c> 与 <c>--shot-zoom=</c>。</para>
/// <para><c>ladder</c>：五条结果并排的<b>定格</b>对照（遮罩是手工拼的终态：全部已到末步、结果年龄 0、四 / 五档带亮环）。真实演出里条目逐条依次出现，
/// 不会五档同时满显——这张只用来比字号、颜色与描边的阶梯。</para>
/// </remarks>
public sealed partial class GameRoot
{
    /// <summary>预览循环播放时两遍之间的停顿（秒）。</summary>
    private const double RevealPreviewPauseSeconds = 1.2;

    /// <summary>
    /// 示例棋串（基础、位置加值、倍增子数量、阵型阶数、军势）：军势依次落在一至五档（formation-tiers 裁决的阈值 8 / 16 / 32 / 64）。
    /// 军势是写死的字面量（引擎层不算军势），与"⌊(基础 + 加值) × 1.5^(倍增子 + 阵型)⌋"自洽；第一、三条是规格算例（"只有阵型""阵型多一步"），
    /// 第二、四条是"四步揭示""高档末步定格"，第三、五条是"倍增 + 阵型"。阵型阶数照明细原样给，示例棋串只占一格（只做展示）。
    /// </summary>
    private static readonly (int Base, int Bonus, int Multipliers, int Formation, int Power)[] RevealSampleGroups =
    [
        (3, 0, 0, 1, 4),       // 一档：3×1.5 = 4（只有阵型，三步）
        (5, 2, 1, 0, 10),      // 二档：(5+2)×1.5 = 10
        (5, 2, 1, 2, 23),      // 三档：(5+2)×1.5×2.25 = 23（倍增 + 阵型，五步）
        (20, 4, 1, 0, 36),     // 四档：(20+4)×1.5 = 36，一圈亮环
        (10, 4, 1, 3, 70),     // 五档：(10+4)×1.5×3.375 = 70，两圈亮环 + 轻震
    ];

    private string? _revealPreview;
    private int _revealAtMs = -1;

    /// <summary><c>--reveal-preview=ladder</c> 的定格遮罩；其余时候为 <c>null</c>（遮罩取自时间线）。</summary>
    private ShowMask? _revealStill;
    private bool _revealAimed;
    private double _revealPause;

    /// <summary>当前演出已推进的毫秒数（含提速倍率），只用于自证行。</summary>
    private int _showClockMs;

    /// <summary>轻震开始那一刻的相机位姿（自证：轻震结束后注视点与缩放不变）；不在轻震中为 <c>null</c>。</summary>
    private CameraPose? _shakeFrom;

    /// <summary>无人值守下某次轻震结束时位姿与开始前不同、或相机节点没回到位姿本身（见 <see cref="TrackShake"/>）：截图流程据此以退出码 1 结束。</summary>
    private bool _shakeDrifted;

    /// <summary>是否处于军势揭示预览（只做展示）。</summary>
    private bool RevealPreviewing => _revealPreview is not null;

    /// <summary>音效提示是否逐条打印（<c>--shot-show=</c> 截图与预览：无人值守下没有音频节点，提示与档位只能从这一行看）。</summary>
    private bool CueLog => _shotShow is not null || RevealPreviewing;

    /// <summary>画面此刻照着哪份遮罩画：定格预览用手工遮罩，其余取时间线的。</summary>
    private ShowMask ShownMask() => _revealStill ?? _show.Mask();

    /// <summary>读预览的启动选项（在 <c>EnsureRecognized</c> 之前、<c>ReadCarryArgs</c> 之前调用：预览同样关闭带入带出）。</summary>
    private void ReadRevealArgs(LaunchArgs args)
    {
        _revealPreview = args.Text("reveal-preview", "play 或 ladder（军势揭示的逐档预览，只做展示）");
        _revealAtMs = args.Value<int>("reveal-at", "非负整数（预览播放到第几毫秒取图）", t => int.TryParse(t, out int v) && v >= 0 ? v : null) ?? -1;
    }

    /// <summary>预览选项的组合校验（<c>EnsureRecognized</c> 之后）。</summary>
    private void CheckRevealArgs(bool mapSelect)
    {
        if (_revealPreview is not (null or "play" or "ladder"))
        {
            throw new System.FormatException($"--reveal-preview={_revealPreview} 无效：应为 play 或 ladder。");
        }

        if (RevealPreviewing && (_autoDemo || _pickCheck || _shotShow is not null || mapSelect || _carryPreview is not null || _shotRecruitCollapsed || _shotForbidden))
        {
            throw new System.FormatException("--reveal-preview= 只做展示，不与 --auto-demo / --pick-check / --shot-show= / --map-select / --carry-preview= / --shot-recruit-collapsed / --shot-forbidden 同用。");
        }

        bool playShot = _revealPreview == "play" && _screenshotFrame >= 0;
        if ((_revealAtMs >= 0) != playShot)
        {
            throw new System.FormatException("--reveal-at= 须与 --reveal-preview=play 和 --screenshot= 同用；--reveal-preview=play 配 --screenshot= 时也必须给 --reveal-at=。");
        }
    }

    /// <summary>
    /// 示例条目：五条单子棋串在地图中央左右排开、相隔一格，并上下错开两行——最远一档缩放下标注相对格子更大，排成一行会互相压住；
    /// 也不贴地图边（贴边的一档白色小字容易被看成坐标标注）。条目内容由 Presentation 从军势明细投影（步骤、档位、时长都在那边定）。
    /// </summary>
    private ImmutableArray<RevealEntry> RevealSampleEntries()
    {
        DefaultBoardView board = _session.World.Board();
        int row = board.Height / 2;
        int middle = board.Width / 2;
        return
        [
            .. RevealSampleGroups.Select((sample, i) => RevealEntry.From(new GroupPower(
                _session.Me,
                [new Coord(System.Math.Clamp(middle + ((i - 2) * 2), 0, board.Width - 1), System.Math.Clamp(row + (i % 2 == 0 ? 1 : -1), 0, board.Height - 1))],
                sample.Base,
                LineBonus: sample.Bonus,
                SynergyBonus: 0,
                HighGroundBonus: 0,
                BannerBonus: 0,
                ChainBonus: 0,
                SentryBonus: 0,
                BoundaryBonus: 0,
                MultiplierCount: sample.Multipliers,
                Power: sample.Power) { FormationTier = sample.Formation })),
        ];
    }

    /// <summary>
    /// 示例节拍序列：每条棋串一拍军势揭示（单条目，不触发整拍上限的压缩，各档按设计节奏播），其后隔一拍<b>空的</b>势力节拍（没有任何玩家变化：不显示、不发声，只占 0.9 秒）。
    /// 真实结算里一次只有一拍军势揭示；这里拆成五拍只为逐档看清，时间线、遮罩与音效提示走的仍是同一套代码。
    /// 间隔拍不能省，也不能换成带数值的势力到账：音效提示按"一次结算至多一拍军势揭示、每名玩家至多一条势力变化"导出，
    /// 两拍军势揭示紧挨着会漏掉后一拍的第一声，同一名玩家的多条势力变化在遮罩里只留最后一条。到账声的按档升调因此不在预览里，要在真实对局里听。
    /// </summary>
    private ImmutableArray<SettlementBeat> RevealSampleBeats()
    {
        ImmutableArray<SettlementBeat>.Builder beats = ImmutableArray.CreateBuilder<SettlementBeat>();
        foreach (RevealEntry entry in RevealSampleEntries())
        {
            beats.Add(new PowerRevealBeat([entry]));
            beats.Add(new PowerBeat([]));
        }

        return beats.ToImmutable();
    }

    /// <summary>定格对照的遮罩：五条结果同时满显（已到末步、步内进度满、结果年龄 0），四 / 五档带进行到一半的亮环；不震。</summary>
    private ShowMask RevealLadderMask()
    {
        ImmutableArray<RevealEntry> entries = RevealSampleEntries();
        return ShowMask.Empty with
        {
            Reveals = [.. entries.Select(e => new RevealDisplay(e.Coord, e.Owner, e.Steps[^1].RunningText, e.Steps[^1].Text, e.FinalTier, PowerInterpolation.FullPermille, true, e.FinalTier, 0))],
            Rings =
            [
                .. entries.Where(e => NumberTierStyle.For(e.FinalTier).RingCount > 0)
                    .Select(e => new ImpactRing(e.Coord, NumberTierStyle.For(e.FinalTier).RingCount, PowerInterpolation.FullPermille / 2)),
            ],
        };
    }

    /// <summary>
    /// 预览的单帧驱动（代替对局推进）：第一次进来把相机对准示例棋串；<c>ladder</c> 挂上定格遮罩；<c>play</c> 循环播放示例节拍，
    /// 截图模式下到帧后以 16 ms 一步推进到 <c>--reveal-at=</c> 的时刻取图。
    /// </summary>
    private void DriveRevealPreview(double delta)
    {
        if (!_revealAimed)
        {
            _revealAimed = true;
            ImmutableArray<RevealEntry> entries = RevealSampleEntries();
            (float x, float z) = _board.PlaneCenterOf(entries[entries.Length / 2].Coord);
            SyncAspect();
            _board.Rig.Set(new CameraPose(x, z, _board.Rig.Pose.Distance));   // 缩放不动（开局的最远一档）；要近景用滚轮，截图用 --shot-cell= / --shot-zoom=
            ApplyCamera("预览对准示例棋串");
            _hud.SetTurnSummary(
                $"预览：军势揭示示例（{(_revealPreview == "ladder" ? "五档定格对照" : "逐档循环播放，按住空格提速")}；只做展示，不碰对局与档案）",
                Visuals.FactionColorOf(_session.Me));
            GD.Print($"[reveal-preview] {_revealPreview}：示例棋串 " + string.Join("、", entries.Select(e => $"{e.Coord.ToNotation()} 「{e.Steps[^1].RunningText}」{e.FinalTier} 档（{e.Steps.Length} 步）")));
            if (_revealPreview == "ladder")
            {
                _revealStill = RevealLadderMask();
            }

            _dirty = true;
        }

        if (_revealPreview != "play")
        {
            return;
        }

        if (_screenshotFrame >= 0)
        {
            if (_frame < _screenshotFrame)
            {
                return;
            }

            // 取图：固定 16 ms 一步推进到指定时刻（与 --shot-show 同一步长，音效提示与轻震起止照常逐步导出并打印），最后一步补齐零头。
            BeginShow(RevealSampleBeats(), ShowDuration.Normal, "预览");
            int totalMs = _show.TotalDurationMs;
            while (_showClockMs < _revealAtMs && !_show.IsFinished)
            {
                StepShow(System.Math.Min(16, _revealAtMs - _showClockMs), fast: false);
            }

            // 取图时刻落在演出之外：遮罩已空，截到的只是一张没有任何中间态的盘面——报错退出，不存一张"看起来正常"的空图。
            if (_show.IsFinished)
            {
                GD.PrintErr($"[siege] 错误：--reveal-at={_revealAtMs} 不在示例演出之内（总时长 {totalMs} ms，应小于它）。");
                SetProcess(false);
                GetTree().Quit(1);
                return;
            }

            _screenshotFrame = -1;
            BeginCapture();
            return;
        }

        if (!_show.IsFinished)
        {
            AdvanceShow(delta);
            return;
        }

        _revealPause -= delta;
        if (_revealPause <= 0d)
        {
            _revealPause = RevealPreviewPauseSeconds;
            BeginShow(RevealSampleBeats(), ShowDuration.Normal, "预览");
        }
    }

    /// <summary>
    /// 时间线推进一步：导出音效提示（有音频节点才播，<see cref="CueLog"/> 下逐条打印），并把遮罩给的轻震进度交给棋盘、记下轻震起止。
    /// 返回推进之后的遮罩。
    /// </summary>
    private ShowMask StepShow(int ms, bool fast)
    {
        ShowMask? heard = _sounds is null && !CueLog ? null : _show.Mask();
        ImmutableArray<SettlementBeat> crossed = _show.Advance(ms, fast);
        _showClockMs += fast ? ms * ShowTimeline.SpeedUpFactor : ms;
        ShowMask mask = _show.Mask();
        if (heard is not null)
        {
            EmitCues(SoundCues.Between(heard, mask, crossed), fast);
        }

        _board.SetShake(mask.ShakePermille);
        TrackShake(mask.ShakePermille);
        return mask;
    }

    /// <summary>把一帧的音效提示交给播放器池（无人值守 / <c>--mute</c> 下没有），并在自证模式下打印种类、档位与升调。</summary>
    private void EmitCues(ImmutableArray<SoundCue> cues, bool fast)
    {
        _sounds?.Play(cues, fast);
        if (CueLog && !cues.IsDefaultOrEmpty)
        {
            GD.Print($"[sound] 第 {_frame} 帧（演出 {_showClockMs} ms）提示："
                + string.Join("、", cues.Select(c => $"{CueName(c.Kind)}·{c.Tier} 档（升 {ShowSounds.SemitonesOf(c)} 半音，音高 ×{ShowSounds.PitchScaleOf(c):0.00}）"))
                + (_sounds is null ? "；没有音频节点，不发声" : fast ? "；提速中降音量" : string.Empty));
        }
    }

    private static string CueName(SoundCueKind kind) => kind switch
    {
        SoundCueKind.Placement => "落子",
        SoundCueKind.Capture => "提子",
        SoundCueKind.Relic => "信物",
        SoundCueKind.Reveal => "揭示",
        SoundCueKind.Territory => "领地到账",
        SoundCueKind.Group => "军势到账",
        SoundCueKind.Banner => "横幅",
        _ => kind.ToString(),
    };

    /// <summary>
    /// 轻震起止的自证行（tiered-number-show 2.4）：开始时记下视图模型的位姿，结束时再读一次——轻震只偏移相机节点、不写回位姿，
    /// 两次读数应当相同（期间玩家自己推了镜头除外），且结束后相机节点相对位姿的偏移为 0。
    /// </summary>
    private void TrackShake(int? permille)
    {
        if (permille is not null && _shakeFrom is null)
        {
            _shakeFrom = _board.Rig.Pose;
            GD.Print($"[shake] 第 {_frame} 帧（演出 {_showClockMs} ms）轻震开始：{PoseText(_board.Rig.Pose)}");
        }
        else if (permille is null && _shakeFrom is { } from)
        {
            _shakeFrom = null;
            CameraPose now = _board.Rig.Pose;
            float residue = _board.CameraOffsetFromPose;

            // 无人值守下没有任何镜头输入：位姿变了或相机节点没回到位姿，只能是轻震改了相机状态——记下来，取图后以退出码 1 结束（不只打一行字）。
            bool drifted = Unattended && (now != from || residue > 0.0001f);
            _shakeDrifted |= drifted;
            string same = now == from ? "相同" : Unattended ? "不同（无人值守下没有镜头输入：轻震改了相机状态）" : "不同（期间有镜头输入）";
            GD.Print($"[shake] 第 {_frame} 帧（演出 {_showClockMs} ms）轻震结束：{PoseText(now)}，与轻震开始前{same}；"
                + $"相机节点相对位姿的偏移 {residue:0.#####}{(drifted ? "；自检失败" : string.Empty)}");
        }
    }

    private static string PoseText(CameraPose pose) => $"注视点 ({pose.FocusX:0.###}, {pose.FocusZ:0.###}) 距离 {pose.Distance:0.###}";

    /// <summary>
    /// 截图时的分档自证（另起一行，既有的 [show] 取景行保持原样）：揭示条目（坐标、累计文案、最新一步、步档位、是否末步及引擎层据此取的字号 / 描边 / 弹出缩放）、
    /// 亮环、轻震进度与实际偏移、势力栏各段的档位与放大幅度。全部取自遮罩与样式表。
    /// </summary>
    private void PrintTierShot(ShowMask mask)
    {
        string reveals = mask.Reveals.IsEmpty
            ? "无"
            : string.Join("；", mask.Reveals.Select(r =>
            {
                NumberTierStyle style = NumberTierStyle.For(r.StepTier);
                return $"{r.Coord.ToNotation()}（{Labels.Player(r.Owner)}）「{r.RunningText}」最新一步「{r.StepText}」{r.StepTier} 档 步内 {r.StepPermille}‰ "
                    + $"{(r.AtFinal ? $"已到末步（末步 {r.FinalTier} 档，结果年龄 {r.ResultAgePermille}‰）" : $"未到末步（末步 {r.FinalTier} 档）")}"
                    + $" → 字号 {style.RevealFontSize} 描边 {style.RevealOutlineSize} 弹出缩放 {BoardView.PopScaleOf(r):0.00}";
            }));
        string rings = mask.Rings.IsEmpty ? "无" : string.Join("、", mask.Rings.Select(r => $"{r.Coord.ToNotation()} {r.Count} 圈 {r.ProgressPermille}‰"));
        Vector3 shake = _board.ShakeOffset;
        string shaking = mask.ShakePermille is { } p ? $"{p}‰，相机偏移 ({shake.X:0.####}, {shake.Z:0.####})" : $"无（相机节点相对位姿的偏移 {_board.CameraOffsetFromPose:0.#####}）";
        string rank = string.Join("；", mask.Power.OrderBy(k => k.Key.Value).Where(k => k.Value.Stage is PowerStage.Territory or PowerStage.Group).Select(k =>
            $"{Labels.Player(k.Key)} {k.Value.StageText} {k.Value.Change.TierOf(k.Value.Stage)} 档 段首 {NumberTierStyle.For(k.Value.Change.TierOf(k.Value.Stage)).RankScalePercent}%"
            + $" 段内 {k.Value.StagePermille}‰ 现字号 {Hud.RankFontPx(k.Value)} px（正文 {UiTheme.BodyFontPx} px）"));
        GD.Print($"[show-tier] 揭示条目 {mask.Reveals.Length} 条：{reveals}；亮环 {rings}；轻震 {shaking}；势力栏放大 {(rank.Length == 0 ? "无" : rank)}");

        // 常驻标注的档位分布（只在没有全局预览时画）：各档各几条，字号取样式表；另打遮罩给的显现进度表（被揭示棋串此刻画不画、多透明）。
        if (_session.World.Layer(Siege.Presentation.Layers.TacticalLayer.Power) is Siege.Presentation.Layers.PowerLayerContent power)
        {
            ImmutableArray<Siege.Presentation.Layers.GroupPowerLabel> labels = Siege.Presentation.Layers.GroupPowerLabels.Of(power);
            GD.Print($"[show-tier] 常驻标注 {labels.Length} 条{(_board.Rig.IsOverview ? "（全局预览下不画）" : string.Empty)}："
                + string.Join("、", labels.GroupBy(l => l.Tier).OrderBy(g => g.Key).Select(g =>
                    $"{g.Key} 档 {g.Count()} 条（字号 {NumberTierStyle.For(g.Key).GroupLabelFontSize}：{string.Join(" ", g.Select(l => $"{l.Coord.ToNotation()}={l.Text}"))}）"))
                + "；遮罩给的显现进度（reveal-label-handoff：0 不画，其余按进度取透明度，不在表里的完全显示）"
                + (mask.GroupLabelPermille.IsEmpty ? "空" : string.Join("、", mask.GroupLabelPermille.OrderBy(k => k.Key).Select(k => $"{k.Key.ToNotation()} {k.Value}‰")))
                + $"；{_board.GroupLabelRedrawReadout()}");
        }
    }
}
