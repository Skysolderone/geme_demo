namespace Siege.Presentation.Camera;

/// <summary>
/// 对手行动时的镜头跟随（viewport-camera「跟随对手行动」）：跟过去、轮到本机再回来。纯状态机，不碰引擎——
/// 引擎侧在对手结算开演前调 <see cref="Begin"/>，移动期间每帧调 <see cref="Advance"/> 并把返回的位姿交给 <see cref="BoardCamera.Set"/>（夹取仍在那边做），
/// 轮到本机时调 <see cref="OwnTurnStarted"/>，本机玩家一动相机就调 <see cref="ManualInput"/>。
/// </summary>
public sealed class CameraFollow
{
    /// <summary>一次移动的时长（毫秒）。</summary>
    public const int TravelMs = 350;

    /// <summary>中央区域占可见宽度的比例：目标点全部落在中央区域内就不动（四角有面板，贴边的落点等于看不见）。</summary>
    public const float CentralWidthFraction = 0.6f;

    /// <summary>中央区域占可见纵深的比例。</summary>
    public const float CentralDepthFraction = 0.5f;

    private CameraPose _from;
    private CameraPose _to;
    private int _elapsedMs;
    private CameraPose? _returnTo;

    /// <summary>跟随是否开启（默认开）。</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>是否正在移动（跟过去或返回途中）。移动期间演出不推进。</summary>
    public bool IsTravelling { get; private set; }

    /// <summary>本机玩家在对手行动期间动过相机：到他下一次行动开始之前不再跟随。</summary>
    public bool Suppressed { get; private set; }

    /// <summary>是否记着"跟随之前本机玩家的画面"。</summary>
    public bool HasReturnPose => _returnTo is not null;

    /// <summary>切换开关，返回切换后的状态。关掉时停止移动并丢掉返回位姿（相机留在原地）。</summary>
    public bool Toggle()
    {
        Enabled = !Enabled;
        if (!Enabled)
        {
            IsTravelling = false;
            _returnTo = null;
        }

        return Enabled;
    }

    /// <summary>
    /// 对手的结算要开演了：<paramref name="points"/> 是这次结算涉及的格（落子、被提、新揭示信物）在棋盘平面上的位置。
    /// 需要移动时开始移动并返回 <c>true</c>；关闭、已被本机玩家接管、全局预览、没有目标点、或目标点全部在中央区域内时不动，返回 <c>false</c>。
    /// 第一次移动之前记下 <paramref name="current"/> 作为返回位姿；终点是目标点外接矩形的中心，缩放距离不变。
    /// </summary>
    public bool Begin(CameraPose current, float visibleWidth, float visibleDepth, bool overview, IReadOnlyCollection<(float X, float Z)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!Enabled || Suppressed || overview || points.Count == 0)
        {
            return false;
        }

        float halfWidth = visibleWidth * CentralWidthFraction * 0.5f;
        float halfDepth = visibleDepth * CentralDepthFraction * 0.5f;
        if (points.All(p => MathF.Abs(p.X - current.FocusX) <= halfWidth && MathF.Abs(p.Z - current.FocusZ) <= halfDepth))
        {
            return false;
        }

        _returnTo ??= current;
        float centerX = (points.Min(p => p.X) + points.Max(p => p.X)) * 0.5f;
        float centerZ = (points.Min(p => p.Z) + points.Max(p => p.Z)) * 0.5f;
        Start(current, new CameraPose(centerX, centerZ, current.Distance));
        return true;
    }

    /// <summary>
    /// 轮到本机玩家行动：解除"不再跟随"；记着返回位姿就开始往回移并返回 <c>true</c>（位姿随即清掉，只回一次）。
    /// </summary>
    public bool OwnTurnStarted(CameraPose current)
    {
        Suppressed = false;
        if (_returnTo is not { } home)
        {
            return false;
        }

        _returnTo = null;
        if (!Enabled || home == current)
        {
            return false;
        }

        Start(current, home);
        return true;
    }

    /// <summary>
    /// 本机玩家动了相机（平移、缩放、回家、全局预览）：停止移动、丢掉返回位姿。
    /// <paramref name="opponentActing"/> 为真（对手行动期间）时，到本机下一次行动之前不再跟随。
    /// </summary>
    public void ManualInput(bool opponentActing)
    {
        IsTravelling = false;
        _returnTo = null;
        Suppressed |= opponentActing;
    }

    /// <summary>推进 <paramref name="deltaMs"/> 毫秒，返回此刻应有的位姿（缓入缓出）；不在移动中时返回终点。</summary>
    public CameraPose Advance(int deltaMs)
    {
        if (!IsTravelling)
        {
            return _to;
        }

        _elapsedMs = Math.Min(TravelMs, _elapsedMs + Math.Max(0, deltaMs));
        if (_elapsedMs >= TravelMs)
        {
            IsTravelling = false;
            return _to;
        }

        float t = (float)_elapsedMs / TravelMs;
        float eased = t * t * (3f - (2f * t));
        return new CameraPose(
            _from.FocusX + ((_to.FocusX - _from.FocusX) * eased),
            _from.FocusZ + ((_to.FocusZ - _from.FocusZ) * eased),
            _from.Distance + ((_to.Distance - _from.Distance) * eased));
    }

    private void Start(CameraPose from, CameraPose to)
    {
        _from = from;
        _to = to;
        _elapsedMs = 0;
        IsTravelling = true;
    }
}
