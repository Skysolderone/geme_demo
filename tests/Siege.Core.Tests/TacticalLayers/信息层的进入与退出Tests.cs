using Siege.Presentation.Layers;
using Siege.Presentation.Style;

namespace Siege.Core.Tests.TacticalLayers;

/// <summary>规格：tactical-layers —— Requirement: 信息层的进入与退出</summary>
public class 信息层的进入与退出Tests
{
    [Fact]
    public void 按住进入松开退出()
    {
        // 设计文档 §14.2：按住战术视图键进入临时信息模式，松开立即恢复默认棋盘（含场景处理恢复）。
        // 松开别的键不影响当前层。按键到"层 X 按下 / 松开"事件的映射由阶段 B 的 InputMap 完成（渲染与真实手柄输入归阶段 B + 人工检查清单）。
        // 变异验证 M-I1：TacticalLayerState.Release 的条件改为 `Mode == LayerInputMode.ClickToToggle && Active == layer` → 本测试红 1。
        var state = new TacticalLayerState();
        Assert.Equal(LayerInputMode.HoldToShow, state.Mode);

        state.Press(TacticalLayer.Power);
        Assert.Equal(TacticalLayer.Power, state.Active);
        Assert.NotEqual(SceneTreatment.Default, state.Treatment);

        state.Release(TacticalLayer.Relics);
        Assert.Equal(TacticalLayer.Power, state.Active);

        state.Release(TacticalLayer.Power);
        Assert.Null(state.Active);
        Assert.Equal(SceneTreatment.Default, state.Treatment);
    }

    [Fact]
    public void 键鼠与手柄分别配置()
    {
        // 设计文档 §14.2 / 裁决 12：键鼠与手柄两套键位互不影响、各自生效。
        // 变异验证 M-I2：TacticalViewBindings 构造函数让两个设备共用同一张表（`_bindings[Gamepad] = _bindings[KeyboardMouse]`）→ 本测试红 1。
        TacticalViewBindings bindings = TacticalViewBindings.Defaults();
        var gamepadBefore = bindings.Snapshot(InputDevice.Gamepad);

        bindings.Rebind(InputDevice.KeyboardMouse, TacticalLayer.Territory, "key:T");
        Assert.Equal("key:T", bindings.BindingOf(InputDevice.KeyboardMouse, TacticalLayer.Territory));
        Assert.Equal(gamepadBefore, bindings.Snapshot(InputDevice.Gamepad));
        Assert.Equal(TacticalLayer.Territory, bindings.Resolve(InputDevice.KeyboardMouse, "key:T"));
        Assert.Null(bindings.Resolve(InputDevice.Gamepad, "key:T"));

        var keyboardBefore = bindings.Snapshot(InputDevice.KeyboardMouse);
        bindings.Rebind(InputDevice.Gamepad, TacticalLayer.Territory, "pad:y");
        Assert.Equal(keyboardBefore, bindings.Snapshot(InputDevice.KeyboardMouse));
        Assert.Equal(TacticalLayer.Territory, bindings.Resolve(InputDevice.Gamepad, "pad:y"));
        Assert.Null(bindings.Resolve(InputDevice.Gamepad, "pad:dpad_up"));

        // 同一设备内一个键只控一层：把气层也绑到 key:T，领地层失去该键
        bindings.Rebind(InputDevice.KeyboardMouse, TacticalLayer.Liberties, "key:T");
        Assert.Equal(TacticalLayer.Liberties, bindings.Resolve(InputDevice.KeyboardMouse, "key:T"));
        Assert.Null(bindings.BindingOf(InputDevice.KeyboardMouse, TacticalLayer.Territory));
        Assert.Equal("pad:y", bindings.BindingOf(InputDevice.Gamepad, TacticalLayer.Territory));
    }

    [Fact]
    public void 辅助设置切换为点击()
    {
        // 设计文档 §14.2 / D5：启用"点击切换"后单击进入、再次单击退出，无需按住；与按住模式共用同一可见性状态与场景处理。
        // 变异验证 M-I3：TacticalLayerState.Press 的点击模式分支改为总是 `Active = layer` → 本测试红 1。
        var state = new TacticalLayerState();
        state.Press(TacticalLayer.Liberties);
        state.SetMode(LayerInputMode.ClickToToggle);
        Assert.Null(state.Active);

        state.Click(TacticalLayer.Relics);
        Assert.Equal(TacticalLayer.Relics, state.Active);
        SceneTreatment clicked = state.Treatment;

        state.Click(TacticalLayer.Relics);
        Assert.Null(state.Active);

        var hold = new TacticalLayerState();
        hold.Press(TacticalLayer.Relics);
        Assert.Equal(hold.Active, TacticalLayer.Relics);
        Assert.Equal(hold.Treatment, clicked);

        state.Click(TacticalLayer.Order);
        state.Back();
        Assert.Null(state.Active);

        // D5：HUD 上的信息层按钮（鼠标点击）走状态机的 Toggle，界面侧不复写判断；Toggle 与"点击模式下的 Press"必须逐步等价。
        // check 阶段发现 Godot 的 GameRoot.ToggleLayer 自己写了 `Active == layer ? Back() : Press(layer)`，已收回状态机。
        // 变异验证 M-I4（check 阶段实做）：Toggle 改为 `Active = layer`（不退出）→ 本测试红 1。
        var viaToggle = new TacticalLayerState();
        var viaClick = new TacticalLayerState(LayerInputMode.ClickToToggle);
        foreach (TacticalLayer layer in new[] { TacticalLayer.Power, TacticalLayer.Power, TacticalLayer.Relics, TacticalLayer.Relics, TacticalLayer.Territory })
        {
            viaToggle.Toggle(layer);
            viaClick.Press(layer);
            Assert.Equal(viaClick.Active, viaToggle.Active);
        }

        Assert.Equal(TacticalLayer.Territory, viaToggle.Active);

        // 按住模式下点按钮同样是切换：Toggle 不看 Mode
        var holding = new TacticalLayerState();
        holding.Toggle(TacticalLayer.Order);
        holding.Toggle(TacticalLayer.Order);
        Assert.Null(holding.Active);
    }
}
