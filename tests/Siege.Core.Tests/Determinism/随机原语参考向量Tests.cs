using Siege.Core.Determinism;

namespace Siege.Core.Tests.Determinism;

/// <summary>
/// 规范：.trellis/spec/core/determinism.md ——「可复现」的地基。用公开参考向量钉死两个随机原语的位级实现：
/// 任何常量（移位量、乘数、旋转量）写错都会让同种子的整局生成结果静默改变，而统计测试对此完全不敏感。
/// </summary>
public class 随机原语参考向量Tests
{
    [Fact]
    public void SplitMix64_种子0的前五个输出()
    {
        // 参考向量：SplitMix64 (Steele/Lea/Flood 2014) seed = 0，与 Vigna 参考实现一致；trellis-check 2026-09-13 另用 Python 独立实现复算过。
        // 变异验证 K-1：Mix 的第二个乘数改为 0x94D049BB133111EA → 红 1（本测试）。
        ulong state = 0;
        ulong[] expected =
        [
            0xE220A8397B1DCDAFUL,
            0x6E789E6AA1B965F4UL,
            0x06C45D188009454FUL,
            0xF88BB8A8724C81ECUL,
            0x1B39896A51A8749BUL,
        ];
        foreach (ulong e in expected)
        {
            Assert.Equal(e, SplitMix64.Next(ref state));
        }
    }

    [Fact]
    public void Xoshiro256StarStar_状态1234的前十个输出()
    {
        // 参考向量：xoshiro256** (Blackman/Vigna) 初始状态 {1, 2, 3, 4}，与 rand_xoshiro 的 reference 测试向量一致。
        // 变异验证 K-2：NextUInt64 的 RotateLeft(_s3, 45) 改为 44 → 红 1（本测试，第 2 个输出起不同）；
        // K-3：RotateLeft(_s1 * 5, 7) * 9 改为 * 7 → 红 1（本测试，第 1 个输出即不同）。
        var stream = new RandomStream("reference", 1, 2, 3, 4);
        ulong[] expected =
        [
            11520UL,
            0UL,
            1509978240UL,
            1215971899390074240UL,
            1216172134540287360UL,
            607988272756665600UL,
            16172922978634559625UL,
            8476171486693032832UL,
            10595114339597558777UL,
            2904607092377533576UL,
        ];
        foreach (ulong e in expected)
        {
            Assert.Equal(e, stream.NextUInt64());
        }

        Assert.Equal(10, stream.Consumed);
    }

    [Fact]
    public void 等概率整数用拒绝采样消除取模偏差()
    {
        // NextInt 的接受上界 = 2^64 − (2^64 mod range)，被拒绝的恰是最后一个不完整周期，取模后各余数严格等概率。
        // range = 100：2^64 mod 100 = 16 → 上界 2^64 − 16 = 0xFFFF_FFFF_FFFF_FFF0；range = 2^30：上界 2^64 − 2^30；range = 1：不拒绝。
        // 不用统计守这一条——不拒绝时的偏差只有 range / 2^64（权重和 ≤ 100 量级下约 5e-18），任何样本量都测不出，只能结构性钉住。
        // 变异验证 K-4：RejectionBound 改为恒返回 ulong.MaxValue（不拒绝）→ 红 1（本测试）。
        Assert.Equal(0xFFFF_FFFF_FFFF_FFF0UL, RandomStream.RejectionBound(100));
        Assert.Equal(ulong.MaxValue - (1UL << 30) + 1, RandomStream.RejectionBound(1 << 30));
        Assert.Equal(ulong.MaxValue, RandomStream.RejectionBound(1));
    }
}
