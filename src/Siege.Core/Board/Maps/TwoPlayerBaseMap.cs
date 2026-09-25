namespace Siege.Core.Board.Maps;

/// <summary>
/// 2 人标准档基准地图 <c>siege-2p-base-v1</c>（small-maps D1）：9×9、C2（绕中心 180°）对称，两个出生区在左下 / 右上角的 h=2 高台，
/// 中央 h=0 岛经两座桥进出，四周一格宽深水。
/// </summary>
/// <remarks>
/// <para><b>权威数据是设计师手工编写的 <c>maps/siege-2p-base-v1.json</c></b>，编译时作为嵌入资源放进本程序集（<c>Siege.Core.csproj</c>），
/// 所以三个入口（批量、终端、图形版）不依赖工作目录就能拿到它；本类只做"资源 → <see cref="MapData"/>"，不另写一份地图。
/// 改图即改 JSON，任何内容变化都要递增标识（boundaries.md「内置图内容一变，标识必须递增」）。</para>
/// <para>对称、规模、地形要素由 <c>两人基准地图Tests</c> 守门；静态校验与人数预算在建局时（<see cref="GameBoard.Load"/>）照常执行。</para>
/// <para>规格：openspec/changes/small-maps/specs/map-definition —— Requirement: 2 人基准地图</para>
/// </remarks>
public static class TwoPlayerBaseMap
{
    /// <summary>地图标识。</summary>
    public const string Id = "siege-2p-base-v1";

    /// <summary>嵌入资源名（与 <c>Siege.Core.csproj</c> 的 <c>LogicalName</c> 一致）。</summary>
    internal const string ResourceName = "Siege.Core.Maps." + Id + ".json";

    private static readonly Lazy<string> Json = new(ReadResource);

    /// <summary>构建 2 人基准地图（每次返回新对象）。</summary>
    public static MapData Create()
    {
        MapData map = MapFile.FromJson(Json.Value);
        return map.Id == Id
            ? map
            : throw new InvalidOperationException($"嵌入资源 {ResourceName} 里的地图标识是 {map.Id}，应为 {Id}。");
    }

    private static string ReadResource()
    {
        using Stream stream = typeof(TwoPlayerBaseMap).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"程序集里没有嵌入资源 {ResourceName}：maps/{Id}.json 没有登记进 Siege.Core.csproj。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
