using System.Numerics;

namespace Siege.Presentation.Camera;

/// <summary>棋盘平面（世界 X / Z）上的一个轴对齐矩形。数值由引擎侧经 <c>Coord ↔ 3D</c> 的唯一映射给出，本程序集不自算格心。</summary>
public readonly record struct PlaneRect(float MinX, float MinZ, float MaxX, float MaxZ)
{
    /// <summary>X 方向跨度。</summary>
    public float Width => MaxX - MinX;

    /// <summary>Z 方向跨度。</summary>
    public float Depth => MaxZ - MinZ;

    /// <summary>X 中线。</summary>
    public float CenterX => (MinX + MaxX) * 0.5f;

    /// <summary>Z 中线。</summary>
    public float CenterZ => (MinZ + MaxZ) * 0.5f;
}

/// <summary>
/// 对局相机位姿（viewport-camera，design D6）：状态<b>只有</b>注视点 (x, z) 与距离 d；俯角与朝向恒定，
/// 相机位置与注视目标都由这三个数推出。
/// </summary>
public readonly record struct CameraPose(float FocusX, float FocusZ, float Distance)
{
    /// <summary>
    /// 俯角恒为 60°。h=2 高台（0.70 高）在这个角度下向远处只投 0.70 / tan 60° ≈ 0.40 格的遮挡，小于半格——
    /// 这条论证只依赖俯角、不依赖距离，所以缩放 MUST NOT 改它（<c>--pick-check</c> 多位姿钉住）。
    /// </summary>
    public const float PitchDegrees = 60f;

    /// <summary>垂直视场角（度）。</summary>
    public const float FovDegrees = 54f;

    /// <summary>注视目标相对注视点的固定抬升：看向略高于 h=0 地砖的位置。</summary>
    public const float TargetLift = 0.2f;

    /// <summary>注视目标相对注视点朝屏幕近处（+Z）的固定偏移：透视下近半盘占屏更多，偏一点画面才居中。</summary>
    public const float TargetNearShift = 0.3f;

    private const float PitchRadians = PitchDegrees * 0.0174532924f;

    /// <summary>相机注视的世界坐标。</summary>
    public Vector3 Target => new(FocusX, TargetLift, FocusZ + TargetNearShift);

    /// <summary>相机所在的世界坐标：自注视目标沿固定俯角向后上方退 <see cref="Distance"/>。棋盘"上"（−Z）始终朝屏幕上方。</summary>
    public Vector3 Eye => Target + new Vector3(0f, Distance * MathF.Sin(PitchRadians), Distance * MathF.Cos(PitchRadians));
}

/// <summary>
/// 对局相机视图模型（viewport-camera；design D6「逻辑与引擎分离」）：平移 / 缩放 / 回家 / 边界夹取的纯计算，零引擎依赖。
/// 引擎侧只负责采输入、把 <see cref="Pose"/> 写到相机节点。每个改状态的入口末尾统一夹取。
/// </summary>
public sealed class BoardCamera
{
    /// <summary>
    /// 所见范围的线性近似：距离 d 下画面纵向（Z）约可见 <c>d × ViewPerDistance</c> 的棋盘跨度，横向再乘宽高比。
    /// 取自引入本能力之前的固定相机："跨度 12.7 配距离 14.6"，于是一屏看全的地图上最远距离与旧公式逐位相等。
    /// </summary>
    private const float ReferenceDistance = 14.6f;
    private const float ReferenceSpan = 12.7f;

    /// <summary>最远距离的上限：再远格子小到看不清棋子。大于它才能一屏看全的地图需要推屏。</summary>
    public const float FarthestCap = 28f;

    /// <summary>最近限值要完整显示的跨度：一个 5×5 平台，四周各留一格。</summary>
    public const float NearestSpan = 7f;

    /// <summary>开局对准出生平台时，平台四周各留的余量（格）。</summary>
    public const float OpeningMargin = 2f;

    /// <summary>平移速度 = 系数 × d（世界单位 / 秒）：约 1.5 秒横穿一屏，视觉速度不随缩放变。</summary>
    public const float PanSpeedPerDistance = 0.6f;

    /// <summary>滚轮每一格的缩放倍率。</summary>
    public const float ZoomStepFactor = 0.9f;

    private const float LockEpsilon = 1e-4f;

    private readonly PlaneRect _bounds;
    private float _aspect;
    private float _x;
    private float _z;
    private float _distance;
    private bool _overview;
    private CameraPose _beforeOverview;

    /// <summary>以地图外接矩形（含坐标标注外圈）建相机：初始在地图中心、最远缩放。</summary>
    public BoardCamera(PlaneRect bounds, float aspect = 16f / 9f)
    {
        if (!(bounds.Width > 0f) || !(bounds.Depth > 0f))
        {
            throw new ArgumentException("地图外接矩形必须有正的跨度。", nameof(bounds));
        }

        _bounds = bounds;
        _aspect = CheckedAspect(aspect);
        _x = bounds.CenterX;
        _z = bounds.CenterZ;
        _distance = Farthest;
        Clamp();
    }

    /// <summary>地图外接矩形。</summary>
    public PlaneRect Bounds => _bounds;

    /// <summary>当前位姿。</summary>
    public CameraPose Pose => new(_x, _z, _distance);

    /// <summary>整盘一屏看全所需的距离。</summary>
    public float FullViewDistance => MathF.Max(DistanceToShow(_bounds.Depth), DistanceToShow(_bounds.Width / _aspect));

    /// <summary>最远限值 = min(整盘一屏所需距离, 上限)。</summary>
    public float Farthest => MathF.Min(FullViewDistance, FarthestCap);

    /// <summary>最近限值：完整显示一个 5×5 平台；地图本身比它还小时退到最远限值。</summary>
    public float Nearest => MathF.Min(DistanceToShow(NearestSpan), Farthest);

    /// <summary>最远缩放下整盘一屏可见（如 <c>siege-4p-base-v4</c>）：这类地图上相机等价于旧的固定相机。</summary>
    public bool FitsOneScreen => FullViewDistance <= FarthestCap;

    /// <summary>是否处于全局预览：整盘一屏可见，距离越过 <see cref="FarthestCap"/>，注视点锁在地图中心。</summary>
    public bool IsOverview => _overview;

    /// <summary>当前缩放下注视点的可行矩形：外接矩形向内收缩所见范围之半；某方向所见 ≥ 地图跨度则退化为中线。</summary>
    public PlaneRect Feasible
    {
        get
        {
            (float minX, float maxX) = Range(_bounds.MinX, _bounds.MaxX, VisibleWidth);
            (float minZ, float maxZ) = Range(_bounds.MinZ, _bounds.MaxZ, VisibleDepth);
            return new PlaneRect(minX, minZ, maxX, maxZ);
        }
    }

    /// <summary>当前缩放下画面纵向（Z）所见的棋盘跨度（线性近似）。</summary>
    public float VisibleDepth => _distance * ReferenceSpan / ReferenceDistance;

    /// <summary>当前缩放下画面横向（X）所见的棋盘跨度（线性近似）。</summary>
    public float VisibleWidth => VisibleDepth * _aspect;

    /// <summary>显示 <paramref name="span"/> 这么宽的棋盘所需的距离。运算次序与旧固定相机的公式一致（<c>14.6 × 跨度 ÷ 12.7</c>）。</summary>
    public static float DistanceToShow(float span) => ReferenceDistance * span / ReferenceSpan;

    /// <summary>窗口宽高比变了：所见范围跟着变，立即重夹。</summary>
    public void SetAspect(float aspect)
    {
        // 停在最远缩放的相机在窗口变形后仍停在（新的）最远缩放：否则一屏看全的地图会被窗口拉伸"挤出"画面。
        bool atFarthest = _distance >= Farthest - LockEpsilon;
        _aspect = CheckedAspect(aspect);
        if (atFarthest)
        {
            _distance = Farthest;
        }

        Clamp();
    }

    /// <summary>
    /// 平移：<paramref name="right"/> / <paramref name="up"/> 是 −1..1 的意图（屏幕右 = +X，屏幕上 = −Z），超出范围按 ±1 计。
    /// 只改注视点；速度与 d 成正比。
    /// </summary>
    public void Pan(float right, float up, float seconds)
    {
        if (_overview)
        {
            return;
        }

        float step = PanSpeedPerDistance * _distance * MathF.Max(seconds, 0f);
        _x += Math.Clamp(right, -1f, 1f) * step;
        _z -= Math.Clamp(up, -1f, 1f) * step;
        Clamp();
    }

    /// <summary>缩放：<paramref name="steps"/> 为正拉近、为负拉远（滚轮格数）。只改距离；改后立即重夹注视点。</summary>
    public void Zoom(int steps)
    {
        if (_overview)
        {
            if (steps <= 0)
            {
                return;
            }

            // 全局预览里拉近：退出预览，从平时的最远缩放接着拉，注视点留在地图中心。
            _overview = false;
            _distance = Farthest;
        }

        _distance *= MathF.Pow(ZoomStepFactor, steps);
        Clamp();
    }

    /// <summary>
    /// 全局预览开关（只在一屏看不全的地图上有意义，一屏看全的地图返回 <c>false</c>、不动相机）。
    /// 切入：记下当前位姿，距离取 <see cref="FullViewDistance"/>（越过 <see cref="FarthestCap"/>）、注视点锁地图中心；
    /// 再切一次：回到切入前的位姿。预览中推屏与拉远无效，拉近 / 回家 / 开局对准 / 直接置位都会退出预览。
    /// 俯角不变——预览只是"更远"，拾取的几何论证不受影响（<c>--pick-check</c> 另验这一位姿）。
    /// </summary>
    public bool ToggleOverview()
    {
        if (FitsOneScreen)
        {
            return false;
        }

        if (_overview)
        {
            _overview = false;
            Set(_beforeOverview);
        }
        else
        {
            _beforeOverview = Pose;
            _overview = true;
            Clamp();
        }

        return true;
    }

    /// <summary>回家：注视点移到 (<paramref name="x"/>, <paramref name="z"/>)，缩放距离不变，结果同样经过夹取。</summary>
    public void Home(float x, float z)
    {
        if (_overview)
        {
            // 预览里回家：距离回到切入前的值。
            _overview = false;
            _distance = _beforeOverview.Distance;
        }

        _x = x;
        _z = z;
        Clamp();
    }

    /// <summary>
    /// 开局对准出生平台（<paramref name="platform"/> = 出生区<b>格心</b>的外接矩形；未选区传 <c>null</c>，等同回到地图中心）。
    /// <b>平台整个可见优先于精确居中</b>：距离取"完整显示平台并在四周各留 <see cref="OpeningMargin"/> 格"所需的距离（夹在最近 / 最远限值内，
    /// 双向设定——已经拉得更近的相机会被拉远），注视点取平台中心再经夹取。画面所见范围恒在地图外接矩形之内，
    /// 所以贴边的平台只是偏离画面中心、仍整个在画面里，靠边一侧的余量到坐标标注外圈为止。
    /// 一屏看全的地图上不动相机：没动过就仍是最远缩放的初始位姿，动过就保留玩家的画面。
    /// </summary>
    public void Open(PlaneRect? platform)
    {
        if (_overview)
        {
            _overview = false;
            _distance = _beforeOverview.Distance;
        }

        if (platform is not { } rect)
        {
            Home(_bounds.CenterX, _bounds.CenterZ);
            return;
        }

        if (FitsOneScreen)
        {
            // 整盘本来就看得全：锁定不动相机，玩家在插旗阶段拉近 / 推过的画面原样保留。
            return;
        }

        // 格心外接矩形 → 格边：两侧各加半格（格距为 1，与 NearestSpan 同一单位）；再加两侧余量。
        float pad = 1f + (2f * OpeningMargin);
        _distance = MathF.Max(DistanceToShow(rect.Depth + pad), DistanceToShow((rect.Width + pad) / _aspect));
        Home(rect.CenterX, rect.CenterZ);
    }

    /// <summary>直接给位姿（自检用），同样经过夹取。</summary>
    public void Set(CameraPose pose)
    {
        _overview = false;
        _x = pose.FocusX;
        _z = pose.FocusZ;
        _distance = pose.Distance;
        Clamp();
    }

    /// <summary>
    /// <c>--pick-check</c> 的 7 个自检位姿：地图中心（中间缩放）、四个角的夹取位置（最近缩放，此时可行矩形最大）、最近、最远。
    /// 返回的位姿都已夹取；不改变本相机的当前状态。
    /// </summary>
    public IReadOnlyList<(string Name, CameraPose Pose)> CheckPoses()
    {
        CameraPose saved = Pose;
        bool wasOverview = _overview;
        CameraPose beforeOverview = _beforeOverview;
        float middle = MathF.Sqrt(Nearest * Farthest);
        (string, float, float, float)[] raw =
        [
            ("中心", _bounds.CenterX, _bounds.CenterZ, middle),
            ("左上角", _bounds.MinX, _bounds.MinZ, Nearest),
            ("右上角", _bounds.MaxX, _bounds.MinZ, Nearest),
            ("左下角", _bounds.MinX, _bounds.MaxZ, Nearest),
            ("右下角", _bounds.MaxX, _bounds.MaxZ, Nearest),
            ("最近", _bounds.CenterX, _bounds.CenterZ, Nearest),
            ("最远", _bounds.CenterX, _bounds.CenterZ, Farthest),
        ];

        var poses = new List<(string, CameraPose)>(raw.Length);
        foreach ((string name, float x, float z, float d) in raw)
        {
            Set(new CameraPose(x, z, d));
            poses.Add((name, Pose));
        }

        Set(saved);
        if (wasOverview)
        {
            _beforeOverview = beforeOverview;
            _overview = true;
            Clamp();
        }

        return poses;
    }

    private static float CheckedAspect(float aspect) =>
        aspect > 0f && float.IsFinite(aspect) ? aspect : throw new ArgumentOutOfRangeException(nameof(aspect), "宽高比必须为正。");

    private static (float Min, float Max) Range(float min, float max, float visible)
    {
        float slack = ((max - min) - visible) * 0.5f;
        if (slack <= LockEpsilon)
        {
            float mid = (min + max) * 0.5f;
            return (mid, mid);
        }

        return ((min + max) * 0.5f - slack, (min + max) * 0.5f + slack);
    }

    private void Clamp()
    {
        if (_overview)
        {
            _distance = FullViewDistance;
            _x = _bounds.CenterX;
            _z = _bounds.CenterZ;
            return;
        }

        _distance = Math.Clamp(_distance, Nearest, Farthest);
        PlaneRect feasible = Feasible;
        _x = Math.Clamp(_x, feasible.MinX, feasible.MaxX);
        _z = Math.Clamp(_z, feasible.MinZ, feasible.MaxZ);
    }
}
