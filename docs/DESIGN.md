# RefreshShop 设计文档

## 1. 功能定位

`RefreshShop` 只做一件事：

> 在商店界面增加免费、无限次数的刷新入口，刷新本地玩家自己的商店卡牌和遗物选项。

不做以下内容：

- 不刷新先古之民事件选项。
- 不刷新普通事件选项。
- 不修改其他玩家的商店库存。
- 不新增卡牌、遗物、药水内容。
- 不绕过购买同步逻辑。
- 不把刷新行为设计成联机共享事件。

## 2. 使用场景

| 场景 | 行为 |
|---|---|
| 单机 | 刷新自己的商店卡牌和遗物 |
| 联机主机安装 | 只刷新主机自己的商店卡牌和遗物 |
| 联机客机安装 | 只刷新客机自己的商店卡牌和遗物 |
| 其他玩家未安装 | 不影响其他玩家商店 |

核心判断：商店库存是玩家本地自己的 `MerchantInventory`，购买结果通过奖励/金币同步链路处理。刷新入口只改当前玩家本地商店库存，不参与先古之民那种事件 index 同步。

## 3. 玩家体验

### 3.1 商店界面

在商店中显示一个“刷新”入口。

交互目标：

- 点击刷新入口后，当前商店的卡牌与遗物重新生成。
- 刷新后界面立即更新。
- 刷新入口始终可用。
- 不扣金币。
- 没有刷新次数上限。

### 3.2 图标与视觉

特别要求：

> 刷新选项复用游戏中商店里删除卡牌的金币图标。

落地方式：

- 优先复用 `NMerchantCardRemoval` 的现成视觉结构。
- 复用删卡服务中的 `%Visual` / `Cost` / `%CostLabel` 视觉区域。
- 刷新入口价格显示建议为 `0`，用于表达“免费”。
- 如果最终 UI 方案选择不显示数字，则仍保留金币图标作为按钮识别符。

视觉优先级：

1. 尽量复用原生商店槽位大小、缩放、悬浮、商人手指指向反馈。
2. 尽量复用删卡服务金币图标，减少新增资源。
3. 不新增外部图片资源，除非原生节点无法稳定复用。

## 4. 源码依据

### 4.1 商店库存结构

商店库存由 `MerchantInventory` 持有：

- `CharacterCardEntries`
- `ColorlessCardEntries`
- `RelicEntries`
- `PotionEntries`
- `CardRemovalEntry`

当前需求只刷新：

- 角色卡牌槽位
- 无色卡牌槽位
- 遗物槽位

暂不刷新：

- 药水槽位
- 删除卡牌服务

原因：客户明确提出商店内遗物与卡牌刷新，且删除卡牌服务本身会被作为 UI 参考对象。

### 4.2 卡牌生成

商店卡牌条目为 `MerchantCardEntry`。

关键行为：

- `Populate()` 会重新生成卡牌。
- 生成后会走 `Hook.ModifyMerchantCardCreationResults`。
- `CalcCost()` 会重新计算价格。

刷新卡牌的最小语义：

```text
对每个未购买/仍有库存的 MerchantCardEntry：
  重新 Populate()
  触发 EntryUpdated
```

### 4.3 遗物生成

商店遗物条目为 `MerchantRelicEntry`。

关键行为：

- 构造时根据稀有度填充遗物。
- 原版在购买后存在 `RestockAfterPurchase` 路径。
- 遗物生成会通过遗物池取物，并影响遗物池状态。

遗物刷新需要额外谨慎：

- 不能简单无限调用原版 `FillSlot` 后丢弃旧遗物，否则可能不断消耗遗物池。
- 刷新时应避免“预览刷掉大量未来遗物”。

建议实现策略：

```text
刷新前记录当前遗物槽位的遗物身份
生成新遗物时排除当前界面已有遗物
刷新完成后只保留新界面结果
避免把未购买的旧遗物当作已获得或永久移除
```

具体实现时优先选择可逆方案：

1. 用反射或补丁包装遗物池抽取过程，刷新失败可回滚。
2. 或使用临时候选池生成预览结果，购买时再走正式获得流程。
3. 避免直接多次调用会永久移除遗物池内容的方法。

## 5. 入口设计

### 5.1 最终方案：删卡服务右下角半尺寸金币图标

入口位置：

- 刷新图标直接叠加在删卡服务节点的右下角。
- 图标大小为删卡服务主图标的 `0.5` 缩放。
- 不替换、不禁用、不遮挡原版删卡服务功能。

布局关系：

```text
商店库存
├─ 角色卡牌
├─ 无色卡牌
├─ 遗物
├─ 药水
└─ 删除卡牌服务
     └─ 刷新入口（右下角叠层，Scale 0.5）
```

刷新入口视觉：

```text
图标：复用删除卡牌服务的金币图标/Visual 纹理/材质
缩放：Vector2(0.5f, 0.5f)
位置：父节点（删卡服务节点）右下角偏移
价格：0，用小字体显示或隐藏
标题：刷新商店
描述：免费刷新当前商店中的卡牌和遗物。不会消耗金币。
```

### 5.2 不使用的方案

不推荐“删卡服务旁新增独立槽位”：

- 会改变商店布局排布。
- 需要额外商店槽位空间。
- 不如右下角叠层视觉紧凑、不易误触。

不推荐“替换删卡服务槽位为双功能”：

- 容易误伤原版删卡功能。
- 用户可能仍需要删卡。
- 联机同步里删卡有专门 `OneOffSynchronizer`，不应复用其真实购买逻辑。

## 6. 刷新规则

### 6.1 卡牌

刷新目标：

- 当前商店全部角色卡牌槽位。
- 当前商店全部无色卡牌槽位。

规则：

- 刷新后同屏不重复。
- 继续使用原版商店卡池限制。
- 继续使用原版联机卡牌限制。
- 继续使用原版稀有度、类型、升级、价格逻辑。
- 已购买而消失的槽位不强制补回，首版仅刷新仍在售的槽位。

### 6.2 遗物

刷新目标：

- 当前商店全部仍在售的遗物槽位。

规则：

- 刷新后同屏不重复。
- 继续遵守 `IsAllowedInShops`。
- 继续遵守玩家已拥有遗物、解锁状态和运行状态限制。
- 购买同步仍由原版遗物购买流程处理。

### 6.3 金币

刷新行为：

- 不检查金币是否足够。
- 不扣金币。
- 不发送金币扣除同步消息。
- 不写入购买历史为消费。

刷新后商品价格：

- 商品自身价格仍由原版规则计算。
- 刷新入口价格固定为 0。

### 6.4 次数

- 无限刷新。
- 不保存刷新次数。
- 不因离开商店而记录额外状态。

## 7. 联机边界

### 7.1 为什么商店可以只影响自己

商店库存偏向本地玩家自己的 `MerchantInventory`。

购买时：

- 买卡会通过原版流程加入本地牌组，并同步获得的具体卡牌。
- 买遗物会通过原版流程获得本地遗物，并同步获得的具体遗物。
- 扣金币走原版金币同步。

刷新本身不产生跨玩家效果。

### 7.2 不承诺的行为

- 不保证其他玩家能看到你的刷新过程。
- 不把你的商店库存广播给其他玩家。
- 不刷新其他玩家商店。
- 不支持刷出其他玩家未安装的 Mod 卡牌/遗物。

## 8. 状态与数据流

### 8.1 正常打开商店

```text
进入商店房间
→ 创建 MerchantInventory
→ UI 读取 Inventory 并创建商店槽位
→ RefreshShop 注入刷新入口
```

### 8.2 点击刷新

```text
点击刷新入口
→ 读取当前 NMerchantInventory.Inventory
→ 刷新卡牌槽位
→ 刷新遗物槽位
→ 触发槽位视觉更新
→ 保持商店打开状态
```

### 8.3 购买刷新后的商品

```text
点击商品
→ 走原版 MerchantEntry.OnTryPurchaseWrapper
→ 原版扣金币
→ 原版获得卡牌/遗物
→ 原版联机同步购买结果
```

## 9. 技术实现草案

### 9.1 项目结构

```text
RefreshShop/
├─ RefreshShop.csproj
├─ mod_manifest.json
├─ project.godot
├─ build.ps1
├─ README.md
├─ LICENSE
├─ docs/
│  └─ DESIGN.md
└─ src/
   ├─ RefreshShopMod.cs
   ├─ HarmonyPatcher.cs
   └─ RefreshShopLog.cs
```

后续实现可继续拆分：

```text
src/
├─ Core/
│  ├─ ShopRefreshService.cs
│  ├─ MerchantInventoryAccess.cs
│  └─ RefreshSafetySnapshot.cs
├─ Patches/
│  ├─ MerchantInventoryPatch.cs
│  └─ MerchantCardRemovalVisualPatch.cs
└─ UI/
   ├─ NMerchantRefreshSlot.cs
   └─ RefreshSlotInjector.cs
```

### 9.2 补丁切入点

候选切入点：

| 切入点 | 用途 |
|---|---|
| `NMerchantInventory.Initialize` 后置 | 商店 UI 完成绑定后注入刷新入口 |
| `NMerchantInventory.Open` 后置 | 确保刷新入口在每次打开时可见 |
| `MerchantCardEntry.Populate` | 可复用原版卡牌刷新逻辑 |
| `MerchantRelicEntry.RestockAfterPurchase` 或等价包装 | 复用遗物补货语义 |

### 9.3 UI 注入策略

首选：

```text
复制/实例化删除卡牌服务附近的 UI 结构
替换点击行为为 Refresh
保留金币图标和价格显示区域
价格文字固定为 0
```

如果直接复制原节点不稳定：

```text
新建 Control 节点
复用原 Cost 容器、CostLabel、Visual 的纹理/材质
接入鼠标点击、悬浮提示、商人手指反馈
```

### 9.4 刷新服务伪代码

```text
RefreshMerchantInventory(inventory):
  if inventory is null:
    return failure

  refreshableCards = inventory.CharacterCardEntries + inventory.ColorlessCardEntries
  for each cardEntry in refreshableCards:
    if cardEntry.IsStocked:
      cardEntry.Populate()
      cardEntry.OnMerchantInventoryUpdated()

  RefreshRelicsSafely(inventory)

  return success
```

遗物刷新伪代码：

```text
RefreshRelicsSafely(inventory):
  snapshot = capture current relic entries and relic pool state
  try:
    for each stocked relicEntry:
      generate replacement that is not already shown
      update entry model and price
      notify entry updated
  catch:
    restore snapshot
    show warning
```

## 10. 风险与规避

| 风险 | 规避 |
|---|---|
| 刷新遗物时消耗遗物池 | 使用快照/回滚或临时候选池 |
| UI 注入破坏删卡服务 | 不替换删卡服务，新增刷新入口 |
| 联机中刷出未知 Mod 内容 | 默认只走当前客户端已加载卡池/遗物池；不承诺跨端未知内容 |
| 刷新入口误扣金币 | 不调用 `PlayerCmd.LoseGold`，不发送 `GoldLostMessage` |
| 原版商店布局变化 | 使用节点名与类型双重匹配，失败时只记录日志不崩溃 |

## 11. 验收标准

### 11.1 单机

- 进入商店后可看到刷新入口。
- 刷新入口使用删卡服务金币视觉。
- 点击刷新后卡牌和遗物发生变化。
- 金币不变。
- 可无限点击刷新。
- 刷新后购买卡牌正常进入牌组。
- 刷新后购买遗物正常进入遗物栏。

### 11.2 联机

- 主机安装时，只刷新主机自己的商店。
- 客机安装时，只刷新客机自己的商店。
- 未安装玩家商店不变。
- 刷新过程不要求其他玩家安装。
- 购买刷新后的原版卡牌/遗物后，其他玩家能看到获得结果。

## 12. 首版范围

首版 `0.1.0` 目标：

- 建立独立 GitHub 仓库项目结构。
- 提供可构建 Godot C# Mod 骨架。
- 完成商店刷新设计文档。
- 后续实现 UI 注入和刷新服务。

首个可玩版本建议标为 `0.2.0`：

- 实现刷新入口。
- 实现卡牌刷新。
- 实现遗物安全刷新。
- 完成单机和双人联机验证。