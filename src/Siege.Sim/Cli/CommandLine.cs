namespace Siege.Sim.Cli;

/// <summary>
/// 极简命令行解析：<c>--key value</c> / <c>--flag</c>，其余为位置参数。不引第三方包。
/// <para>
/// strict-cli：每个读取方法都把 key 记进消费集合，子命令解析完后调 <see cref="EnsureRecognized"/> 结算。
/// 没人认领的选项一律报错——静默忽略的选项会产出"命令跑通了但参数没生效"的口径错误数据（实例：<c>--matches 200</c> 跑成 1 局）。
/// </para>
/// </summary>
public sealed class CommandLine
{
    /// <summary>建议"最相近合法选项"的编辑距离上限（design.md D3）。超过它就只报全部合法选项。</summary>
    private const int MaxSuggestDistance = 3;

    /// <summary>
    /// 已删除的选项 → 删除说明（restore-go-core-rules tasks 5.1）。传入它们 MUST 报"已删除"并说明原因——
    /// 笼统的"未知选项"会让人以为拼错了、去试相近的写法；静默忽略则是 strict-cli 要消灭的失败模式。对全部子命令生效。
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RetiredOptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["max-rounds"] = "大回合上限终局已随 restore-go-core-rules（裁决 #4）删除，对局只剩三类终局；批量跑局要限长请用 --turn-limit（小回合数截断）",
        ["dominance-start"] = "势力碾压已随 restore-go-core-rules（裁决 #4）删除",
        ["no-catch-up"] = "落后者征募补偿已随 restore-go-core-rules（裁决 #7）删除",
        ["site-values"] = "据点已随 restore-go-core-rules（裁决 #14）整体移除",
    };

    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private readonly List<string> _positional = [];

    /// <summary>被任一读取方法取用过的 key（无论该选项是否真的给出）——即"本子命令认识的选项"。</summary>
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    public CommandLine(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string[] list = [.. args];
        for (int i = 0; i < list.Length; i++)
        {
            string arg = list[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                string key = arg[2..];
                if (i + 1 < list.Length && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    _options[key] = list[++i];
                }
                else
                {
                    _options[key] = "true";
                }
            }
            else
            {
                _positional.Add(arg);
            }
        }
    }

    /// <summary>位置参数。</summary>
    public IReadOnlyList<string> Positional => _positional;

    /// <summary>是否给出了某选项。条件读取里 <c>Has</c> 就是该选项的读取方，同样计入消费（design.md D1）。</summary>
    public bool Has(string key) => TryRead(key, out _);

    /// <summary>字符串选项；缺省返回 <paramref name="fallback"/>。</summary>
    public string Get(string key, string fallback) => TryRead(key, out string? v) ? v! : fallback;

    /// <summary>字符串选项；缺省为 <c>null</c>。</summary>
    public string? GetOrNull(string key) => TryRead(key, out string? v) ? v : null;

    /// <summary>整数选项；缺省返回 <paramref name="fallback"/>，格式错误抛出。</summary>
    public int GetInt(string key, int fallback) =>
        TryRead(key, out string? v) ? int.Parse(v!, System.Globalization.CultureInfo.InvariantCulture) : fallback;

    /// <summary>无符号 64 位选项（十进制或 <c>0x</c> 十六进制）。</summary>
    public ulong GetUInt64(string key, ulong fallback)
    {
        if (!TryRead(key, out string? v))
        {
            return fallback;
        }

        return v!.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt64(v[2..], 16)
            : ulong.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>布尔开关。</summary>
    public bool Flag(string key) => TryRead(key, out string? v) && v is "true" or "1" or "yes";

    /// <summary>
    /// 本子命令的合法选项 = 已被读取方取用的 key，按字典序（Ordinal）稳定排序（错误信息要可复现）。
    /// <para>
    /// 零白名单：没有"声明但未读取"的口子。某选项只在另一分支才被读到时（如 replay 的 <c>--file</c> 与 <c>--dir/--seed</c>），
    /// 由子命令自己把互斥关系报成错误，而不是在这里声明放行——放行等于恢复静默忽略（design.md D1 / D2）。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> LegalOptions() => [.. _consumed.Order(StringComparer.Ordinal)];

    /// <summary>给出了但不属于合法选项集合的选项名，按字典序（Ordinal）稳定排序。</summary>
    public IReadOnlyList<string> UnrecognizedOptions()
    {
        IReadOnlyList<string> legal = LegalOptions();
        return [.. _options.Keys.Where(k => !legal.Contains(k)).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// 结算：存在未被本子命令读取的选项时抛出，报出未知选项名与最相近的合法选项。
    /// 必须在创建输出目录与执行对局之前调用（design.md D4：半份输出比没有输出更糟）。
    /// </summary>
    public void EnsureRecognized()
    {
        IReadOnlyList<string> unknown = UnrecognizedOptions();
        if (unknown.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> retired = [.. unknown.Where(RetiredOptions.ContainsKey)];
        if (retired.Count > 0)
        {
            throw new ArgumentException(string.Join("；", retired.Select(k => $"选项 --{k} 已删除：{RetiredOptions[k]}")) + "。");
        }

        IReadOnlyList<string> legal = LegalOptions();
        string named = string.Join("、", unknown.Select(k => Describe(k, legal)));
        string all = legal.Count == 0 ? "（无）" : string.Join(" ", legal.Select(k => $"--{k}"));
        throw new ArgumentException($"未知选项 {named}。合法选项：{all}");
    }

    /// <summary>最相近的合法选项：编辑距离 ≤ <see cref="MaxSuggestDistance"/> 且最小；并列取字典序靠前者。没有够近的返回 <c>null</c>。</summary>
    internal static string? NearestOption(string unknown, IReadOnlyList<string> legal)
    {
        ArgumentNullException.ThrowIfNull(legal);
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (string candidate in legal.Order(StringComparer.Ordinal))
        {
            int distance = EditDistance(unknown, candidate);
            if (distance <= MaxSuggestDistance && distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Levenshtein 编辑距离（滚动两行）。</summary>
    internal static int EditDistance(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int[] previous = [.. Enumerable.Range(0, b.Length + 1)];
        int[] current = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string Describe(string unknown, IReadOnlyList<string> legal) =>
        NearestOption(unknown, legal) is { } near ? $"--{unknown}（是否想用 --{near}？）" : $"--{unknown}";

    private bool TryRead(string key, out string? value)
    {
        _consumed.Add(key);
        return _options.TryGetValue(key, out value);
    }
}
