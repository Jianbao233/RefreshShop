# RefreshShop

《杀戮尖塔 2》商店刷新 Mod。

## 目标

在商店界面增加一个免费刷新入口，用于刷新本地玩家自己的商店卡牌与遗物选项。

- 刷新次数无限。
- 刷新不消耗金币。
- 刷新入口复用游戏商店中“删除卡牌服务”的金币图标/价格视觉区域。
- 联机时只影响安装者自己的商店库存，不修改其他玩家商店。
- 不处理先古之民事件选项。

## 当前状态

当前仓库已完成项目骨架和设计文档，后续按 `docs/DESIGN.md` 实现补丁逻辑。

## 构建

```powershell
cd "K:\杀戮尖塔mod制作\STS2_mod\RefreshShop"
.\build.ps1
```

构建脚本会复制到：

- `K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\RefreshShop`
- 如果 Steam 目录不存在，则使用 `%APPDATA%\SlayTheSpire2\mods\RefreshShop`

## 开源协议

MIT