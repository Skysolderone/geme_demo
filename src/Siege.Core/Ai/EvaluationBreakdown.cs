using System.Collections.Immutable;
using System.Numerics;

namespace Siege.Core.Ai;

/// <summary>
/// 一次评价的分维度分解（design.md D2）：九个维度各自的原始值与权重，总分 = Σ 权重 × 原始值。
/// 全部整数，不含浮点；势力增量来自任意精度的势力值（restore-go-core-rules D1），因此原始值与总分同为 <see cref="BigInteger"/>，不溢出。
/// </summary>
public sealed record EvaluationBreakdown(ImmutableArray<BigInteger> Raw, EvaluationWeights Weights)
{
    /// <summary>维度数。</summary>
    public const int DimensionCount = 9;

    /// <summary>
    /// AI 评价版本，写入跑局日志首部（ai-eye 1.1）。1 = 七维（ai-eye 之前，旧日志首部缺该字段）；2 = ai-eye 九维（眼位、威胁）；
    /// 3 = 活形硬约束 + 停手阈值（ai-eye 段 B，裁决 R10）+ 预筛只算七维（段 C）；
    /// 4 = 链接模式下的决胜一手（ai-pass-review 裁决 P7 / P13；此前 board-link-mode 的 C15 落后者阈值取 0 未递增，一并归入本版）；
    /// 5 = AI 缺省按座位视野 + 专家前瞻名册限同战场（ai-seat-vision 裁决 V1 / V2 / D4：链接模式下专家的前瞻重排在全知与按座位视野下都变了）；
    /// 6 = 循环提子停手（cycle-stop 裁决 D2 / D3：停滞大回合数 K 缺省 6，连续 K 个大回合无净增且前 N 候选全是提子 / 反提即视同无改善手 Pass；K = 0 与版本 5 逐步相同）；
    /// 7 = 活形判定无视栅栏（fence-eye-rule 裁决 F5：眼位 / 安全 / 威胁读到的活形与眼空间随规则变了，同一局面的评价不同；评价代码本身未改）；
    /// 8 = 专家构筑期不前瞻 + 两层平局裁决（expert-tiebreak 裁决 1–5：构筑期决策直接取高难的选择；前瞻后分数即一层分数，两层加分只在两层平局带宽内裁决。简单 / 标准 / 高难逐步不变）；
    /// 9 = 专家前瞻的模拟对手改用专家本人的高难口径（expert-opponent-model 裁决 1–3：候选落点数、只看即时收益、候选格上限与停手阈值随专家本人，贪心条数 1、不扰动、不前瞻；原为标准预设。简单 / 标准 / 高难逐步不变）；
    /// 10 = 整批候选（ai-whole-batch 裁决 1–5：标准 / 高难 / 专家在既有候选之后追加剪枝候选与配对候选，按留一法口径施加停手阈值；Pass = 贪心无保留且整批候选为空。跑局层开关为 0 时与版本 9 逐步相同；简单难度逐步不变）；
    /// 11 = 搭桥候选（ai-bridge-candidate 裁决 1–6：标准 / 高难 / 专家在既有候选之后追加至多 1 个以搭桥单点为起点的不扰动候选，枚举全部合法空格、不经预筛、不受前 N；Pass 另要求搭桥候选为空。跑局层开关为 0 时与版本 10 逐步相同；简单难度逐步不变）；
    /// 12 = 旧地图信物删除、信物维改读挂在棋子上的信物（relic-redesign 接线段 W1：信物维由"控制 / 估计未揭示的地图信物格"改为 <c>RelicEstimate</c> 的未校准平值——己方棋子上的信物每件 +5、在局对手棋子上的每件 −5；
    /// 段 C 重写信物估值时再递增。盘面没有信物时各维原始值与版本 11 相同，但对局中途刷新的信物会被拾取，整局走法随之变）。
    /// 维度数、任一维原始值的算法、或"哪些候选参与打分 / 保留"的规则变化 MUST 递增它：旧版本日志的 AI 决策不能用新评价逐步重现，
    /// 回放按首部版本处理（版本不同即在首部报分歧、不重跑）。
    /// </summary>
    public const int Version = 12;

    /// <summary>全零分解（空批次 / Pass）。</summary>
    public static EvaluationBreakdown Zero(EvaluationWeights weights) => new([.. new BigInteger[DimensionCount]], weights);

    /// <summary>某维度的原始值。</summary>
    public BigInteger RawOf(EvaluationDimension dimension) => Raw[(int)dimension];

    /// <summary>某维度的加权贡献。</summary>
    public BigInteger ContributionOf(EvaluationDimension dimension) => Weights.Of(dimension) * Raw[(int)dimension];

    /// <summary>加权总分。</summary>
    public BigInteger Total
    {
        get
        {
            BigInteger total = BigInteger.Zero;
            for (int i = 0; i < DimensionCount; i++)
            {
                total += ContributionOf((EvaluationDimension)i);
            }

            return total;
        }
    }

    /// <summary>值相等：逐维比较（<see cref="ImmutableArray{T}"/> 默认是引用相等）。</summary>
    public bool Equals(EvaluationBreakdown? other) =>
        other is not null && Weights == other.Weights && Raw.SequenceEqual(other.Raw);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Weights);
        foreach (BigInteger value in Raw)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"势力{Raw[0]} 敌损{Raw[1]} 信物{Raw[2]} 安全{Raw[3]} 成长{Raw[4]} 先手{Raw[5]} 供给{Raw[6]} 眼位{Raw[7]} 威胁{Raw[8]} = {Total}";
}
