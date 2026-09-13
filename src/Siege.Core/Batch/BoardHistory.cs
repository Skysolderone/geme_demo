using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>一次已提交盘面：提交序号（从 1 起，按提交顺序连续）+ 结算后盘面的确定性序列化。</summary>
public sealed record CommittedBoard(int Sequence, string Board);

/// <summary>
/// 本局全部<b>合法批次结算后</b>的盘面集合，服务盘面同形禁则（设计文档 §6.2）。
/// </summary>
/// <remarks>
/// <para>按"内容哈希 + 序号 + 完整内容"存储（design.md D4）：哈希只做预筛，命中后 MUST 按内容再比对一次，
/// 不能只信哈希。保留序号是为了在同形失败时告诉玩家"和第几次提交重复"。</para>
/// <para>全局生效、不区分提交者（裁决记录 4）。预览中间态 MUST NOT 记入；Pass 不改变盘面，也不记入。</para>
/// <para>集合是对局状态的一部分，MUST 随存档持久化：<see cref="Serialize"/> / <see cref="Deserialize(string)"/>。
/// 哈希不持久化，恢复时按内容重算，哈希函数变更不会让旧存档失效。</para>
/// </remarks>
public sealed class BoardHistory
{
    private readonly List<CommittedBoard> _entries = [];
    private readonly Dictionary<int, List<int>> _indexByHash = [];
    private readonly Func<string, int> _hasher;

    public BoardHistory()
        : this(Fnv1a)
    {
    }

    /// <summary>测试接缝：注入哈希函数以构造碰撞，验证命中后仍按内容比对。</summary>
    internal BoardHistory(Func<string, int> hasher)
    {
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
    }

    /// <summary>已提交次数，也是最新的提交序号。</summary>
    public int Count => _entries.Count;

    /// <summary>按提交顺序排列的全部条目。</summary>
    public IReadOnlyList<CommittedBoard> Entries => _entries;

    /// <summary>若 <paramref name="board"/> 与某次历史提交内容完全相同，返回该次提交序号；否则 <c>null</c>。</summary>
    public int? FindDuplicate(string board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!_indexByHash.TryGetValue(_hasher(board), out List<int>? indices))
        {
            return null;
        }

        // 哈希命中只是预筛：必须按内容逐条再比对，碰撞不得误判为同形。
        foreach (int index in indices)
        {
            if (string.Equals(_entries[index].Board, board, StringComparison.Ordinal))
            {
                return _entries[index].Sequence;
            }
        }

        return null;
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
    public static BoardHistory Deserialize(string text) => Deserialize(text, Fnv1a);

    internal static BoardHistory Deserialize(string text, Func<string, int> hasher)
    {
        ArgumentNullException.ThrowIfNull(text);
        var history = new BoardHistory(hasher);
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

    private void Append(CommittedBoard entry)
    {
        int hash = _hasher(entry.Board);
        if (!_indexByHash.TryGetValue(hash, out List<int>? indices))
        {
            indices = [];
            _indexByHash[hash] = indices;
        }

        indices.Add(_entries.Count);
        _entries.Add(entry);
    }

    /// <summary>FNV-1a 32 位，逐字符。与进程、平台无关——<c>string.GetHashCode</c> 每进程随机化，不可用。</summary>
    internal static int Fnv1a(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char ch in text)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (int)hash;
        }
    }
}
