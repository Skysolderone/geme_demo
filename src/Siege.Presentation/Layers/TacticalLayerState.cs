using System.Collections.Immutable;
using Siege.Presentation.Style;

namespace Siege.Presentation.Layers;

/// <summary>四种战术信息层（设计文档 §14.2）。</summary>
public enum TacticalLayer
{
    /// <summary>盘面层：两种读法见 <see cref="BoardReading"/>。由领地层与气层合并而来（merge-board-layer）。</summary>
    Board,
    Power,
    Relics,
    Order,
}

/// <summary>
/// 盘面层的两种读法。合并的依据是二者点亮的空格集合<b>恒等</b>：覆盖的定义是棋子向四邻接相邻格提供覆盖，
/// 气的定义是棋串的空邻格，所以"被某方覆盖的空格"与"某条棋串的气"是同一批格子；分成两层只是让玩家
/// 在同一片格子的两种着色之间来回切换（merge-board-layer）。
/// </summary>
public enum BoardReading
{
    /// <summary>归属：每个格子的占据、独占、争议与中立状态。</summary>
    Ownership,

    /// <summary>棋串：棋串轮廓、所有气与危险棋串。</summary>
    Groups,
}

/// <summary>进入 / 退出信息层的输入映射（tactical-ui D5）。两种模式共用同一个可见性状态。</summary>
public enum LayerInputMode
{
    /// <summary>默认：按住显示，松开立即恢复默认棋盘。</summary>
    HoldToShow,

    /// <summary>辅助设置：单击进入，再次单击退出。</summary>
    ClickToToggle,
}

/// <summary>
/// 信息层可见性状态机（tactical-ui D4 / D5 / 裁决 3）。
/// </summary>
/// <remarks>
/// <para><b>互斥</b>：任意时刻至多一层可见；进入另一层即自动退出当前层。</para>
/// <para><b>输入抽象</b>：只接收"层 X 按下 / 松开 / 点击 / 返回"，不认识任何按键码；键鼠与手柄的按键到层的映射见 <see cref="TacticalViewBindings"/>。</para>
/// <para><b>零副作用</b>：本类不持有、不引用任何对局对象，与游戏状态严格单向；自己部署期间与他人行动期间都可用。</para>
/// </remarks>
public sealed class TacticalLayerState
{
    public TacticalLayerState(LayerInputMode mode = LayerInputMode.HoldToShow)
    {
        Mode = mode;
    }

    /// <summary>当前输入模式。</summary>
    public LayerInputMode Mode { get; private set; }

    /// <summary>当前可见的信息层；默认棋盘为 <c>null</c>。</summary>
    public TacticalLayer? Active { get; private set; }

    /// <summary>
    /// 盘面层当前的读法。<b>持久</b>：关闭盘面层不重置它，下次打开仍是这一种（裁决 D4 / D6）。
    /// 读法表达的是玩家当前关心什么（谁占了地 / 谁要被吃），这个关注点不会因为松开一次按键就改变。
    /// </summary>
    public BoardReading Reading { get; private set; } = BoardReading.Ownership;

    /// <summary>
    /// 本次打开盘面层时的起始读法，用于点击模式的三态循环：推进过一次再按才关闭。
    /// 不记它的话，若上次停在棋串读法，这一轮就会退化成"打开 → 关闭"两态（少掉另一读法那一态）。
    /// </summary>
    private BoardReading? _readingOnOpen;

    /// <summary>当前场景处理（降饱和等），随 <see cref="Active"/> 变化。</summary>
    public SceneTreatment Treatment => LayerVisuals.For(Active, Reading);

    /// <summary>切换输入模式（辅助设置）。切换时回到默认棋盘，避免"按住中改成点击"留下悬空状态。读法不受影响。</summary>
    public void SetMode(LayerInputMode mode)
    {
        Mode = mode;
        Active = null;
    }

    /// <summary>
    /// 在两种读法之间切换。<b>两种输入模式下都可用</b>——按住显示模式里键正被按住，没有"再按一次"可言，
    /// 所以读法切换不得绑在层键上，否则按住显示的玩家会被锁死在一种读法里（裁决 D3，规格写成 MUST NOT）。
    /// 不改变任何层的可见性。
    /// </summary>
    public void CycleReading() =>
        Reading = Reading == BoardReading.Ownership ? BoardReading.Groups : BoardReading.Ownership;

    /// <summary>层 X 的键被按下。按住模式：显示 X（替换当前层）。点击模式：转交 <see cref="Toggle"/>。</summary>
    public void Press(TacticalLayer layer)
    {
        if (Mode == LayerInputMode.ClickToToggle)
        {
            Toggle(layer);
            return;
        }

        Active = layer;
    }

    /// <summary>
    /// 点击语义：X 已显示则退出，否则显示 X（替换当前层）。供 HUD 上的信息层按钮直接调用——
    /// 鼠标点按钮天然是"点击切换"，但它 MUST NOT 在界面侧复写这段判断（D5：两种输入映射共用同一套状态机）。
    /// </summary>
    public void Toggle(TacticalLayer layer)
    {
        // 盘面层在点击模式下循环三态：关闭 → 当前读法 → 另一读法 → 关闭。
        // 判据是"是否已相对本次打开时的读法推进过"，不是"当前是不是某一种具体读法"——
        // 后者在上次停在棋串读法时会退化成两态（D6）。关闭不重置 Reading。
        if (layer == TacticalLayer.Board && Active == TacticalLayer.Board)
        {
            if (Reading == _readingOnOpen)
            {
                CycleReading();
                return;
            }

            Active = null;
            _readingOnOpen = null;
            return;
        }

        Active = Active == layer ? null : layer;
        _readingOnOpen = Active == TacticalLayer.Board ? Reading : null;
    }

    /// <summary>层 X 的键被松开。按住模式：若 X 正在显示则立即退出；点击模式忽略。</summary>
    public void Release(TacticalLayer layer)
    {
        if (Mode == LayerInputMode.HoldToShow && Active == layer)
        {
            Active = null;
        }
    }

    /// <summary>一次完整点击 = 按下 + 松开。</summary>
    public void Click(TacticalLayer layer)
    {
        Press(layer);
        Release(layer);
    }

    /// <summary>返回 / 取消：回到默认棋盘。</summary>
    public void Back() => Active = null;
}

/// <summary>输入设备组（设计文档 §14.2：键鼠与手柄可分别配置）。</summary>
public enum InputDevice
{
    KeyboardMouse,
    Gamepad,
}

/// <summary>
/// 战术视图键位表：每个设备组各一套"绑定标识 → 信息层"。绑定标识是不透明字符串（由 Godot 层定义，如 InputMap 事件的文本），
/// 两套配置互不影响（tactical-ui 裁决 12）。
/// </summary>
public sealed class TacticalViewBindings
{
    private readonly Dictionary<InputDevice, Dictionary<TacticalLayer, string>> _bindings = [];

    public TacticalViewBindings(
        IReadOnlyDictionary<TacticalLayer, string> keyboardMouse, IReadOnlyDictionary<TacticalLayer, string> gamepad)
    {
        ArgumentNullException.ThrowIfNull(keyboardMouse);
        ArgumentNullException.ThrowIfNull(gamepad);
        _bindings[InputDevice.KeyboardMouse] = [];
        _bindings[InputDevice.Gamepad] = [];
        foreach ((TacticalLayer layer, string binding) in keyboardMouse)
        {
            Rebind(InputDevice.KeyboardMouse, layer, binding);
        }

        foreach ((TacticalLayer layer, string binding) in gamepad)
        {
            Rebind(InputDevice.Gamepad, layer, binding);
        }
    }

    /// <summary>默认键位：键鼠 1–4，手柄方向键。读法切换（Tab）不在此表——它切的是盘面层内部的读法，不是层。</summary>
    public static TacticalViewBindings Defaults() => new(
        new Dictionary<TacticalLayer, string>
        {
            [TacticalLayer.Board] = "key:1",
            [TacticalLayer.Power] = "key:2",
            [TacticalLayer.Relics] = "key:3",
            [TacticalLayer.Order] = "key:4",
        },
        new Dictionary<TacticalLayer, string>
        {
            [TacticalLayer.Board] = "pad:dpad_up",
            [TacticalLayer.Power] = "pad:dpad_right",
            [TacticalLayer.Relics] = "pad:dpad_down",
            [TacticalLayer.Order] = "pad:dpad_left",
        });

    /// <summary>某设备组上某层的绑定；未绑定为 <c>null</c>。</summary>
    public string? BindingOf(InputDevice device, TacticalLayer layer) =>
        _bindings[device].TryGetValue(layer, out string? binding) ? binding : null;

    /// <summary>某设备组的全部绑定快照。</summary>
    public ImmutableSortedDictionary<TacticalLayer, string> Snapshot(InputDevice device) =>
        _bindings[device].ToImmutableSortedDictionary();

    /// <summary>改绑。同一设备组内若该标识已绑在另一层上，先从那一层解绑（一键不控两层）；另一设备组不受影响。</summary>
    public void Rebind(InputDevice device, TacticalLayer layer, string binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        Dictionary<TacticalLayer, string> table = _bindings[device];
        foreach (TacticalLayer other in table.Where(kv => kv.Key != layer && kv.Value == binding).Select(kv => kv.Key).ToList())
        {
            table.Remove(other);
        }

        table[layer] = binding;
    }

    /// <summary>把某设备组上的一个输入标识解析为信息层；不是战术视图键则为 <c>null</c>。</summary>
    public TacticalLayer? Resolve(InputDevice device, string binding)
    {
        foreach ((TacticalLayer layer, string bound) in _bindings[device])
        {
            if (bound == binding)
            {
                return layer;
            }
        }

        return null;
    }
}
