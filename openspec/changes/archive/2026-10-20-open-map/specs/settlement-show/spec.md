## REMOVED Requirements

### Requirement: 棋盘展开节拍

**Reason**: 该节拍是链接（拼接）时对方棋盘滑入贴合的演出；open-map 取消链接后不再有触发时机。锁定后 5 块棋盘随 `visual-style-baseline`「未展开区域的可视表现」直接整图显示，与现行"锁定即重搭显示自己棋盘"同一路径，不另加展开演出（可玩优先；若日后要演出另开 change）。

**Migration**: 滑入 / 相机重定界 / 显现 / "棋盘拼接"横幅四个节拍与其测试删除。

### Requirement: 战场结算横幅

**Reason**: 唯一战场的结算即终局，沿用既有「横幅节拍」的终局横幅与出局横幅；不存在"他场"。

**Migration**: "战场胜出 / 战场落败"横幅与"战场 X 已结算"提示条删除。

### Requirement: 决赛倒计时提示

**Reason**: 决赛倒计时随 open-map 删除。

**Migration**: 无。
