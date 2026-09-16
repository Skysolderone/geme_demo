using System.Collections.Generic;
using Godot;
using Siege.Presentation.Layers;

namespace Siege.Godot;

/// <summary>
/// InputMap 接线（tactical-ui 裁决 12 / implement 3.8）：键鼠与手柄各一组<b>独立的</b>动作名，
/// 两组互不影响；动作名 → 信息层的解析全部走 Presentation 的 <see cref="TacticalViewBindings"/>，本类不持第二份映射表。
/// </summary>
/// <remarks>
/// 动作在运行时注册进 <see cref="InputMap"/>，避免把 <c>project.godot</c> 里易错的
/// <c>Object(InputEventKey, ...)</c> 资源字面量手写一遍；注册后与编辑器里配置的动作等价，可被 <c>InputEvent.IsActionPressed</c> 识别。
/// </remarks>
public sealed class InputBindings
{
    private readonly List<(string Action, InputDevice Device, string Binding)> _actions = [];

    /// <summary>信息层键位表（键鼠 1–5 / 手柄方向键 + 左肩键）。</summary>
    public TacticalViewBindings Layers { get; } = TacticalViewBindings.Defaults();

    /// <summary>手牌信息面板。</summary>
    public const string HandPanelAction = "siege_hand_panel";

    /// <summary>确认批次。</summary>
    public const string ConfirmAction = "siege_confirm";

    /// <summary>Pass。</summary>
    public const string PassAction = "siege_pass";

    /// <summary>撤回暂放（右键 / 手柄 X）。</summary>
    public const string UnstageAction = "siege_unstage";

    /// <summary>返回 / 关闭当前层与面板。</summary>
    public const string BackAction = "siege_back";

    /// <summary>在「按住显示」与「点击切换」之间切换（辅助设置）。</summary>
    public const string ToggleModeAction = "siege_layer_mode";

    /// <summary>
    /// 切换盘面层的读法（归属／棋串）。<b>独立于层键</b>：按住显示模式下层键正被按住，没有"再按一次"可言，
    /// 若把读法切换绑在层键上，按住显示的玩家会被锁死在一种读法里（merge-board-layer D3）。
    /// 手柄用右摇杆按下，避免占用已绑给手牌面板的右肩键。
    /// </summary>
    public const string CycleReadingAction = "siege_board_reading";

    /// <summary>已注册的全部信息层动作。</summary>
    public IReadOnlyList<(string Action, InputDevice Device, string Binding)> LayerActions => _actions;

    /// <summary>注册全部动作。重复调用安全。</summary>
    public void Install()
    {
        (TacticalLayer Layer, Key Key, JoyButton Pad)[] layers =
        [
            (TacticalLayer.Board, Key.Key1, JoyButton.DpadUp),
            (TacticalLayer.Power, Key.Key2, JoyButton.DpadRight),
            (TacticalLayer.Relics, Key.Key3, JoyButton.DpadDown),
            (TacticalLayer.Order, Key.Key4, JoyButton.DpadLeft),
        ];

        foreach ((TacticalLayer layer, Key key, JoyButton pad) in layers)
        {
            string kbm = $"siege_layer_{layer}_kbm";
            Register(kbm, new InputEventKey { PhysicalKeycode = key });
            _actions.Add((kbm, InputDevice.KeyboardMouse, Layers.BindingOf(InputDevice.KeyboardMouse, layer)!));

            string gamepad = $"siege_layer_{layer}_pad";
            Register(gamepad, new InputEventJoypadButton { ButtonIndex = pad });
            _actions.Add((gamepad, InputDevice.Gamepad, Layers.BindingOf(InputDevice.Gamepad, layer)!));
        }

        Register(CycleReadingAction, new InputEventKey { PhysicalKeycode = Key.Tab }, new InputEventJoypadButton { ButtonIndex = JoyButton.RightStick });
        Register(HandPanelAction, new InputEventKey { PhysicalKeycode = Key.H }, new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder });
        Register(ConfirmAction, new InputEventKey { PhysicalKeycode = Key.Enter }, new InputEventJoypadButton { ButtonIndex = JoyButton.A });
        Register(PassAction, new InputEventKey { PhysicalKeycode = Key.P }, new InputEventJoypadButton { ButtonIndex = JoyButton.Y });
        Register(UnstageAction, new InputEventMouseButton { ButtonIndex = MouseButton.Right }, new InputEventJoypadButton { ButtonIndex = JoyButton.X });
        Register(BackAction, new InputEventKey { PhysicalKeycode = Key.Escape }, new InputEventJoypadButton { ButtonIndex = JoyButton.B });
        Register(ToggleModeAction, new InputEventKey { PhysicalKeycode = Key.T });
    }

    private static void Register(string action, params InputEvent[] events)
    {
        var name = new StringName(action);
        if (!InputMap.HasAction(name))
        {
            InputMap.AddAction(name);
        }

        foreach (InputEvent e in events)
        {
            InputMap.ActionAddEvent(name, e);
        }
    }
}
