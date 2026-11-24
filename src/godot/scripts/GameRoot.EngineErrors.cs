using System.Threading;
using Godot;

namespace Siege.Godot;

/// <summary>
/// 无人值守自检的退出码兜底（testing.md「自检中途抛异常 MUST 以非 0 退出」）：
/// <c>_Process</c> 推进对局那一段已就地接住异常；但盘面层刷新、信号、延迟调用等其余回调里抛出的 C# 异常，引擎只记一笔错误就继续下一帧，
/// 自检照样跑完报 0（terrain-bonus 段 B 实测：盘面层"是气但未被覆盖"抛出，进程仍退出 0）。
/// 这里挂一个引擎记录器，统计错误级（非警告）条目；无人值守的各个退出点经 <see cref="QuitChecked"/> 收口：本想以 0 退出而期间记过错误即改为 1。
/// </summary>
public sealed partial class GameRoot
{
    private EngineErrorCounter? _engineErrors;

    /// <summary>引擎记录器：只计数，不改输出（引擎照常打印）。可能在非主线程回调，计数用原子操作。</summary>
    private sealed partial class EngineErrorCounter : Logger
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, global::Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            if (errorType != (int)ErrorType.Warning)
            {
                Interlocked.Increment(ref _count);
            }
        }
    }

    private void InstallEngineErrorCounter()
    {
        _engineErrors = new EngineErrorCounter();
        OS.AddLogger(_engineErrors);
    }

    public override void _ExitTree()
    {
        // 退出前摘掉记录器，免得引擎关停阶段还回调已在终结的托管对象。
        if (_engineErrors is not null)
        {
            OS.RemoveLogger(_engineErrors);
            _engineErrors = null;
        }
    }

    /// <summary>无人值守退出点统一走这里：<paramref name="code"/> 为 0 而运行期间引擎记过错误时，打印并改以 1 退出。</summary>
    private void QuitChecked(int code)
    {
        int errors = _engineErrors?.Count ?? 0;
        if (code == 0 && errors > 0)
        {
            GD.PrintErr($"[siege] 错误：无人值守运行期间引擎记录了 {errors} 条错误（多为回调里抛出的异常，见上方 ERROR 行），自检改以 1 退出。");
            code = 1;
        }

        GetTree().Quit(code);
    }
}
