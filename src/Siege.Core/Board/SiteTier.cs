namespace Siege.Core.Board;

/// <summary>
/// 据点档位。位置与档位是地图静态数据；分值是对局配置，不在此处。
/// 枚举值从 1 起编：<c>default(SiteTier)</c> 不是任何合法档位，校验器据此判定"档位必填"。
/// </summary>
/// <remarks>规格：openspec/changes/scoring-sites/specs/site-control —— Requirement: 据点档位与分值</remarks>
public enum SiteTier
{
    /// <summary>营帐（档位 1）：出生区内的 h=2 格。</summary>
    Tent = 1,

    /// <summary>篝火（档位 2）：出生区河外低地、紧贴邻家高台崖边。</summary>
    Campfire = 2,

    /// <summary>石碑（档位 3）：岛上非林地格。</summary>
    Stele = 3,
}
