using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>一次已提交盘面：提交序号（从 1 起，按提交顺序连续）+ 结算后盘面的确定性序列化。</summary>
public sealed record CommittedBoard(int Sequence, string Board);

/// <summary>
/// 本局全部<b>合法批次结算后</b>的盘面集合，服务盘面同形禁则（设计文档 §6.2）。
/// </summary>
/// <remarks>
/// <para>条目存序列化结果（含棋子类型，存档格式不变）；比对只经 <see cref="GameBoard.SuperkoKey"/> 投影出的同形比对键
/// （每格占用者 + 设施与地表，不含类型，superko-occupancy D1）。内部维护"比对键 → 最早提交序号"的索引（D2），
/// 保留序号是为了在同形失败时告诉玩家"和第几次提交重复"。</para>
/// <para>全局生效、不区分提交者（裁决记录 4）。预览中间态 MUST NOT 记入；Pass 不改变盘面，也不记入。</para>
/// <para>集合是对局状态的一部分，MUST 随存档持久化：<see cref="Serialize"/> / <see cref="Deserialize(string)"/>。
/// 索引不持久化，读档时由序列化结果重建；旧规则下产生的历史可能含只差类型的多条提交，索引取其中最早的一条。</para>
/// </remarks>
public sealed class BoardHistory
{
    private readonly List<CommittedBoard> _entries = [];
    private readonly Dictionary<string, int> _earliestByKey = new(StringComparer.Ordinal);

    /// <summary>已提交次数，也是最新的提交序号。</summary>
    public int Count => _entries.Count;

    /// <summary>按提交顺序排列的全部条目。</summary>
    public IReadOnlyList<CommittedBoard> Entries => _entries;

    /// <summary>
    /// 若 <paramref name="board"/>（盘面序列化结果）与某次历史提交的同形比对键相同，返回最早那次的提交序号；否则 <c>null</c>。
    /// 只差棋子类型的盘面视为同形。
    /// </summary>
    public int? FindDuplicate(string board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return _earliestByKey.TryGetValue(GameBoard.SuperkoKey(board), out int sequence) ? sequence : null;
    }

    /// <summary>记录一次合法批次结算后的盘面，返回其提交序号。只允许由结算驱动器在正式结算时调用。</summary>
    public int Record(string board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int sequence = _entries.Count + 1;
        Append(new CommittedBoard(sequence, board));
        return sequence;
    }

    /// <summary>持久化：每行一条 <c>序号:盘面</c>，按提交顺序。空集合为空串。</summary>
    public string Serialize() =>
        string.Join('\n', _entries.Select(e => string.Concat(e.Sequence.ToString(), ":", e.Board)));

    /// <summary>
    /// 从 <see cref="Serialize"/> 的输出恢复。序号 MUST 从 1 起连续，否则视为存档损坏。
    /// 容忍 <c>\r\n</c> 行尾与结尾多余的换行（存档经文本层或编辑器往返后常见），但不容忍其他任何偏差。
    /// </summary>
    public static BoardHistory Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var history = new BoardHistory();
        text = text.TrimEnd('\r', '\n');
        if (text.Length == 0)
        {
            return history;
        }

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || !int.TryParse(line.AsSpan(0, colon), out int sequence))
            {
                throw new FormatException($"已提交盘面历史格式错误：\"{line}\"。");
            }

            if (sequence != history._entries.Count + 1)
            {
                throw new FormatException(
                    $"已提交盘面历史序号不连续：期望 {history._entries.Count + 1}，实际 {sequence}。");
            }

            history.Append(new CommittedBoard(sequence, line[(colon + 1)..]));
        }

        return history;
    }

    /// <summary>新增与读档共用：记条目并更新索引。同键已有更早的提交时保留更早的序号。</summary>
    private void Append(CommittedBoard entry)
    {
        _earliestByKey.TryAdd(GameBoard.SuperkoKey(entry.Board), entry.Sequence);
        _entries.Add(entry);
    }
}
