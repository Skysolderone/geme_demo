namespace Siege.Core.Tests;

/// <summary>
/// 测试用的仓库路径（retire-legacy-maps：<see cref="RepoRoot"/> 原在随旧地图删除的 <c>FrontierFixtures</c> 里，原样迁出）。
/// </summary>
internal static class TestPaths
{
    /// <summary>仓库根目录（含 <c>siege.sln</c>）。</summary>
    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根目录（siege.sln）。");
    }
}
