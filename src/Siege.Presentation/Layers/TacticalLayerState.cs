using System.Collections.Immutable;
using Siege.Presentation.Style;

namespace Siege.Presentation.Layers;

/// <summary>五种战术信息层（设计文档 §14.2）。</summary>
public enum TacticalLayer
{
    Territory,
    Liberties,
    Power,
    Relics,
    Order,
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

    /// <summary>当前场景处理（降饱和等），随 <see cref="Active"/> 变化。</summary>
    public SceneTreatment Treatment => LayerVisuals.For(Active);

    /// <summary>切换输入模式（辅助设置）。切换时回到默认棋盘，避免"按住中改成点击"留下悬空状态。</summary>
    public void SetMode(LayerInputMode mode)
    {
        Mode = mode;
        Active = null;
    }

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
        Active = Active == layer ? null : layer;
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

    /// <summary>默认键位：键鼠 1–5，手柄方向键 + 肩键。</summary>
    public static TacticalViewBindings Defaults() => new(
        new Dictionary<TacticalLayer, string>
        {
            [TacticalLayer.Territory] = "key:1",
            [TacticalLayer.Liberties] = "key:2",
            [TacticalLayer.Power] = "key:3",
            [TacticalLayer.Relics] = "key:4",
            [TacticalLayer.Order] = "key:5",
        },
        new Dictionary<TacticalLayer, string>
        {
            [TacticalLayer.Territory] = "pad:dpad_up",
            [TacticalLayer.Liberties] = "pad:dpad_right",
            [TacticalLayer.Power] = "pad:dpad_down",
            [TacticalLayer.Relics] = "pad:dpad_left",
            [TacticalLayer.Order] = "pad:left_shoulder",
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
