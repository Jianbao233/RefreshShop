# RefreshShop

《杀戮尖塔 2》商店刷新 Mod。当前版本：**0.3.1**。[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747499124)

0.3.1 修复了某个遗物槽没有合法候选时点击刷新无反应的问题。现在会保留该槽并刷新其余商品，也恢复了已注册共享池中符合条件的 Mod 遗物。

在删卡服务下方增加原生按钮，刷新本地玩家的卡牌、遗物、药水商品。删卡服务保持原样。

## 使用

将 `RefreshShop.dll` 和 `mod_manifest.json` 放在游戏的 `mods/RefreshShop/` 目录。设置页可选用 RitsuLib 或 ModConfig；刷新功能本身不依赖它们。

| 设置 | 默认值 | 范围 |
| --- | --- | --- |
| 每个商店的刷新次数 | 5 | 0–999；0 表示无限 |
| 每次刷新价格 | 25 金币 | 0–999；0 表示免费 |
| 补回已购买的空槽 | 关闭 | 开启时刷新也补货 |

按钮使用游戏 `NPauseMenuButton`，保留原生声音、动画和鼠标／手柄输入，并显示次数和费用。重开同一份商店库存不会重置次数；次数属于本次运行中的库存实例，不写入存档。修改次数上限会重置当前商店的剩余次数。

三项设置立即保存到 `%APPDATA%\SlayTheSpire2\mods_settings\RefreshShop.json`。重启会恢复设置。迁移旧 ModConfig 配置时，只有本 Mod 尚未保存的键会导入；已保存的值优先。保存失败会记录日志，保留原配置文件。

## 联机单端安装

主机或客机可以只在自己这端安装。刷新只修改安装者自己的库存，扣费与购买沿用游戏原生金币／奖励同步，不添加网络消息或额外模型 ID。刷新使用独立随机状态，不推进原生随机流。

本地双进程原生 ENet 测试已覆盖主机单装与客机单装，另一端完全不加载 RefreshShop；握手、刷新扣费、卡牌／药水／遗物购买及双方随机状态核对通过。**这不等于 Steam 远程双机或完整战局验收**。两端仍需有一致的游戏版本及其他会影响玩法、模型 ID 的 Mod，不能靠本 Mod 同步对方不存在的自定义内容。

若原生发送抛出异常，刷新可能已被对方收到，因此本商店会停止继续刷新，避免再次扣费；日志和按钮提示会要求重新连接。外部 Mod 的任意钩子副作用不在完整回滚保证之内。

## 0.3.0 重构

- 分开配置存储、旧配置适配、原生 API 访问、商品生成和界面职责。
- 先在临时条目中生成，再扣费提交；生成、异步扣费失败时恢复库存、费用和次数。
- 保留角色／解锁／可售限制及第三个商店专属遗物槽；兼容可选 MerchantBlacklist 过滤。
- 去掉重复 Harmony 安装、旧槽位金币补丁和每帧 UI 轮询；界面销毁时解除事件订阅。
- 版本只在 `mod_manifest.json` 定义；DLL、staging 和本地归档从它生成。

## 构建与验证

需要 .NET SDK、Godot 4.5.1 Mono；原生回归测试还需要本地合法安装的游戏 DLL/PCK。不随源码分发游戏文件。

```powershell
# 默认只编译，不部署、不上传
.\build.ps1

# 更新 staging；退出游戏后才可部署
.\build.ps1 -Stage
.\build.ps1 -Deploy -GameDir 'F:\Steam\steamapps\common\Slay the Spire 2'

# 在 release/ 保存本地 ZIP；同名历史包不覆盖
.\build.ps1 -Archive

# 配置的跨进程恢复、迁移和失败保护
.\test-config.ps1

# 游戏原生逻辑和 UI 场景，始终无头、静音
.\test.ps1

# 两个独立无头进程，原生 ENet 真实传输；仅一端加载 Mod
.\test-network.ps1
```

测试脚本的 `-GameDir`、`-GodotExe` 可指定游戏和 Mono Godot 路径。旧版 SDK 可用 `test.ps1 -GameDataDir <0.107.1 SDK目录> -LegacyApi` 验证接口；这仍使用已安装游戏的 PCK，不代表旧客户端完整视觉验收。双进程 ENet 测试针对当前 0.111.0 API。

部署会备份旧文件、核对哈希；`torelease/` 和 `release/` 不进入 Git。测试使用独立临时配置和测试用户目录，不操作玩家正式存档。`SLAY_THE_SPIRE2_APPDATA` 是本 Mod 的配置测试入口，不是游戏存档目录开关。

技术说明：[docs/DESIGN.md](docs/DESIGN.md)。验证范围与构建指纹：[docs/VALIDATION.md](docs/VALIDATION.md)。

## 协议

MIT
