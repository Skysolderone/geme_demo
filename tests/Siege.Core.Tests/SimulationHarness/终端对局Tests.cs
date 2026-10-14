using Siege.Core.Board.Maps;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Sim.Play;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>终端对局（play 子命令）的冒烟测试：脚本输入能从插旗走到终局，不崩溃、不卡住。</summary>
public class 终端对局Tests
{
    [Fact]
    public void 脚本输入能落子并走到输入耗尽()
    {
        // 选 1 号区；第一回合选 1 号候选，在出生区落 B1（v3 的 A1 是角石），预演后确认；之后 Pass 一个小回合，输入耗尽退出。
        // retire-legacy-maps 1.8：地图由 v5 改为 link:1（内置固定配对脚本），落点 B1 → V3（1 号出生棋盘左下角；link-map-terrain 起 link:1 的 1 号出生棋盘左下角为 X3）；冒险概率随旧地图删除、不再写死。种子 42 在 link:1 上第 2 大回合即被同棋盘 AI 提光出局、
        // 对局自动跑过链接后终端播报越界崩溃（migration.md 待决 L15），探针（种子 41–52）改取种子 45。断言不变（落点文字与种子随之改）。
        // 段 C：大回合上限删除，脚本不再能"Pass 到上限终局"（AI 不会停手），原"走到终局"改为走到第 3 大回合后输入耗尽干净退出。
        // life-shape 段 B：原脚本 Pass 两次、在第 4 大回合的提示处耗尽；活棋禁入 / 破坏活形生效后 AI 第 4 大回合走法变了（与改动前逐行比对：前 3 个大回合相同），
        // 玩家2 在轮到你之前提走了你唯一的 B1，你出局、对局自动跑到终局，永远走不到"输入耗尽"。改为只 Pass 一次、在第 3 大回合的提示处耗尽——
        // 保护期内别家不能进你的出生区，这一步不再依赖 AI 的走法；断言未改。
        var script = new System.Text.StringBuilder("1\n1\nX3 B\nv\nok\n");
        for (int i = 0; i < 1; i++)
        {
            script.Append("\npass\n");
        }

        var output = new StringWriter();
        int exit = PlayCommand.Run(45, 4, 1, AiDifficulty.Easy, new StringReader(script.ToString()), output, contentSet: ContentSet.V1, map: MapCatalog.Resolve("link:1"), pairingSource: new ScriptedPairingSource());   // more-pieces-relics：写死内容集 v1
        string text = output.ToString();

        Assert.Equal(0, exit);
        Assert.Contains("玩家1(你) 落子 X3B", text, StringComparison.Ordinal);
        Assert.Contains("第 3 大回合", text, StringComparison.Ordinal);
        Assert.Contains("已退出。种子 45", text, StringComparison.Ordinal);

        // restore-go-core-rules 段 E（tasks 5.3）：势力栏显示"领地 + 棋串"，且两项之和等于总势力（测试内独立加和）；样本里确有非零领地分。
        // 变异验证 M-E16：BoardRenderer.Status 只打总势力（去掉领地 / 棋串拆分）→ 实跑红 1，只红 状态栏大数势力缩写不撑破一行——
        // 本测试这条正则也会命中每步播报（PlayCommand 同用 PowerText），所以状态栏本身由下一条测试钉住。
        System.Text.RegularExpressions.MatchCollection rows = System.Text.RegularExpressions.Regex.Matches(text, "势力 (\\d+)（领地 (\\d+) \\+ 棋串 (\\d+)）");
        Assert.True(rows.Count >= 8, $"只找到 {rows.Count} 条势力栏");
        Assert.All(rows, m => Assert.Equal(long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value) + long.Parse(m.Groups[3].Value)));
        Assert.Contains(rows, m => m.Groups[2].Value != "0");
    }

    [Fact]
    public void 状态栏大数势力缩写不撑破一行()
    {
        // tasks 5.3「大数显示不换行溢出」：120 枚倍增子的一条棋串军势 ⌊120 × 3^120 / 2^120⌋ 有 23 位；势力栏用 ≥ 10^6 的缩写（与图形版同一份 PowerNotation），
        // 整行不超过 100 列、不出现完整的 23 位数字。精确值由终局名次 / 预演给出，不在概览栏。
        MapData map = MatchFixtures.Map() with { Id = "test-match-12x12", Width = 12, Height = 12 };
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All, MatchFixtures.Relics(map), MatchOptions.Immediate);
        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                match.Board.Place(new Coord(x, y), MatchFixtures.P0, PieceType.Multiplier);
            }
        }

        match.Debug.Recalculate();
        Scoring.PlayerPower p0 = match.Scoreboard.Latest!.Of(MatchFixtures.P0);
        var output = new StringWriter();
        new BoardRenderer(output).Status(match.Publish(), MatchFixtures.P0);

        string line = output.ToString().Split('\n').Single(l => l.Contains("玩家1(你)", StringComparison.Ordinal) && l.Contains("势力 ", StringComparison.Ordinal));
        Assert.Contains($"势力 {Scoring.PowerNotation.Compact(p0.Total)}（领地 12 + 棋串 {Scoring.PowerNotation.Compact(p0.GroupScore)}）", line, StringComparison.Ordinal);
        Assert.DoesNotContain(p0.GroupScore.ToString(System.Globalization.CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
        Assert.True(line.TrimEnd().Length <= 100, $"势力栏 {line.TrimEnd().Length} 列");
    }

    [Fact]
    public void 输入结束时干净退出()
    {
        var output = new StringWriter();
        // retire-legacy-maps 1.8：地图由 v5 改为 link:1。开场说明的"怎样结束"一行随模式改为链接模式的战场规则说明（Legacy 那句只在旧地图局打印）。
        int exit = PlayCommand.Run(7, 4, 2, AiDifficulty.Easy, new StringReader("2\n"), output, map: MapCatalog.Resolve("link:1"), pairingSource: new ScriptedPairingSource());

        Assert.Equal(0, exit);
        Assert.Contains("已退出。种子 7", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("碾压", output.ToString(), StringComparison.Ordinal);   // 段 C：势力碾压已删除，开场说明不再提
        Assert.Contains("两对都分出胜负后胜者链接打决赛", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void 终端链接局跨过拼接后小回合播报不崩溃且不误报()
    {
        // retire-legacy-maps 4b.1（L15）：本机在第 1 大回合于 3 号出生棋盘落 1 子、此后一直 Pass；种子 2 下本机独占 3 号棋盘，
        // 首轮配对 2 号与 3 号战场，3 号战场（连同本机的子）平移贴到 2 号东侧——这次拼接发生在本机第 3 大回合的 Pass 小回合末尾。
        // 修复前 Summarize 拿拼接后画布的坐标去索引拼接前的盘面：画布变大即 ArgumentOutOfRangeException（原探针 135 / 200 崩溃）；
        // 未越界时会把平移的那枚子误报成"落子"并"提走 玩家1(你) 1 子"。修复后拼接前的格经 LinkLayout 平移再比对：本机这一手照实报 Pass，子还在。
        // 变异记录见 implement.md「4b」（Summarize 不传平移 → 本测试红）。
        MapData map = MapCatalog.Resolve("link:1");
        string origin = map.Boards[2].Origin.ToNotation();
        string script = "3\n1\n" + origin + " B\nv\nok\n" + string.Concat(Enumerable.Repeat("\npass\n", 8));
        var output = new StringWriter();

        int exit = PlayCommand.Run(2, 4, 1, AiDifficulty.Standard, new StringReader(script), output, map: map, pairingSource: new ScriptedPairingSource());

        string text = output.ToString();
        string[] lines = text.Split('\n');
        Assert.Equal(0, exit);
        Assert.Contains("你锁定了 3 号棋盘；这块棋盘上只有你。", text, StringComparison.Ordinal);
        Assert.Contains($"玩家1(你) 落子 {origin}B", text, StringComparison.Ordinal);
        int pairing = Array.FindIndex(lines, l => l.Contains("── 首轮配对：2 号与 3 号战场 ──", StringComparison.Ordinal));
        Assert.True(pairing > 0, "没有走到首轮配对。");
        Assert.Contains("3 号战场平移贴到 2 号战场东侧", lines[pairing + 1], StringComparison.Ordinal);

        // 拼接发生在本机这一手的末尾：这一手的播报照实是 Pass，没有平移带来的假落子 / 假提子，本机那枚子仍在（棋串 1）。
        string linkTurn = lines[pairing - 1];
        Assert.StartsWith("玩家1(你) Pass（", linkTurn, StringComparison.Ordinal);
        Assert.Contains("棋串 1）", linkTurn, StringComparison.Ordinal);
        Assert.DoesNotContain("提走", linkTurn, StringComparison.Ordinal);

        // 拼接之后对局继续逐手播报（同战场对手的落子坐标落在拼接后的画布上），退出码 0。
        Assert.Contains("第 4 大回合", text, StringComparison.Ordinal);
        Assert.Contains(lines[(pairing + 1)..], l => l.StartsWith("玩家", StringComparison.Ordinal) && l.Contains(" 落子 ", StringComparison.Ordinal));
    }
}
