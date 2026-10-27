using System.Collections.Immutable;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 一批地图刷新的记录（relic-redesign D2 / R22，全公开）：预告（落位大回合、每件的位置与种类）与落位结果。
/// 尚未落位时 <see cref="Landed"/> 为 <c>null</c>；落位后是实际写入盘面的信物（被占格跳过，只少不多，按预告次序分配编号）。
/// </summary>
public sealed record RelicBatchRecord(RelicForecast Forecast, ImmutableArray<BoardRelic>? Landed)
{
    /// <summary>是否已落位。</summary>
    public bool HasLanded => Landed is not null;

    /// <summary>单行文本（日志与终端）：<c>R{预告大回合}→R{落位大回合} 预告 n 件[：…]；落位 m 件[：…]</c>。</summary>
    public override string ToString()
    {
        string forecast = Forecast.IsEmpty ? "预告 0 件" : $"预告 {Forecast.Slots.Length} 件：{string.Join("、", Forecast.Slots)}";
        string landed = Landed is not { } l ? "未落位"
            : l.IsEmpty ? "落位 0 件"
            : $"落位 {l.Length} 件：{string.Join("、", l.Select(r => $"{r.Cell.ToNotation()} {r.Token}"))}";
        return $"R{Forecast.ForecastRound}→R{Forecast.LandingRound} {forecast}；{landed}";
    }

    /// <summary>值相等：预告与落位结果逐项比较（<see cref="ImmutableArray{T}"/> 默认是引用相等）。</summary>
    public bool Equals(RelicBatchRecord? other) =>
        other is not null
        && Forecast.Equals(other.Forecast)
        && Landed.HasValue == other.Landed.HasValue
        && (Landed is not { } a || a.SequenceEqual(other.Landed!.Value));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Forecast);
        if (Landed is { } landed)
        {
            foreach (BoardRelic relic in landed)
            {
                hash.Add(relic);
            }
        }

        return hash.ToHashCode();
    }
}
