# QoL Unknown Chinese Patch v0.10.14 legacy-base QoL 1.0.4.5 backport

这是基于 v0.10.11 scene_reprime_fix 旧版稳定代码路线制作的测试版。

## 目标

- 保留 v0.10.11 的 Bootstrap / 旧 QoL 面板触发与翻译逻辑。
- 回填 QoL Unknown 1.0.4.5 / 游戏 v7.0.1 新增词条。
- 新增 QoLModuleManager 模块注册表汉化：直接修改 `QoL_Unknown.QoLModuleManager.Modules` 中的 `Name` / `Description`，用于修复主菜单右侧旧 QoL 模块列表。
- Bootstrap 增加 GUILayout 文本方法补丁，用于覆盖 IMGUI/GUILayout 绘制的旧模块列表。

## 不包含

- 铁人模式
- 控制台锁
- 战争迷雾分辨率改名
- KrokMP 名字修复功能

## 编译

PowerShell 示例：

```powershell
cd "D:\ModWork\QoLChinesePatch_SourceKit_v0.10.14_legacybase_qol1045_backport"
.\build.ps1 -GameDir "D:\steam\steamapps\common\Casualties Unknown Demo"
```

编译成功后会生成：

```text
.\QoLChinesePatch_release.zip
.\release\QoLChinesePatch\QoLChinesePatch.Bootstrap.dll
.\release\QoLChinesePatch\QoLChinesePatch.dll
.\release\QoLChinesePatch\translations.zh-CN.json
.\release\QoLChinesePatch\phrases.zh-CN.json
```

## 安装

先删除旧版：

```powershell
Remove-Item "D:\steam\steamapps\common\Casualties Unknown Demo\BepInEx\plugins\QoLChinesePatch" -Recurse -Force
```

再把 `release/QoLChinesePatch` 放到：

```text
BepInEx/plugins/QoLChinesePatch/
```

这个旧路线版本需要同时安装：

```text
QoLChinesePatch.Bootstrap.dll
QoLChinesePatch.dll
translations.zh-CN.json
phrases.zh-CN.json
```

## 测试重点

1. 主菜单右侧 QoL 模块列表是否汉化。
2. v7 本局设置 / Custom Settings 是否仍然汉化。
3. 中文字体是否保持 v7.0.1 内置 XTMP 风格。
4. 日志是否出现：

```text
QoL module registry localized [startup]
Bootstrap GUI/GUILayout text methods patched
```


## v0.10.14 copyright-safe UI final

- Reverted broad wiki-name import from v0.10.13.
- Kept only targeted QoL UI/dropdown internal IDs needed by QoL 1.0.4.5.
- Added remaining labels: `Seed` -> `种子`, `Custom settings` -> `自定义设置`.
- Does not include full wiki descriptions or wholesale wiki content.
