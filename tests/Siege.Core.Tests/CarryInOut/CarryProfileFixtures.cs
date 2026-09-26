using Siege.Core.Carry;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>
/// 档案测试的临时目录（carry-in-out tasks 2.1：档案测试一律用临时目录，MUST NOT 读写真实用户目录）。
/// 每个实例一个独立子目录，释放时整体删除。
/// </summary>
internal sealed class TempProfile : IDisposable
{
    internal TempProfile()
    {
        Directory = Path.Combine(Path.GetTempPath(), "siege-carry-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        FilePath = Path.Combine(Directory, "profile.json");
    }

    internal string Directory { get; }

    internal string FilePath { get; }

    /// <summary>规格 Scenario 用的固定时钟：2026-09-26 11:40:00。</summary>
    internal static readonly Func<DateTimeOffset> Clock = () => new DateTimeOffset(2026, 9, 26, 11, 40, 0, TimeSpan.FromHours(8));

    internal CarryProfileStore Store() => new(FilePath, Clock);

    /// <summary>直接写档案文件（测试构造初始状态，不经被测代码）。</summary>
    internal void Write(string text) => File.WriteAllText(FilePath, text);

    internal string Read() => File.ReadAllText(FilePath);

    /// <summary>目录下的全部文件名（排序）。</summary>
    internal string[] Files() => [.. System.IO.Directory.GetFiles(Directory).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];

    /// <summary>按规格格式手写一份档案 JSON（测试内独立写法，不调用被测的写出）。</summary>
    internal static string Json(int points, int spare, int draft, int commission, string inFlight = "null", int version = 1) =>
        $"{{\"version\":{version},\"points\":{points},\"inventory\":{{\"SpareStone\":{spare},\"DraftLot\":{draft},\"Commission\":{commission}}},\"inFlight\":{inFlight}}}";

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>真实用户目录的只读快照：用来断言某段代码没有碰缺省档案路径所在目录。</summary>
internal static class RealProfileDir
{
    /// <summary>缺省档案所在目录（<c>%APPDATA%\Siege</c>）的状态：不存在记为 "absent"，存在则列出文件名、长度与修改时间。</summary>
    internal static string State()
    {
        string dir = Path.GetDirectoryName(CarryProfileStore.DefaultPath())!;
        return System.IO.Directory.Exists(dir)
            ? string.Join("|", System.IO.Directory.GetFiles(dir).Order(StringComparer.Ordinal)
                .Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f).Ticks}"))
            : "absent";
    }
}
