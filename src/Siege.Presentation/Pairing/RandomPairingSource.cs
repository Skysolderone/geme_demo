using System.Security.Cryptography;
using Siege.Core.Match;

namespace Siege.Presentation.Pairing;

/// <summary>
/// 图形版有人值守对局的配对来源（board-link-mode D3 / 实现裁决 D3；规格 simulation-harness「各入口支持生成图」：有人值守对局 MUST 使用真随机源、不提供配对种子选项）：
/// 取操作系统的密码学随机数，不可复现；结果作为配对事件记录（<see cref="PairingDecision.Source"/> = <see cref="PairingSourceNames.Random"/>）。
/// 抽取写法共用 Core 的 <see cref="PairingDraw"/>，本类只提供随机数；不参与任何对局子流。
/// 无人值守路径（<c>--auto-demo</c> / <c>--pick-check</c> / <c>--screenshot=</c>）不用它，用 Core 的 <see cref="ScriptedPairingSource"/>（内置固定脚本）。
/// </summary>
/// <remarks>本程序集零 Godot 依赖、不引用 Sim；这是表现层里唯一一处不受控随机，只用于配对，不进规则内核。</remarks>
public sealed class RandomPairingSource : IPairingSource
{
    public PairingDecision Decide(PairingRequest request) => PairingDraw.Draw(request, RandomNumberGenerator.GetInt32, PairingSourceNames.Random);
}
