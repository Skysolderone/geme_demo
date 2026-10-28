using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Siege.Core.PieceRelics;

namespace Siege.Core.Board;

/// <summary>
/// 盘面的信物层（relic-redesign design.md D1）：每格至多 1 件 <see cref="RelicToken"/>。格上有子 = 挂在该子上；空格 = 地图信物。
/// </summary>
/// <remarks>
/// <para><b>不另存持有者</b>：拾取 = <see cref="Place"/> 落到有信物的空格（信物不动，自然变成"挂上"）；
/// 掉落 = <see cref="RemoveStones"/> / <see cref="Clear"/> 移走棋子（信物不动，自然变成"地图信物"）。两个写入原语都不碰信物层。</para>
/// <para>稀疏存放、空层为 <c>null</c>：空层时 <see cref="Clone"/>、<see cref="Serialize"/>、覆盖与计分多出的代价为零，行为与信物层上线之前逐字节相同。</para>
/// <para>挂载（<see cref="AttachRelic"/>）是写入原语：推进突变版本号，令序列化记忆失效。同形禁则不比较信物层（R28，<see cref="SuperkoKey"/> 剥掉信物段）。</para>
/// <para>对局流程经 <c>MatchFlow</c> 在大回合开始时按 <see cref="RelicSpawner"/> 落位地图信物（relic-redesign 接线段 W1）。</para>
/// </remarks>
public sealed partial class GameBoard
{
    /// <summary>棋子网格（及改造段）与信物段之间的分隔符。改造段只含字母、数字、<c>:</c>、<c>,</c>、<c>-</c>，不会出现它。</summary>
    private const char RelicSeparator = '#';

    /// <summary>信物层：格下标 → 信物，按格下标升序（即坐标字典序）。空层为 <c>null</c>。</summary>
    private SortedDictionary<int, RelicToken>? _relics;

    /// <summary>信物层是否非空。</summary>
    public bool HasRelics => _relics is { Count: > 0 };

    /// <summary>该格的信物（无则 <c>null</c>）。格上有子即挂在该子上。</summary>
    public RelicToken? RelicAt(Coord c)
    {
        RequireInBounds(c);
        return _relics is not null && _relics.TryGetValue(Index(c), out RelicToken token) ? token : null;
    }

    /// <summary>格下标处的信物（热路径：不经坐标越界检查）。</summary>
    private RelicToken? RelicAtIndex(int index) =>
        _relics is not null && _relics.TryGetValue(index, out RelicToken token) ? token : null;

    /// <summary>盘面上的全部信物，按所在格的坐标字典序（先行后列）。空层为空数组。</summary>
    public ImmutableArray<BoardRelic> RelicsOnBoard()
    {
        if (_relics is not { Count: > 0 } relics)
        {
            return ImmutableArray<BoardRelic>.Empty;
        }

        ImmutableArray<BoardRelic>.Builder builder = ImmutableArray.CreateBuilder<BoardRelic>(relics.Count);
        foreach ((int index, RelicToken token) in relics)
        {
            builder.Add(new BoardRelic(CoordAt(index), token));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// <paramref name="player"/> 持有的信物件数 = 挂在其棋子上的信物件数（relic-redesign R24：终局 / 战场结算比较链的第二项"持有信物数"的唯一实现）。
    /// 不论信物种类、带入者与保险；地图信物（空格上）不计。
    /// </summary>
    public int HeldRelicCount(PlayerId player)
    {
        if (_relics is not { Count: > 0 } relics)
        {
            return 0;
        }

        int count = 0;
        foreach (int index in relics.Keys)
        {
            if (_occupants[index] is { } occupant && occupant.Owner == player)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 写入原语：把信物 <paramref name="token"/> 放到格 <paramref name="c"/>。格上有子即挂在该子上，空格即成为地图信物。<b>不做规则判定</b>——
    /// 刷新位置、挂出时机由调用方负责；本方法只守盘面不变量：格在盘内且可落子、该格尚无信物（R13 每子 1 件）、编号在盘面上唯一。任一不满足抛 <see cref="SiegeRuleException"/>，盘面不变。
    /// </summary>
    public void AttachRelic(Coord c, RelicToken token)
    {
        RequireInBounds(c);
        if (!Map.IsPlayable(c))
        {
            throw new SiegeRuleException($"信物只能放在可落子格：{c.ToNotation()} 不可落子。");
        }

        int index = Index(c);
        if (RelicAtIndex(index) is { } existing)
        {
            throw new SiegeRuleException($"{c.ToNotation()} 已有信物 {existing}，每格（每枚棋子）至多 1 件。");
        }

        if (_relics is not null)
        {
            foreach ((int other, RelicToken placed) in _relics)
            {
                if (placed.Id == token.Id)
                {
                    throw new SiegeRuleException($"信物编号 {token.Id} 已在 {CoordAt(other).ToNotation()} 使用，本局内编号必须唯一。");
                }
            }
        }

        (_relics ??= new SortedDictionary<int, RelicToken>()).Add(index, token);
        _version++;
    }

    /// <summary>副本复制信物层（<see cref="Clone"/> 调用）：空层仍为 <c>null</c>，非空则复制一份，副本与原盘面之后的挂载互不可见。</summary>
    private void CopyRelicsTo(GameBoard copy) => copy._relics = _relics is { Count: > 0 } relics ? new SortedDictionary<int, RelicToken>(relics) : null;

    /// <summary>
    /// 信物段文本（不含分隔符 <c>#</c>）：按格下标升序，每件 <c>记法:编号:类型码:带入者:保险</c>，件间以 <c>,</c> 分隔。
    /// 带入者为一位大写十六进制玩家编号，地图信物写 <c>-</c>；保险写 <c>1</c> / <c>0</c>。例：<c>E5:0:W:-:0,F7:3:G:2:1</c>。
    /// </summary>
    private string RelicSegment()
    {
        var sb = new StringBuilder();
        foreach ((int index, RelicToken token) in _relics!)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(CoordAt(index).ToNotation()).Append(':')
              .Append(token.Id.ToString(CultureInfo.InvariantCulture)).Append(':')
              .Append(RelicKinds.CodeOf(token.Kind)).Append(':');
            if (token.Origin is { } origin)
            {
                if (origin.Value is < 0 or > 15)
                {
                    throw new SiegeRuleException($"盘面序列化只支持玩家编号 0–15，信物 {token} 的带入者为 {origin.Value}。");
                }

                sb.Append(HexDigits[origin.Value]);
            }
            else
            {
                sb.Append('-');
            }

            sb.Append(':').Append(token.Insured ? '1' : '0');
        }

        return sb.ToString();
    }

    /// <summary><see cref="RelicSegment"/> 的反向实现：逐件解析并经 <see cref="AttachRelic"/> 写入。格式不符或违反盘面不变量即抛 <see cref="FormatException"/>。</summary>
    private void FillRelics(string segment)
    {
        if (segment.Length == 0)
        {
            throw new FormatException("盘面序列化的信物段为空（空信物层不写 '#'）。");
        }

        foreach (string entry in segment.Split(','))
        {
            string[] parts = entry.Split(':');
            if (parts.Length != 5
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || parts[2].Length != 1 || parts[3].Length != 1 || parts[4] is not ("0" or "1"))
            {
                throw new FormatException($"盘面序列化的信物项格式错误：\"{entry}\"。");
            }

            Coord cell;
            try
            {
                cell = Coord.Parse(parts[0]);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new FormatException($"盘面序列化的信物项坐标错误：\"{entry}\"。", ex);
            }

            if (!Contains(cell))
            {
                throw new FormatException($"盘面序列化的信物项坐标 {parts[0]} 超出棋盘范围。");
            }

            RelicKind kind = RelicKinds.KindFromCode(parts[2][0]);
            PlayerId? origin = parts[3] == "-" ? null : new PlayerId(HexValue(parts[3][0], entry));
            bool insured = parts[4] == "1";
            if (insured && origin is null)
            {
                throw new FormatException($"盘面序列化的信物项把地图信物标成了保险件：\"{entry}\"。");
            }

            try
            {
                AttachRelic(cell, new RelicToken(id, kind, origin, insured));
            }
            catch (SiegeRuleException ex)
            {
                throw new FormatException($"盘面序列化的信物段与盘面不符：{ex.Message}", ex);
            }
        }

        static int HexValue(char ch, string entry)
        {
            int value = HexDigits.IndexOf(ch, StringComparison.Ordinal);
            return value >= 0 ? value : throw new FormatException($"盘面序列化的信物项带入者不是一位大写十六进制：\"{entry}\"。");
        }
    }
}
