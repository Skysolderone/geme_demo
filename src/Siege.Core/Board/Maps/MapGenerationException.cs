namespace Siege.Core.Board.Maps;

/// <summary>
/// 尝试次数耗尽仍未得到通过校验的地图。消息带最后一次尝试的失败原因。
/// retire-legacy-maps：原定义在已删除的边疆档生成器文件（FrontierMapGenerator.cs）里；链接档生成器 <see cref="LinkMapGenerator"/> 复用它，按裁决 L10 保留并原样迁出到本文件。
/// </summary>
public sealed class MapGenerationException : Exception
{
    public MapGenerationException(string message)
        : base(message)
    {
    }
}
