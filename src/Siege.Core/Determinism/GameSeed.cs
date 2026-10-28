using System.Text;

namespace Siege.Core.Determinism;

/// <summary>
/// 对局种子：全局唯一的随机根。本身不产生随机数，只按<b>名字</b>派生互相独立的子流（<see cref="Stream"/>）。
/// </summary>
/// <remarks>
/// <para>规范 <c>.trellis/spec/core/determinism.md</c>「随机子流隔离」：征募（<c>recruit</c>）、
/// 流程初始化（<c>setup</c>）、信物刷新（<c>relic-spawn:&lt;批&gt;</c>）等各走一条子流，改变其中一条的消费次数 MUST NOT 影响其他子流的取值。
/// 做法是每条子流的初始状态只由「种子 + 子流名」决定，与其他子流消费了多少无关。</para>
/// <para>派生用 FNV-1a 64 位散列子流名，与种子混合后经 SplitMix64 展开成 xoshiro256** 的 256 位状态。
/// 全部是位级确定的整数运算，不依赖 BCL 的随机类（其算法不承诺跨版本稳定）。</para>
/// </remarks>
public readonly record struct GameSeed(ulong Value)
{
    /// <summary>征募子流名。</summary>
    public const string Recruit = "recruit";

    /// <summary>流程初始化子流名。</summary>
    public const string Setup = "setup";

    // retire-legacy-maps 3.1：原型选区子流 zone-pick（边疆档区数多于人数时）与冒险抽签子流 flag-risk 只服务已删除的旧路径，子流名一并删除；
    // 链接模式的盲选用 FlagPick（flag-pick），取值不变。

    /// <summary>
    /// AI 带入的抽取子流名（relic-redesign R21 / D5，沿用 carry-in-out D7 的名字）：按玩家编号升序为每名 AI 抽带入的普通信物与保险件
    /// （消费顺序见 <c>PieceRelics.RelicCarryAi</c>）。独立命名，MUST NOT 扰动征募、流程初始化、信物刷新与 AI 决策的既有序列。
    /// 旧补给的征召签子流 <c>carry-draft:&lt;编号&gt;</c> 随补给删除（接线段 W2）。
    /// </summary>
    public const string CarryAi = "carry-ai";

    /// <summary>
    /// 链接模式的盲选插旗子流名（board-link-mode 实现裁决 B2，match-setup「原型插旗替代路径」）：每名玩家按编号升序各抽一次 <c>[0, 出生棋盘数)</c>，
    /// 未由人指定（原型路径）或时限到时尚未选择（正式插旗）的玩家取自己那一次的结果。不看任何已插的旗。
    /// 独立命名，MUST NOT 扰动首回合顺序、征募、信物刷新与 AI 决策的既有序列。
    /// </summary>
    public const string FlagPick = "flag-pick";

    /// <summary>
    /// 挂在棋子上的信物的地图刷新子流名 <c>relic-spawn:&lt;落位大回合&gt;</c>（relic-redesign D2）：每批一条，在落位前 1 个大回合预告时一次抽完本批的位置与类型。
    /// 按批命名（每批一条子流），刷新是纯函数、不需要持久化消费次数；任一批的消费次数 MUST NOT 扰动其他批，
    /// 更不扰动征募、流程初始化、盲选插旗、带入与 AI 决策的既有序列。唯一消费者 <c>PieceRelics.RelicSpawner</c>。
    /// </summary>
    public static string RelicSpawn(int landingRound) => $"relic-spawn:{landingRound}";

    /// <summary>
    /// 出局 / 弃赛者未挂出的非保险带入件掉落的子流名 <c>relic-drop:&lt;玩家编号&gt;</c>（relic-redesign R17）：每名玩家一条（一名玩家只会出局或弃赛一次）。
    /// 同样 MUST NOT 扰动任何既有子流。唯一消费者 <c>PieceRelics.RelicSpawner</c>。
    /// </summary>
    public static string RelicDrop(int player) => $"relic-drop:{player}";

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    /// <summary>派生一条命名子流。同名同种子 → 逐项一致的序列；每次调用都从头开始，返回的流各自独立推进。</summary>
    public RandomStream Stream(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("子流名不得为空。", nameof(name));
        }

        ulong hash = FnvOffset;
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        // 种子与名字散列各自过一次 SplitMix64 再异或，避免「种子 ^ 名字」这种线性混合让不同 (种子, 名字) 对撞出同一状态。
        ulong root = SplitMix64.Mix(Value) ^ SplitMix64.Mix(hash ^ Golden);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        return new RandomStream(name, s0, s1, s2, s3);
    }

    /// <summary>十六进制表示，写入对局记录。</summary>
    public override string ToString() => Value.ToString("X16");

    /// <summary>解析 <see cref="ToString"/> 的十六进制表示。</summary>
    public static GameSeed Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new GameSeed(Convert.ToUInt64(text.Trim(), 16));
    }
}

/// <summary>SplitMix64：只用于把 64 位根展开成子流初始状态，不直接对外产随机数。</summary>
internal static class SplitMix64
{
    internal static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        return Mix(state);
    }

    internal static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
