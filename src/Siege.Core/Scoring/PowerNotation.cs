using System.Globalization;
using System.Numerics;

namespace Siege.Core.Scoring;

/// <summary>
/// 势力 / 军势的<b>显示</b>记法（restore-go-core-rules design Open Question 4）：不封顶之后单串军势可达几十位十进制，
/// 终端与图形界面的概览栏需要一个不换行、不撑破布局的短写法。只做"精确整数 → 文字"，比较与排序 MUST 始终用精确值。
/// </summary>
/// <remarks>
/// <para>放在 Core 是因为终端版（<c>Siege.Sim</c>，不引用表现层）与图形版（经 <c>Siege.Presentation</c>）共用这一份——两份缩写规则会让同一个值在两个入口显示不同。</para>
/// <para>全程整数与字符串运算，不经浮点（计分路径的"无浮点"守门同样扫到本文件）。</para>
/// </remarks>
public static class PowerNotation
{
    /// <summary>缩写阈值：绝对值达到 10^6 起缩写，以下原样给精确值。</summary>
    public static readonly BigInteger CompactThreshold = BigInteger.Pow(10, 6);

    /// <summary>用后缀的最大十进制指数（T = 10^12 这一档的上沿）；再往上改用 <c>e</c> 记数。</summary>
    private const int MaxSuffixExponent = 14;

    private static readonly string[] Suffixes = ["M", "B", "T"];

    /// <summary>
    /// 短写法：|v| &lt; 10^6 → 精确十进制；否则三位有效数字、向零截断（不四舍五入，<c>999999999</c> 写成 <c>999M</c> 而不是 <c>1000M</c>）：
    /// 10^6–10^14 用 M / B / T 后缀（<c>1.23M</c>、<c>12.3B</c>、<c>999T</c>），10^15 起用 <c>d.dde指数</c>（<c>5.15e47</c>）。负数前置 <c>-</c>。
    /// </summary>
    public static string Compact(BigInteger value)
    {
        if (BigInteger.Abs(value) < CompactThreshold)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        string sign = value.Sign < 0 ? "-" : string.Empty;
        string digits = BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture);
        int exponent = digits.Length - 1;
        if (exponent <= MaxSuffixExponent)
        {
            int group = exponent / 3;               // 2 = M、3 = B、4 = T
            int integerDigits = (exponent % 3) + 1; // 小数点前的位数 1–3
            string mantissa = integerDigits == 3 ? digits[..3] : $"{digits[..integerDigits]}.{digits[integerDigits..3]}";
            return $"{sign}{mantissa}{Suffixes[group - 2]}";
        }

        return $"{sign}{digits[0]}.{digits[1..3]}e{exponent.ToString(CultureInfo.InvariantCulture)}";
    }
}
