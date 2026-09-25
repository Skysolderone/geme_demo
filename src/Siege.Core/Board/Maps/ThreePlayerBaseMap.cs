namespace Siege.Core.Board.Maps;

/// <summary>
/// 3 人标准档基准地图 <c>siege-3p-base-v1</c>（small-maps D1）：11×11、沿竖直中轴（F 列）左右镜像对称。
/// 出生区 1 跨中轴位于上方，出生区 2 / 3 在左下 / 右下互为镜像，都在 h=2 高台；中央 h=0 岛四周一格宽深水，三座桥各对一个出生区。
/// </summary>
/// <remarks>
/// <para><b>权威数据是设计师手工编写的 <c>maps/siege-3p-base-v1.json</c></b>，做法同 <see cref="TwoPlayerBaseMap"/>：
/// 编译时作为嵌入资源放进本程序集（<c>Siege.Core.csproj</c>），三个入口不依赖工作目录；本类只做"资源 → <see cref="MapData"/>"，不另写一份地图。
/// 改图即改 JSON，任何内容变化都要递增标识（boundaries.md「内置图内容一变，标识必须递增」）。</para>
/// <para>镜像对称、规模、三区距离公平由 <c>三人基准地图Tests</c> 守门；静态校验与人数预算在建局时（<see cref="GameBoard.Load"/>）照常执行。</para>
/// <para>规格：openspec/changes/small-maps/specs/map-definition —— Requirement: 3 人基准地图</para>
/// </remarks>
public static class ThreePlayerBaseMap
{
    /// <summary>地图标识。</summary>
    public const string Id = "siege-3p-base-v1";

    /// <summary>嵌入资源名（与 <c>Siege.Core.csproj</c> 的 <c>LogicalName</c> 一致）。</summary>
    internal const string ResourceName = "Siege.Core.Maps." + Id + ".json";

    private static readonly Lazy<string> Json = new(ReadResource);

    /// <summary>构建 3 人基准地图（每次返回新对象）。</summary>
    public static MapData Create()
    {
        MapData map = MapFile.FromJson(Json.Value);
        return map.Id == Id
            ? map
            : throw new InvalidOperationException($"嵌入资源 {ResourceName} 里的地图标识是 {map.Id}，应为 {Id}。");
    }

    private static string ReadResource()
    {
        using Stream stream = typeof(ThreePlayerBaseMap).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"程序集里没有嵌入资源 {ResourceName}：maps/{Id}.json 没有登记进 Siege.Core.csproj。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
