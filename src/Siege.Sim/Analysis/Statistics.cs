namespace Siege.Sim.Analysis;

// 本文件属于离线分析输出层：允许浮点（裁决 14）。任何数值都不回流到对局状态。

/// <summary>比例的置信区间。</summary>
public sealed record Proportion(int Successes, int Trials, double Lower, double Upper)
{
    public double Value => Trials == 0 ? double.NaN : (double)Successes / Trials;

    public bool IsEmpty => Trials == 0;

    /// <summary>某个参考值是否落在区间之外（即差异在 95% 水平上显著）。</summary>
    public bool Excludes(double reference) => !IsEmpty && (reference < Lower || reference > Upper);

    public override string ToString() => IsEmpty
        ? "无样本"
        : $"{Value * 100:F1}% ({Successes}/{Trials}，95% 区间 {Lower * 100:F1}%–{Upper * 100:F1}%)";
}

/// <summary>区间目标的偏离评估。</summary>
public enum DeviationDirection
{
    Within,
    Below,
    Above,
    Unmeasurable,
}

/// <summary>对照目标区间的评估结果：方向与幅度。</summary>
public sealed record Deviation(DeviationDirection Direction, double Value, double Low, double High, string Unit)
{
    /// <summary>偏离绝对幅度（在区间内为 0）。</summary>
    public double Amount => Direction switch
    {
        DeviationDirection.Below => Low - Value,
        DeviationDirection.Above => Value - High,
        _ => 0,
    };

    /// <summary>相对越界端的百分比幅度。</summary>
    public double Percent => Direction switch
    {
        DeviationDirection.Below when Low != 0 => Amount / Low * 100,
        DeviationDirection.Above when High != 0 => Amount / High * 100,
        _ => 0,
    };

    public override string ToString() => Direction switch
    {
        DeviationDirection.Within => $"在目标 {Range()} 内（实测 {Value:0.##}{Unit}）",
        // 越界端为 0 时（如截断率目标 0）相对幅度无意义，只给绝对幅度。
        DeviationDirection.Below => $"偏离：低于目标下限 {Low:0.##}{Unit}，实测 {Value:0.##}{Unit}，低 {Amount:0.##}{Unit}{(Low != 0 ? $"（-{Percent:F0}%）" : string.Empty)}",
        DeviationDirection.Above => $"偏离：超出目标上限 {High:0.##}{Unit}，实测 {Value:0.##}{Unit}，高 {Amount:0.##}{Unit}{(High != 0 ? $"（+{Percent:F0}%）" : string.Empty)}",
        _ => $"不可测（目标 {Range()}）",
    };

    private string Range() => $"{Low:0.##}–{High:0.##}{Unit}";
}

/// <summary>统计工具：Wilson 区间、均值、中位数、偏离评估。</summary>
public static class Statistics
{
    /// <summary>95% 的正态分位数。</summary>
    public const double Z95 = 1.959963984540054;

    /// <summary>Wilson 得分区间（不是正态近似）：小样本与接近 0 / 1 的比例下仍在 [0, 1] 内。</summary>
    public static Proportion Wilson(int successes, int trials, double z = Z95)
    {
        if (trials <= 0)
        {
            return new Proportion(0, 0, double.NaN, double.NaN);
        }

        double n = trials;
        double p = successes / n;
        double z2 = z * z;
        double denominator = 1 + (z2 / n);
        double center = (p + (z2 / (2 * n))) / denominator;
        double half = z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4 * n * n))) / denominator;
        return new Proportion(successes, trials, Math.Max(0, center - half), Math.Min(1, center + half));
    }

    public static double Mean(IEnumerable<double> values)
    {
        double sum = 0;
        int n = 0;
        foreach (double v in values)
        {
            sum += v;
            n++;
        }

        return n == 0 ? double.NaN : sum / n;
    }

    public static double Mean(IEnumerable<long> values) => Mean(values.Select(v => (double)v));

    public static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>对照区间目标评估；<paramref name="value"/> 为 NaN 视为不可测。</summary>
    public static Deviation Assess(double value, double low, double high, string unit = "")
    {
        if (double.IsNaN(value))
        {
            return new Deviation(DeviationDirection.Unmeasurable, value, low, high, unit);
        }

        DeviationDirection direction = value < low ? DeviationDirection.Below : value > high ? DeviationDirection.Above : DeviationDirection.Within;
        return new Deviation(direction, value, low, high, unit);
    }

    /// <summary>明确标为不可测的指标（裁决 10）。</summary>
    public static Deviation Unmeasurable(double low, double high, string unit = "") =>
        new(DeviationDirection.Unmeasurable, double.NaN, low, high, unit);
}
