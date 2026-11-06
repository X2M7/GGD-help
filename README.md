# GGD-help

**V0.2.4 · Windows x64 鹅鸭杀透明会议蒙版，原生轻量版。** 基于 [snapea/goose-duck-help](https://github.com/snapea/goose-duck-help) 改进，保留原项目作者归属。直接透过蒙版查看游戏会议人物，在旁边记录身份、两条备注与死亡情况。

项目仓库：[X2M7/GGD-help](https://github.com/X2M7/GGD-help)。

## 下载与启动

从 [V0.2.4 Releases](https://github.com/X2M7/GGD-help/releases/tag/V0.2.4) 下载 **GGD-help-V0.2.4-win-x64.exe**，双击即可运行。发布程序只有一个 exe，可离线运行；不需要安装 Node.js、Electron、WebView2 或下载浏览器组件。[历史 Releases](https://github.com/X2M7/GGD-help/releases) 保留旧版。

原生版使用 Windows Forms 和系统的 .NET Framework 4.x。正常安装的 Windows 10 / 11 已自带该运行环境，无需另外安装运行时；系统组件被精简或损坏的环境需要先修复。[微软的 Windows / .NET Framework 说明](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)。本版本为 x64，未提供 32 位 Windows 版本。

从源码目录启动时，可以双击 **启动助手.cmd**：存在 V0.2.4 exe 时直接启动，否则使用系统 C# 编译器在本地构建。发布 exe 不依赖源码目录。

游戏请使用**无边框窗口或窗口模式**。独占全屏可能遮住桌面蒙版。助手覆盖你选择的显示器，没有自动跟随游戏窗口的功能，需要通过位置校准对齐。

## 体积优化

从 V0.2.0 起，界面与窗口控制迁移到 C# / Windows Forms；当前 V0.2.4 单文件 exe 约 **98 KB**，V0.1.3 Electron 便携包为 **102.51 MB**。大幅缩小的原因是复用 Windows 自带的 .NET Framework，不再把完整 Chromium / Electron 运行环境装进发布包。程序启动也不再需要解包浏览器运行文件。

保留透明蒙版、独立可移动开关、鼠标穿透、身份与备注、死亡记录、功能显示开关和布局校准。界面控件改用 Windows 原生控件，外观与 Electron 旧版有区别。

下拉框、数字轮次框和箭头统一采用深色样式。身份区域现在只有两个联动下拉框：「阵营」和「具体身份」，为长角色名留出更宽的空间。数字框与相邻按钮居中对齐，小屏幕保留最小可操作尺寸。从 V0.2.4 起，文字、按钮和备注采用不透明绘制，记录卡底色仍可独立调节透明度；这样可减少 Windows 合成透明控件时丢字和闪烁的问题。展开的选项列表和设置窗口保持深色不透明，方便阅读。昵称区域继续完全留空。

V0.2.4 针对启动时只剩蒙版、文字不显示和选择框白点闪烁修复绘制流程：固定文字层在底板上方，底板只覆盖可见卡片，选择框和数字箭头在一次缓冲绘制中完成，不再先显示白色系统样式；数字框完整清理背景，避免空隙残留旧字符像素。保留原有记录、角色目录、位置校准和功能开关。

V0.2.2 修复从悬浮菜单进入位置设置等操作时出现的 `ContextMenuStrip` 已释放异常。悬浮菜单和托盘菜单在关闭后继续复用，菜单命令在点击处理结束后执行，避免菜单关闭过程中被提前释放；已有记录和位置校准保留。

V0.2.3 按 Steam 版公告扩充至 79 项身份（好 34、中 11、坏 34），包含清醒梦者、布谷鸟、巫医、走失小鸭、宿主、寄生者、喜鹊、剑客鸭等新角色，并将原先三个身份框改为「阵营 → 具体身份」两个联动选择框。完整名单及来源见 [角色目录](docs/roles.md)。

## 游戏中使用

1. 启动后会显示 16 张记录卡与右上方的悬浮开关。卡片之外的区域透明，无需截图。
2. **拖动悬浮开关左侧的六点手柄**可以移动它；点击「收起蒙版 / 显示蒙版」切换显示，隐藏不会清空记录。
3. 记录卡顶部不显示编号和昵称，留出空间查看游戏原昵称。先在左侧选择「好 / 中 / 坏」，再在右侧选择该阵营的具体身份；阵营待定时，右侧不可选择。切换阵营会恢复该阵营之前记录的身份。下面是「备注 1」与「备注 2」两个文字框，修改后自动保存。
4. 顶部加减按钮和数字框用于手动切换会议轮次。点击人物的死亡方式，默认记录为当前轮死亡；也可以先填写该人物的死亡轮次再补记。
5. 继续操作游戏时，点击顶部「穿透游戏」或悬浮开关「穿透」。记录仍可见，鼠标操作交给游戏；点击悬浮开关「编辑」恢复记录模式。点击「收起」会隐藏蒙版。
6. 下一局点击「新对局」并确认，清空本局角色记录、回到第 1 轮，保留位置校准和功能开关。

| 每人按钮 | 记录方式 |
| --- | --- |
| 刀 | 刀杀 |
| 票 | 投票出局 |
| 刺 | 刺杀 |
| 炸 | 炸死 |
| 吞 | 吞食 |
| 连 | 连带死亡 |
| ？ | 死因不明 |
| 复活 | 清除死亡标记，保留身份推测与两条备注 |

死亡后显示「第 N 轮 · 死亡方式」。可以修改卡片内的轮次或点击其他方式更正死因；推进会议轮次不会改动已有死亡记录。身份选项按 Steam 版角色补齐，名单、核对日期与官方来源见 [角色目录](docs/roles.md)。角色是否可选取决于游戏地图、模式和房间设置，未列出的特殊身份可以写在备注里。所有信息均由玩家手动记录，助手不会自动识别人物、昵称、死亡或会议轮次，也不注入游戏进程。

## 功能显示开关

打开「位置 / 设置」，在「显示功能」中勾选需要的内容：

- **人物身份判定**：统一显示或隐藏每人的阵营与具体身份两个下拉框。
- **备注 1 和 2**：统一显示或隐藏两个备注框。
- **死亡方式与死亡轮次**：统一显示或隐藏死亡按钮、轮次和复活操作。

三个开关默认全选，修改后立即生效并保存。取消勾选只隐藏界面，已有记录仍保留；重新勾选即可继续编辑。隐藏功能后记录卡缩短；三项全关时不显示空底板，位置校准期间仍保留拖动手柄。昵称区域始终留空。

## 分辨率与位置

打开「位置 / 设置」：

- **游戏所在显示器**：选择游戏屏幕，显示当前像素分辨率。插拔显示器或更改分辨率时重新适配。
- **参考分辨率**：提供 1280×720、1366×768、1600×900、1920×1080、2560×1440、3840×2160、1920×1200、2560×1600、2560×1080、3440×1440。
- **游戏画面比例**：游戏两边有黑边时选择实际比例，例如 16:9；铺满整屏时选择「铺满屏幕」。超宽预设使用各自的准确宽高比。预设不会修改系统或游戏分辨率。
- **整体左右 / 上下、位置间距**：整体调整记录卡位置。
- **拖动校准角色位置**：启用后拖动每张卡片的校准柄，对齐对应人物。可以避开头像和发言标识。
- **记录卡大小 / 底色**：调整卡片尺寸与背景透明度。
- **显示人数**：隐藏末尾未使用的卡片，保留其记录。

默认位置来自原项目的 1920×1080、4 列 × 4 行布局，按画面比例缩放。V0.2.1 记录卡加宽以容纳选择框，保留已有位置；原先排列很紧凑时可微调校准。不同游戏版本、人数或 UI 缩放仍可能需要逐人校准。校准按**显示器、分辨率、Windows DPI 缩放**分别保存；「重置当前屏幕位置」需要确认，其他屏幕配置与玩家记录保留。

## 快捷键和恢复

| 快捷键 | 功能 |
| --- | --- |
| Ctrl + Shift + G | 显示 / 隐藏蒙版 |
| Ctrl + Shift + L | 切换记录 / 鼠标穿透 |
| Esc（蒙版获得键盘焦点时） | 退出校准、隐藏设置 |

悬浮开关「···」菜单和系统托盘菜单可以切换显示器、找回开关并恢复编辑、导出记录，以及退出程序。快捷键被其他程序占用时会提示，仍可用鼠标操作。再次运行同一配置的原生 exe 会找回现有窗口。

编辑备注时，键盘输入属于助手。回到游戏前使用「穿透」或「收起」，或点击游戏的透明空白区域。重启会恢复为可见、可编辑状态。

## 数据与旧版升级

记录、布局和偏好保存在：

```text
%APPDATA%\GGD-help\native-state.json
```

每次保存通过临时文件替换记录，并在 `native-state.json.bak` 中保留上一次有效版本。记录文件损坏时优先读取该备份；原损坏文件在后续保存时另存为 `.corrupt-*`。这份滚动备份会随着保存更新，长期留档请使用「位置 / 设置」中的**导出记录**。导入 JSON 会在确认后替换当前记录、偏好和布局。

从 V0.1.x 升级：

1. **先完全退出旧版**，再启动 V0.2.4。
2. 如果尚无 `native-state.json`，程序会尝试读取 `%APPDATA%\GGD-help` 中旧版 Electron 的记录。迁移只读旧文件，身份、两条备注、死亡记录和功能开关会迁入原生数据文件；原 Electron 数据保持不变。
3. 旧版屏幕校准在分辨率与 DPI 唯一匹配时复用；无法唯一匹配的屏幕需要重新校准，原配置仍保留。
4. 旧数据被占用或无法解析时会提示。可以退出、关闭旧版后重试，也可以明确选择以空白记录启动。需要恢复时可在设置中导入已导出的 JSON。

原生版也接受包含旧记录键 `goose-duck-overlay:v2` 的 JSON。新旧版本的数据此后分别保存，不会自动双向同步。若仅有更早的 `%APPDATA%\goose-duck-help-overlay` 目录，原生版不会自动扫描该目录；请先备份，再通过旧版 GGD-help 完成其已有的目录迁移，或导出兼容的 JSON 后导入。

从 V0.2.2 及更早版本升级到两级身份选择时，原先三个阵营中的身份记录都会保留。旧记录有多个身份推测时，优先显示与原单一身份匹配的阵营，否则按好、中、坏顺序显示首个已有记录；切换阵营可查看其余记录。没有身份的玩家保持「阵营待定」。无法识别的旧自定义身份仍保留，不会因目录更新被删除。

若提示保存失败，请检查磁盘空间和配置目录权限，先导出记录再退出。

## 运行源码与构建

原生版构建要求：Windows x64、Windows PowerShell、系统 .NET Framework C# 编译器。**不需要 npm install 或 .NET SDK**，构建过程可离线执行。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-native.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-native.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-native-models.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-native-components.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-native-menus.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-native.ps1
```

- `build-native.ps1`：编译 `native/*.cs`（不包含测试代码），生成 `release/GGD-help-V0.2.4-win-x64.exe` 和 `release/SHA256SUMS.txt`。
- `run-native.ps1`：启动原生 exe，不存在时先构建；加 `-Build` 强制重新构建。
- `test-native-models.ps1`：检查身份目录、旧角色记录迁移、阵营选择持久化、比例坐标、功能开关、存储恢复与旧数据解析；可通过 `-Fixtures` 额外传入真实 Electron 迁移样本目录。
- `test-native-components.ps1`：不显示窗口，检查阵营联动与角色过滤、控件缩放、功能开关、死亡轮次、输入状态和选择框绘制。
- `test-native-menus.ps1`：在独立 Windows 桌面使用隔离配置，检查悬浮菜单和托盘菜单的关闭、重复打开、设置、显示器切换和退出时机，不操作日常桌面。
- `test-native.ps1`：把单个发布 exe 复制到独立临时目录，验证真实鼠标选择菜单、数字箭头与边界、中文输入、角色记录、功能开关、文字实际显示、底板透明度与层级、透明区域穿透、置顶、悬浮开关和重启保存。测试会使用桌面的鼠标和键盘，请勿同时操作；测试配置与日常记录分开，截图仅写入本地 `artifacts`。

安装了 Node.js 时也可以使用 `npm start`、`npm run dist`、`npm test`、`npm run test:release`；这些默认命令都已指向原生版，其中 `npm start` 会先重新编译。

验证环境为 Windows 11；尚未在 Windows 10 真机或实际游戏中完成复测。原生版使用 Windows 原生按钮、输入框和窗口命中检测，避开旧 Electron 透明窗口对鼠标移动消息的依赖。不同游戏、全屏模式和多屏配置仍需实际对齐验证。

## 保留的 Electron 源码

旧界面代码继续保存在仓库中，方便对照和维护。开发旧版需要 Node.js 22.12+ 和 npm，首次安装依赖需要联网：

```powershell
npm ci
npm run start:electron
npm run check:electron
npm run test:electron
npm run test:smoke:electron
npm run package:electron
npm run dist:electron
npm run test:release:electron
```

Electron 目录包输出到 `out/GGD-help-win32-x64`；便携包输出到 `release/electron/GGD-help-Electron-V0.2.4-win-x64.exe`，与原生发布包分开保存。V0.2.4 Releases 提供的是原生轻量版。

已安装这些开发依赖时，可额外生成真实 Electron 存储样本，验证原生迁移器读取 WAL、压缩 SST、最新写入与删除记录。生成器只使用新建的临时配置，不读取日常记录：

```powershell
node scripts/make-native-fixtures.cjs
$fixtures = (Get-Content -Raw -Encoding UTF8 artifacts/native-fixtures.json | ConvertFrom-Json).profile
.\scripts\test-native-models.ps1 -Fixtures $fixtures
```

## 源码结构

- `native/Program.cs / OverlayContext.cs / NativeWindows.cs`：启动、窗口管理、快捷键、显示器与鼠标穿透。
- `native/LauncherForm.cs / OverlayForms.cs / PlayerCard.cs / SettingsForm.cs`：悬浮开关、透明蒙版、记录卡与设置。
- `native/ThemedControls.cs`：保留原生输入行为的深色下拉框与数字框。
- `native/Models.cs / StateStore.cs / LegacyStorage.cs`：记录和布局、原子保存与备份、旧版 Electron 数据的只读迁移。
- `native/ModelTests.cs / native/tests`：原生逻辑及界面测试。
- `index.js / preload.js / renderer.js / core.js / launcher.* / tests`：保留的 Electron 实现及其测试。

项目保留 `package.json` 中的原作者归属与 ISC 许可证声明，原始代码来源见上方项目链接。
