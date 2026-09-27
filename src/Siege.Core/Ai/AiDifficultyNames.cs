namespace Siege.Core.Ai;

/// <summary>
/// 难度名称的唯一解析（expert-lookahead D10 / simulation-harness「各入口的难度选项」）：批量跑局 <c>--difficulty</c>、终端 <c>play --difficulty</c>、
/// 配置文件与日志首部的 <c>Players[].Difficulty</c>、图形版 <c>--difficulty=</c> 共用这一处。
/// </summary>
/// <remarks>
/// 只接受 <see cref="AiDifficulty"/> 的四个成员名（不区分大小写）；数字（含既有三档的序号）、未知名称与空值一律拒绝，MUST NOT 回落到缺省值。
/// 此前入口用的 <c>Enum.Parse</c> 会接受 <c>"3"</c> 这类数字串，产生未定义或意外的枚举值。
/// </remarks>
public static class AiDifficultyNames
{
    private static readonly string[] Names = Enum.GetNames<AiDifficulty>();

    /// <summary>四个可用名称，按枚举次序以 <c>|</c> 连接（用法说明与错误信息共用）。</summary>
    public static string Usage { get; } = string.Join("|", Names);

    /// <summary>按名称解析；不合法返回 <c>false</c>。只比对成员名本身（不区分大小写、不去空白），数字串一律不认。</summary>
    public static bool TryParse(string? text, out AiDifficulty difficulty)
    {
        string? name = text is null ? null : Array.Find(Names, n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
        difficulty = name is null ? default : Enum.Parse<AiDifficulty>(name);
        return name is not null;
    }

    /// <summary>按名称解析；不合法抛 <see cref="ArgumentException"/>，消息列出可用名称。</summary>
    public static AiDifficulty Parse(string? text) =>
        TryParse(text, out AiDifficulty difficulty)
            ? difficulty
            : throw new ArgumentException($"未知难度 {(text is null ? "（空）" : $"\"{text}\"")}：难度只接受名称 {Usage}（不区分大小写），不接受数字。");
}
