# RefreshShop

《杀戮尖塔 2》商店刷新 Mod。

当前版本：**v0.2.3**。

### v0.2.3

- 修复通过 RitsuLib 修改设置后，重启游戏恢复默认值的问题；设置变更立即保存。
- 修复旧 ModConfig 配置迁移不完整的问题，刷新次数、价格、补货开关一起迁移。
- 已保存的配置优先于旧值；仍可在 RitsuLib 或 ModConfig 设置页修改。
- 保存使用临时文件替换，写入失败时保留原配置。

## 功能

在商店删卡服务右下角增加一个刷新按钮。点击后**重建本地玩家整个商店**，刷新所有卡牌、遗物、药水和删卡服务。

### v0.2.0 新增

- **可配置刷新次数**：通过 ModConfig 设置每次进入商店的可用刷新次数（0 = 无限）
- **可配置刷新价格**：每次刷新消耗的金币（0 = 免费）
- **补货开关**：开启时刷新会恢复已购买的空槽位，关闭时只重抽未购买的槽位
- 按钮显示剩余次数/上限 + 价格（复用删卡服务原生金币图标样式）
- 次数耗尽时按钮变灰不可点击
- 金币不足时点击无效 + 播放提示音
- 进入新商店节点时次数自动重置
- 扣金币走游戏原生 `PlayerCmd.LoseGold` + 联机同步路径

### 基础特性

- 联机时只影响安装者自己的商店库存，扣金币走联机同步，不修改其他玩家商店
- 遗物池通过游戏自带 `RefreshRarity` 自动补充，不会耗尽

## 安装

1. 下载 `RefreshShop_v*.zip`
2. 解压到 `Slay the Spire 2\mods\RefreshShop\`
3. 文件夹内应有 `RefreshShop.dll` 和 `mod_manifest.json`
4. 推荐安装 RitsuLib（或 ModConfig）以在设置页调整配置

## 配置存储

三项设置（刷新次数 / 刷新价格 / 补货开关）由本 mod 自己落盘，保存在：

```text
%APPDATA%\SlayTheSpire2\mods_settings\RefreshShop.json
```

- 修改后自动保存，重启游戏不会回落到默认值。
- ModConfig 注册并载入旧文件后，自动迁移尚未保存的配置项；已保存的项保持不变。
- 之后不再轮询 ModConfig 的旧值，但用户在其设置页的主动修改仍会保存。

## 构建

```powershell
cd RefreshShop
.\build.ps1
```

构建脚本默认使用 Release，自动复制到游戏 mods 目录和 `torelease\`。

仅编译（不部署）：`dotnet build -c Release`。

配置回归测试：`powershell -ExecutionPolicy Bypass -File .\test-config.ps1`。测试在系统临时目录使用独立配置，覆盖跨进程恢复、旧配置迁移、设置回调和写入失败，不修改玩家配置。

## 开源协议

MIT
