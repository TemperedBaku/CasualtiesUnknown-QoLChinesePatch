# KrokMP 兼容策略

目标：本补丁不依赖 KrokMP，但玩家因其他 Mod 需要 KrokMP 时也应尽量共存。

## 兼容原则

1. 不引用 KrokMP DLL。
2. 不修改 KrokMP 的按钮、菜单、Steamworks 初始化逻辑。
3. 不创建 `KROKMP_*` 命名对象，避免和真实 KrokMP 撞名。
4. 如果检测到 KrokMP 插件或真实 `KROKMP_*` 对象，跳过可选的 QoLCP mainmenu-lite 兼容对象。
5. 对 QoL 已有 `EnhancedSettingsButton`，只做隐藏式 auto-prime，不追加 onClick。
6. 默认关闭旧 `KrokMP-style lifecycle shim`。

## 为什么这样做

早期完整汉化依赖 KrokMP，是因为 KrokMP 间接推动 QoL 菜单生命周期，使 `EnhancedSettingsPanel(Clone)` 能生成。v0.10.6 以后，补丁通过 Bootstrap auto-prime 自己完成这件事：隐藏式调用 `EnhancedMenuController.OpenMenu()`，短 burst 翻译，再恢复 inactive。

真正需要避免的是重复触发 `OpenMenu()`。如果给 QoL 原按钮追加第二个 onClick，就会出现菜单打开后立刻关闭的双重 toggle。

## 测试矩阵

- QoL Unknown + XTMP + 汉化补丁
- QoL Unknown + XTMP + KrokMP + 汉化补丁
- QoL Unknown + XTMP + KrokMP + 依赖 KrokMP 的其他 Mod + 汉化补丁

重点观察：

- 是否出现重复 EnhancedSettingsButton。
- 点击 QoL 菜单是否闪退/无反应。
- 日志中是否出现 `found existing QoL button; leaving original onClick untouched`。
- 日志中是否出现 `bootstrap auto-prime completed; totalChanged=...`。
