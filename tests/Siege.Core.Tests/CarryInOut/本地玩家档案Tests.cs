using Siege.Core.Ai;
using Siege.Core.Carry;
using Siege.Sim;
using Siege.Sim.Cli;
using Siege.Sim.Play;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: 本地玩家档案</summary>
/// <remarks>全部读写落在 <see cref="TempProfile"/> 的临时目录（tasks 2.1），MUST NOT 读写真实用户目录。</remarks>
public class 本地玩家档案Tests
{
    [Fact]
    public void 档案内容()
    {
        // 规格 Scenario：本机玩家有 17 点补给点、备用子 1 件、换型令 2 件，且没有进行中的对局
        // → 档案为 {"version":1,"points":17,"inventory":{"SpareStone":1,"DraftLot":0,"Commission":2},"inFlight":null}。
        // 经存取的真实写入路径得到这份档案：20 点、换型令 2 件起步，兑换 1 件备用子（价 3）→ 17 点、备用子 1 件。
        const string expected = "{\"version\":1,\"points\":17,\"inventory\":{\"SpareStone\":1,\"DraftLot\":0,\"Commission\":2},\"inFlight\":null}";
        using var temp = new TempProfile();
        temp.Write(TempProfile.Json(points: 20, spare: 0, draft: 0, commission: 2));
        CarryProfileStore store = temp.Store();
        Assert.Equal(CarryProfileLoadStatus.Loaded, store.Load().Status);

        Assert.True(store.TryExchange(SupplyKind.SpareStone, out string? refusal), refusal);

        Assert.Equal(expected, temp.Read());
        Assert.Equal(expected, store.Current.ToJson());

        // 读回：逐项与写出一致；带缩进与 CRLF 的同一份档案按同样内容读取（determinism.md「持久化文本必须容忍行尾差异」）。
        CarryProfile parsed = CarryProfile.Parse(expected);
        Assert.Equal((1, 17, 1, 0, 2, (CarryInFlight?)null),
            (parsed.Version, parsed.Points, parsed.StockOf(SupplyKind.SpareStone), parsed.StockOf(SupplyKind.DraftLot), parsed.StockOf(SupplyKind.Commission), parsed.InFlight));
        string pretty = "{\r\n  \"version\": 1,\r\n  \"points\": 17,\r\n  \"inventory\": { \"SpareStone\": 1, \"DraftLot\": 0, \"Commission\": 2 },\r\n  \"inFlight\": null\r\n}\r\n";
        Assert.Equal(expected, CarryProfile.Parse(pretty).ToJson());
    }

    [Fact]
    public void 在途记录的写出与读回()
    {
        // 规格正文：在途记录含对局标识、带入的补给及其指定或抽得的类型；未带入时补给为空。两种形状都要往返。
        var withCarry = new CarryProfile { Points = 3, InFlight = new CarryInFlight("m-1", new CarryIn(SupplyKind.Commission, Board.PieceType.Fortress)) };
        Assert.Equal(
            "{\"version\":1,\"points\":3,\"inventory\":{\"SpareStone\":0,\"DraftLot\":0,\"Commission\":0},\"inFlight\":{\"matchId\":\"m-1\",\"supply\":\"Commission\",\"type\":\"Fortress\"}}",
            withCarry.ToJson());
        Assert.Equal(withCarry.ToJson(), CarryProfile.Parse(withCarry.ToJson()).ToJson());
        Assert.Equal(new CarryInFlight("m-1", new CarryIn(SupplyKind.Commission, Board.PieceType.Fortress)), CarryProfile.Parse(withCarry.ToJson()).InFlight);

        var without = new CarryProfile { InFlight = new CarryInFlight("m-2", null) };
        Assert.Contains("\"inFlight\":{\"matchId\":\"m-2\",\"supply\":null,\"type\":null}", without.ToJson(), StringComparison.Ordinal);
        Assert.Equal(new CarryInFlight("m-2", null), CarryProfile.Parse(without.ToJson()).InFlight);
    }

    [Fact]
    public void 路径可配置()
    {
        // 规格 Scenario：以 --profile <路径> 启动终端对局 → 系统读写该路径，缺省路径下的档案不被触碰。
        // 入口的选项解析（Program.PlayProfile）给出指向该路径的存取；用它跑一局脚本化终端对局（不带入、在选区处输入耗尽退出）。
        using var temp = new TempProfile();
        string before = RealProfileDir.State();

        CarryProfileStore store = Program.PlayProfile(new CommandLine(["--profile", temp.FilePath]))!;
        Assert.Equal(temp.FilePath, store.FilePath);
        Assert.False(File.Exists(temp.FilePath));

        var output = new StringWriter();
        int exit = PlayCommand.Run(7, 4, 1, AiDifficulty.Easy, new StringReader("\n"), output, flagRisk: 0, contentSet: Board.ContentSet.V1, profile: store);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(temp.FilePath), "档案应写到 --profile 指定的路径");
        Assert.Contains(temp.FilePath, output.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, RealProfileDir.State());

        // 不给 --profile：路径解析为用户数据目录下的 Siege/profile.json（构造不做任何 I/O）。
        string expectedDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Siege", "profile.json");
        Assert.Equal(expectedDefault, CarryProfileStore.DefaultPath());
        Assert.Equal(expectedDefault, Program.PlayProfile(new CommandLine([]))!.FilePath);
        Assert.Equal(before, RealProfileDir.State());
    }

    [Fact]
    public void 原子写入()
    {
        // 规格 Scenario：写入档案时进程在替换原文件之前被终止 → 原档案内容保持写入前的状态，可正常读取。
        // 用注入的写入失败桩模拟：临时文件写完、替换之前抛出（tasks 2.2）。
        using var temp = new TempProfile();
        string original = TempProfile.Json(points: 17, spare: 1, draft: 0, commission: 2);
        temp.Write(original);
        var store = new CarryProfileStore(temp.FilePath, TempProfile.Clock, beforeReplace: _ => throw new IOException("桩：替换前进程终止"));
        store.Load();

        Assert.Throws<IOException>(() => store.TryExchange(SupplyKind.DraftLot, out _));

        Assert.Equal(original, temp.Read());
        CarryProfileStore reread = temp.Store();
        Assert.Equal(CarryProfileLoadStatus.Loaded, reread.Load().Status);
        Assert.Equal(17, reread.Current.Points);
        Assert.Equal(0, reread.Current.StockOf(SupplyKind.DraftLot));
        // 失败的写入不改内存中的档案：内存与磁盘一致。
        Assert.Equal(17, store.Current.Points);
    }
}
