namespace Siege.Core.Board.Maps;

/// <summary>
/// 地图生成尝试次数耗尽仍未得到通过校验的地图。消息带最后一次尝试的失败原因。
/// 由棋盘档生成器（<see cref="BoardMapGenerator"/>）抛出；三个入口按"地图错误"统一报错退出。
/// （retire-legacy-maps 段 B 由已删除的边疆档生成器文件迁出，类型与命名空间不变。）
/// </summary>
public sealed class MapGenerationException : Exception
{
    public MapGenerationException(string message)
        : base(message)
    {
    }
}
