using System;
using System.Collections.Generic;
using System.Linq;

namespace Siege.Godot;

/// <summary>
/// 图形版的命令行参数（<c>--flag</c> / <c>--key=value</c>）。严格解析（testing.md「静默忽略的输入会产出口径错误的数据」）：
/// <c>--</c> 之后的用户参数里，没有被任何读取方认领的选项、或值解析不了的选项，一律报错，不静默忽略。
/// </summary>
/// <remarks>
/// 与 <c>Siege.Sim</c> 的 strict-cli 同一做法：<b>合法选项集合 = 被读取过的名字</b>，没有另一张白名单表——
/// 加一个新选项只需要加一处读取。引擎参数（<c>--</c> 之前，如 <c>--path</c> / <c>--headless</c>）仍可被读取（历史上两处都认），
/// 但不参与未知选项校验：那是 Godot 自己的选项集合。
/// <para>读取时发现的错误（开关带值、值为空、值解析不了）先记下、按"未给出"返回，统一在 <see cref="EnsureRecognized"/> 里与未知选项一起报出——
/// 那时合法选项集合才完整，每一种错误都能附上全部合法选项。</para>
/// </remarks>
public sealed class LaunchArgs
{
    private readonly List<(string Name, string? Value)> _user;
    private readonly List<(string Name, string? Value)> _engine;
    private readonly SortedSet<string> _known = new(StringComparer.Ordinal);
    private readonly List<string> _errors = [];

    public LaunchArgs(IEnumerable<string> userArgs, IEnumerable<string> engineArgs)
    {
        _user = [.. userArgs.Select(Split)];
        _engine = [.. engineArgs.Select(Split)];
    }

    /// <summary>开关：给出即为真；写成 <c>--flag=值</c> 记为错误。</summary>
    public bool Flag(string name)
    {
        if (!TryRead(name, out string? value))
        {
            return false;
        }

        if (value is not null)
        {
            _errors.Add($"--{name} 是开关，不带值。");
            return false;
        }

        return true;
    }

    /// <summary>带值选项的原文；未给出为 <c>null</c>，给了但值为空记为错误。</summary>
    public string? Text(string name, string what)
    {
        if (!TryRead(name, out string? value))
        {
            return null;
        }

        if (string.IsNullOrEmpty(value))
        {
            _errors.Add($"--{name}= 后面必须给{what}。");
            return null;
        }

        return value;
    }

    /// <summary>带值选项，经 <paramref name="parse"/> 解析；解析不了记为错误（结算时报出，不会静默按"未给出"开局）。</summary>
    public T? Value<T>(string name, string what, Func<string, T?> parse)
        where T : struct
    {
        if (Text(name, what) is not { } text)
        {
            return null;
        }

        T? parsed = parse(text);
        if (parsed is null)
        {
            _errors.Add($"--{name}={text} 无效：应为{what}。");
        }

        return parsed;
    }

    /// <summary>结算：用户参数里存在未被读取过的选项、或读取时记下过错误即抛出，并列出全部合法选项。MUST 在全部读取之后、建局之前调用。</summary>
    public void EnsureRecognized()
    {
        string[] unknown = [.. _user.Where(a => !_known.Contains(a.Name)).Select(a => a.Name.Length == 0 ? "（空）" : a.Name).Distinct().Order(StringComparer.Ordinal)];
        if (unknown.Length > 0)
        {
            _errors.Insert(0, $"未知选项 {string.Join("、", unknown)}。");
        }

        if (_errors.Count > 0)
        {
            throw new FormatException($"{string.Join(" ", _errors)}合法选项：{string.Join(" ", _known)}");
        }
    }

    private static (string Name, string? Value) Split(string arg)
    {
        int eq = arg.IndexOf('=', StringComparison.Ordinal);
        return eq < 0 ? (arg, null) : (arg[..eq], arg[(eq + 1)..]);
    }

    private bool TryRead(string name, out string? value)
    {
        _known.Add("--" + name);
        foreach ((string candidate, string? v) in _user.Concat(_engine))
        {
            if (candidate == "--" + name)
            {
                value = v;
                return true;
            }
        }

        value = null;
        return false;
    }
}
