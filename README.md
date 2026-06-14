# RefreshShop

《杀戮尖塔 2》商店刷新 Mod。

## 功能

在商店删卡服务右下角增加一个半尺寸免费刷新按钮。点击后**重建本地玩家整个商店**，刷新所有卡牌、遗物、药水和删卡服务。

- 刷新次数无限，不消耗金币
- 刷新入口复用商店删卡服务的金币图标视觉，显示价格 0
- 联机时只影响安装者自己的商店库存，不发送网络消息，不修改其他玩家商店
- 遗物池通过游戏自带 `RefreshRarity` 自动补充，不会耗尽

## 安装

1. 下载 `RefreshShop_v*.zip`
2. 解压到 `Slay the Spire 2\mods\RefreshShop\`
3. 文件夹内应有 `RefreshShop.dll` 和 `mod_manifest.json`

## 构建

```powershell
cd RefreshShop
.\build.ps1
```

构建脚本自动复制到游戏 mods 目录和 `torelease\`。

## 开源协议

MIT