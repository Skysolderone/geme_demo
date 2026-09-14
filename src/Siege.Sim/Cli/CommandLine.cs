namespace Siege.Sim.Cli;

/// <summary>极简命令行解析：<c>--key value</c> / <c>--flag</c>，其余为位置参数。不引第三方包。</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private readonly List<string> _positional = [];

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

    /// <summary>是否给出了某选项。</summary>
    public bool Has(string key) => _options.ContainsKey(key);

    /// <summary>字符串选项；缺省返回 <paramref name="fallback"/>。</summary>
    public string Get(string key, string fallback) => _options.TryGetValue(key, out string? v) ? v : fallback;

    /// <summary>字符串选项；缺省为 <c>null</c>。</summary>
    public string? GetOrNull(string key) => _options.TryGetValue(key, out string? v) ? v : null;

    /// <summary>整数选项；缺省返回 <paramref name="fallback"/>，格式错误抛出。</summary>
    public int GetInt(string key, int fallback) =>
        _options.TryGetValue(key, out string? v) ? int.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;

    /// <summary>无符号 64 位选项（十进制或 <c>0x</c> 十六进制）。</summary>
    public ulong GetUInt64(string key, ulong fallback)
    {
        if (!_options.TryGetValue(key, out string? v))
        {
            return fallback;
        }

        return v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt64(v[2..], 16)
            : ulong.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>布尔开关。</summary>
    public bool Flag(string key) => _options.TryGetValue(key, out string? v) && v is "true" or "1" or "yes";
}
