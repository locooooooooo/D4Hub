# D4Hub 开荒地图 HUD 需求文档

状态：需求已确认（2026-08-03 三项前置决策已拍板）；P0/P1/P2 已实现

日期：2026-08-03

本文把用户提供的第三方开荒地图工具截图转化为 D4Hub 的开荒地图 HUD 需求。截图字段为重建推断（见 §3 说明），仅用于布局与功能参考，不代表 D4Hub 可取得游戏内部地图或坐标。

## 1. 产品目标与边界

### 1.1 产品目标

在 D4Hub 现有 HUD 覆盖层架构上，提供一个贴附于暗黑破坏神 4 客户端的**开荒地图 HUD**：

- P0：透明、置顶、点击穿透的地图覆盖窗口，显示当前区域地图底图 + POI 标记 + 顶部世界事件计时条（地狱狂潮 / 世界 Boss / 军团 Boss），并支持布局编辑与自动贴附；
- P1：把截图中的控制面板（区域选择、地图大小、显示内容、透明度、覆屏大小、计时条样式）落到 `Document.Overlay.MapHud` 持久化设置与主窗口设置区；
- P2：本地音频提醒、快捷键、多区域 POI 数据、世界事件日程推算。

### 1.2 安全边界（强制）

- 只读取用户屏幕上可见的 D4 客户端像素和用户主动提供的本地文件（地图图片、POI 数据 JSON）；
- **不读取游戏进程或内存，不注入，不检查游戏网络流量，不发送自动输入，不做自动寻路或自动拾取**；
- **不把 D4 游戏衍生贴图、地图二进制或未审核的游戏资产作为运行时依赖**——地图底图只能来自用户本地提供的图片文件或程序化示意绘制，不能随包分发游戏资源；
- POI 标记坐标只能来自用户本地维护的 JSON 或用户在探索时手动落点，不能从游戏内存读取；
- 统计数据与地图数据默认只在本地会话/本机文件存在，不上传用户屏幕、地图或 POI 数据。

### 1.3 当前迭代的非目标

- 不承诺标记点与游戏内真实位置的精度（精度取决于用户提供的底图与 POI 数据质量）；
- 不把世界事件计时条宣称为游戏内部精确倒计时（它基于公开轮换日程推算，可能漂移）；
- 不在没有用户底图与 POI 数据时为区域伪造地图或坐标。

## 2. 当前实现基线

以下结论以 2026-08-03 工作树为准，后续代码变更须同步更新本节。

| 能力 | 当前实现 | 地图 HUD 复用方式 |
| --- | --- | --- |
| HUD 覆盖窗口 | `StatisticsOverlayWindow`、`OverlayWindow`、`TransmutationReminderWindow` 均为 `Topmost + AllowsTransparency + WS_EX_TRANSPARENT/TOOLWINDOW/NOACTIVATE`，并调用 `OverlayCapturePolicy.ExcludeFromCapture` | 新建 `MapOverlayWindow` 复用同一窗口范式与点击穿透代码 |
| 放置与自动贴附 | `HudViewModel` 持有 `HudPlacement`/`StatisticsHudPlacement` 等 record，`CalculateStatisticsHudPlacement` + `UpdateHudPlacement` 基于角色面板检测定位 | 新增 `MapHudPlacement` 与 `CalculateMapHudPlacement`，复用视口/面板检测 |
| 布局编辑 | `IsLayoutEditing` + `Begin/Cancel/Reset/SaveLayoutTemplate` 命令；`SetLayoutEditing` 在编辑态移除透明标志以允许交互 | 地图 HUD 复用同一编辑态切换，编辑态同时揭示设置面板 |
| 持久化设置 | `Document.Overlay` 含 `HudDisplayMode`、`DamageStatisticsHudEnabled`、`StatisticsHudCompact`、`AutoAttach` 等 | 新增 `Document.Overlay.MapHud` 子节 |
| 世界事件 | `HelltidesPrivacyPolicy.cs` 已存在，说明地狱狂潮为已考量主题 | 计时条基于公开日程推算，复用隐私策略约束 |
| 地图 / POI 模型 | **无** | 已新增 `MapHudSettings`、`PoiMarker`/`PoiMarkerCategory`、`PoiCatalogStore`、`WorldEventClock`/`WorldEventSchedule`（`src/D4Hub.Core/MapHudModels.cs`） |
| 地图 HUD 窗口 | — | 已新增 `MapOverlayWindow`（`src/D4Hub.App/MapOverlayWindow.xaml(.cs)`），复用现有窗口范式与点击穿透，含计时条 + 底图 + POI 叠加 |
| 放置与接线 | — | `HudViewModel` 已暴露 `MapHudPlacement`/`MapHudHidden` 回调、`IsMapHudEnabled`、`CalculateMapHudPlacement`；`MainWindow` 已接线 |
| 验收 | — | 已新增 4 项验收检查（设置持久化 / 放置锚定 / 日程推算偏移 / POI 校验），全套 101 项通过 |

现有 HUD 的窗口生命周期、点击穿透、自捕获排除、布局编辑验收均已通过既有实现覆盖；地图 HUD 不应重新发明这些机制。

> 2026-08-03 状态：P0/P1/P2 均已实现并通过构建与验收检查（103 项）。
> - P0：`MapOverlayWindow`（置顶/透明/穿透 + 计时条 + 底图 + POI）、`HudViewModel` 生命周期集成、`CalculateMapHudPlacement`。
> - P1：设置面板落在**"地图工具"工作区**左侧（360px 控制面板，右侧保留 Helltides 社区地图 WebView），覆盖当前区域、显示内容 5 项、地图透明度、覆屏大小、计时条背景宽度、横排/竖排、底图目录、POI JSON、日程偏移；全部绑定 `Document.Overlay.MapHud` 持久化，变更经 VM 防抖（120ms）触发 `MapOverlayWindow.RefreshMap()` 实时生效。
> - P2：音频提醒 + 全局热键 + 多区域 POI 切换。
>   - **音频提醒**：`WorldEventEdgeTracker`（Core 纯逻辑）检测世界事件"等待→进行中"上升沿，`MapOverlayWindow` 每秒驱动，`MediaPlayer` 实例池播放用户本地文件（Boss→世界Boss、蓝→地狱狂潮、蓝品→军团）；默认静音，`AudioVolume` 可调，无文件/播放失败一律静默。
>   - **全局热键**：切换显隐（默认 `~`）、重绘地图（默认 F5）、重置位置（默认 F6），存 `MapHudSettings.Hotkey*`（WPF Key 枚举名），`GlobalHotkeyService` 新增 `UnregisterAll`，配置变更整体重注册，注册失败（被占用）不影响其余；F2 跟踪开关保留。
>   - **多区域 POI 切换**：P1 区域选择已按区域重载 POI 数据集，P2 无新增工作。

## 3. 截图字段与阶段映射

> 说明：本模型当前运行的视觉模型不支持图片解析，以下字段由早前一轮交互中对截图的重建推断得到，**实现前须由用户对照实际截图逐项核对**，不得直接当作已确认规格。

截图可见模块：

| 分类 | 模块 | 阶段 | 当前/目标语义 |
| --- | --- | --- | --- |
| 计时 | 地狱狂潮倒计时 + 状态 | P0 | 基于公开轮换日程推算；显示倒计时与"进行中/等待中" |
| 计时 | 世界 Boss 倒计时 + 状态 | P0 | 同上 |
| 计时 | 军团 Boss 倒计时 + 状态 | P0 | 同上 |
| 地图 | 区域底图（如 DRY STEPPES） | P0 | 用户本地图片或程序化示意；不打包游戏资产 |
| 地图 | 种秘宝箱标记 | P0 | 来自本地 POI JSON，可独立开关 |
| 地图 | 精锐宝箱标记 | P0 | 同上 |
| 地图 | 事件标记 | P0 | 同上 |
| 地图 | 仪式标记 | P0 | 同上 |
| 地图 | 地城虫标记 | P0 | 同上 |
| 控制 | 当前区域下拉 | P1 | 切换底图与对应 POI 数据集 |
| 控制 | 地图大小滑块 | P1 | 影响 HUD 窗口尺寸/底图缩放 |
| 控制 | 显示内容复选（含"显示浮层地图"） | P1 | 各标记类别独立开关 |
| 控制 | 声音设置（开/音量/音效 Boss·蓝·蓝品） | P2 | 仅播放用户本地音频文件 |
| 控制 | 覆屏大小滑块 | P1 | HUD 整体缩放系数 |
| 控制 | 计时条背景宽度（0=透明） | P1 | 计时条不透明度 |
| 控制 | 计时条排列（横排/竖排） | P1 | 计时条布局方向 |
| 控制 | 快捷键（点击设置/重置/切换显隐/重绘） | P2 | 全局热键 |

## 4. 功能需求

### 4.1 HUD 窗口与贴附

- **FR-MAP-01** 新建 `MapOverlayWindow`，沿用 `StatisticsOverlayWindow` 的窗口范式：`Topmost`、`AllowsTransparency`、`WindowStyle=None`、`IsHitTestVisible=False`、`ShowInTaskbar=False`、`ExcludeFromCapture`，点击穿透标志沿用 `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`。
- **FR-MAP-02** `HudViewModel` 新增 `MapHudPlacement` 与 `CalculateMapHudPlacement`，复用现有视口/角色面板检测做自动贴附；未开启 `AutoAttach` 时回退到上次保存位置。
- **FR-MAP-03** `MapHudEnabled` 开关复用 `DamageStatisticsHudEnabled` 同款持久化与显隐通道；关闭时不创建或隐藏窗口。

### 4.2 地图底图与标记

- **FR-MAP-04** 底图来源只能是：(a) 用户在设置中指定的本地图片文件路径，或 (b) 程序化示意绘制；**严禁**在程序集中嵌入 D4 游戏地图二进制或贴图。
- **FR-MAP-05** POI 标记来自用户本地 JSON（路径在设置中指定），每条含 `Region`、`Category`、`X`、`Y`（归一化 0–1）、可选 `Label`；不支持从游戏内存读取坐标。
- **FR-MAP-06** 五种标记类别（种秘宝箱/精锐宝箱/事件/仪式/地城虫）各自独立开关；关闭类别时不渲染对应标记，不计入计数。
- **FR-MAP-07** 标记渲染使用 WPF `Image`（底图）+ `Canvas`/`ItemsControl`（标记）叠加，或 `WriteableBitmap`；标记颜色/图标按类别区分并可在设置中调整。

### 4.3 计时条

- **FR-MAP-08** 顶部计时条显示地狱狂潮 / 世界 Boss / 军团 Boss 三项倒计时，状态分"进行中/等待中"；时间基于公开轮换日程从 `DateTimeOffset` 推算，**不声称游戏内部精度**。
- **FR-MAP-09** 计时条支持横排/竖排切换，背景宽度（0=透明）与整体覆屏大小可调；样式参数持久化。

### 4.4 布局编辑与设置

- **FR-MAP-10** 复用 `IsLayoutEditing` 与 `SetLayoutEditing`；地图 HUD 在编辑态移除透明标志以允许拖拽定位，同时揭示控制面板并使其可交互。
- **FR-MAP-11** 控制面板（区域、显示内容、透明度、覆屏大小、计时条样式）落入主窗口设置区，对应状态持久化到 `Document.Overlay.MapHud`；编辑结束（Esc/保存）后回到点击穿透态。
- **FR-MAP-12** 设置变更不重置地图会话；位置校准、点击穿透、置顶、失焦行为须有独立验收证据（复用现有 HUD 验收夹具模式）。

### 4.5 音频与快捷键（P2）

- **FR-MAP-13** 音频提醒仅播放用户本地文件（Boss/蓝/蓝品可分别指定），不捆绑任何游戏或第三方音频；默认静音，开启需显式确认。
- **FR-MAP-14** 快捷键：切换显隐、重绘地图、重置位置；使用全局钩子时须可配置且不抢占游戏默认按键。

## 5. 数据模型与接口约束

### 5.1 建议新增模型（Core）

```csharp
public readonly record struct MapHudPlacement(int Left, int Top, int Width, int Height);

public sealed class MapHudSettings
{
    public bool Enabled { get; set; }
    public string CurrentRegion { get; set; } = "dry_steppes";
    public double Opacity { get; set; } = 1.0;
    public double OverlayScale { get; set; } = 1.1;
    public double TimerBarBackgroundWidth { get; set; } = 0.40;
    public bool TimerBarHorizontal { get; set; } = true;
    public bool ShowChests { get; set; } = true;
    public bool ShowEliteChests { get; set; } = true;
    public bool ShowEvents { get; set; } = true;
    public bool ShowRituals { get; set; } = true;
    public bool ShowDungeons { get; set; } = true;
    public string? MapImagePath { get; set; }   // 用户本地底图，禁止游戏资产
    public string? PoiDataPath { get; set; }     // 用户本地 POI JSON
    public bool AudioEnabled { get; set; }
    public string? AudioBossPath { get; set; }
    public string? AudioElitePath { get; set; }
    public string? AudioBluePath { get; set; }
}

public sealed class PoiMarker
{
    public string Region { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // chests/elite/event/ritual/dungeon
    public double X { get; set; }   // 归一化 0-1
    public double Y { get; set; }
    public string? Label { get; set; }
}
```

### 5.2 接口约束

- `MapOverlayWindow` 构造签名沿用 `StatisticsOverlayWindow(HudViewModel viewModel)`；
- 放置回调 `MapHudPlacementChanged` 在 `HudViewModel` 中声明，供窗口订阅；
- `Document.Overlay.MapHud` 为新增子节，序列化须向后兼容（缺省字段不抛异常）；
- 底图/POI/音频路径必须是用户显式提供的本地路径，加载失败须 fail-closed（隐藏对应图层并提示），不得回退到游戏资源。

## 6. 技术方案

```text
前台 D4 可见帧（仅用于自动贴附定位）
  -> 视口/角色面板检测（复用现有）
  -> CalculateMapHudPlacement
  -> MapOverlayWindow（置顶/透明/穿透）
       -> 底图 Image（用户本地文件或示意绘制）
       -> POI 标记 Canvas（本地 JSON）
       -> 顶部计时条（公开日程推算）
  -> 布局编辑态：移除穿透 + 揭示设置面板
  -> 设置持久化到 Document.Overlay.MapHud
```

- 继续复用 WPF + .NET 8、`HudViewModel`、`OverlayCapturePolicy`、`RelayCommand` 与现有 HUD 验收夹具；
- 地图 HUD 不引入新的屏幕识别管线（除非未来单独立项），其定位仅依赖已有的视口/面板检测；
- 世界事件日程使用本地配置表（公开轮换周期），不在运行时联网校验。

## 7. 实施路线与交付物

### P0：地图 HUD 窗口与贴附

1. 新增 `MapHıdPlacement` 与 `CalculateMapHudPlacement`，复用 `UpdateHudPlacement` 通道；
2. 新建 `MapOverlayWindow.xaml/.cs`，复制 `StatisticsOverlayWindow` 的窗口范式与点击穿透；
3. 底图从 `MapImagePath` 加载（失败 fail-closed），POI 标记从 `PoiDataPath` 加载；
4. 顶部计时条三项倒计时（公开日程推算）+ 状态文案；
5. 复用 `IsLayoutEditing` 支持拖拽定位，Esc 退出编辑态。

### P1：设置与控制面板

1. `Document.Overlay.MapHud` 持久化全部显示/样式开关；
2. 主窗口设置区新增"地图 HUD"分组（区域、显示内容、透明度、覆屏大小、计时条）；
3. 横排/竖排、背景宽度、覆屏大小实时生效。

### P2：音频与快捷键

1. 本地音频提醒（Boss/蓝/蓝品），默认静音；
2. 全局热键：切换显隐 / 重绘 / 重置；
3. 多区域 POI 数据集切换。

## 8. 验收门槛

### G0：契约与安全边界

- `git diff --check`、现有验证脚本与安全边界扫描通过；
- 代码、文档、UI 均不出现内存读取、注入、自动输入或游戏资产打包暗示；
- 底图/POI/音频加载失败均 fail-closed，不回退游戏资源。

### G1：P0 窗口与贴附

- `MapOverlayWindow` 置顶、透明、点击穿透、`ExcludeFromCapture` 行为可重放；
- 自动贴附在 1920×1080 / 1280×960 真实游戏画面下定位合理；
- 底图与 POI 来自本地文件时正常渲染；路径缺失/损坏时隐藏图层不崩溃；
- 计时条三项倒计时随真实时间递减，状态文案正确。

### G2：P1 设置

- 全部显示/样式开关持久化并在重启后保持；
- 横排/竖排、透明度、覆屏大小、背景宽度实时生效；
- 布局编辑→保存→重启后位置保留。

### G3：P2 音频与热键

- 音频仅播放用户本地文件，默认静音；
- 热键可配置且不抢占游戏默认按键；
- 多区域 POI 切换正确加载对应数据集。

## 9. 风险与回退

| 风险 | 影响 | 回退 |
| --- | --- | --- |
| 误打包 D4 游戏地图资产 | 违反安全边界与许可 | 底图仅限用户本地文件或示意绘制；CI 扫描阻断游戏资源 |
| POI 数据缺失/错误 | 标记偏离真实位置 | 无数据时不渲染；提示用户补充本地 JSON |
| 世界事件日程漂移 | 计时条不准 | 标注"日程推算"；允许用户手动校准偏移 |
| 点击穿透失效 | 遮挡游戏操作 | 复用现有 `SetLayoutEditing` 验证；编辑态外强制透明标志 |
| 底图比例与游戏不一致 | 标记错位 | 归一化坐标 + 用户校准锚点；不声称像素级对齐 |

## 10. 参考

- HUD 窗口范式：`src/D4Hub.App/StatisticsOverlayWindow.xaml(.cs)`、`OverlayWindow.xaml(.cs)`；
- HUD 编排与放置：`src/D4Hub.App/ViewModels/HudViewModel.cs`、`src/D4Hub.Core/HudModels.cs`；
- 地狱狂潮隐私策略：`src/D4Hub.Core/HelltidesPrivacyPolicy.cs`；
- 现有 HUD 需求模板：`docs/product/2026-08-03-damage-statistics-hud-requirements.md`；
- HUD 预览 Owner 决策：`docs/product/2026-07-22-owner-decisions-hud-preview.md`。

### 已确认的前置决策（2026-08-03）

1. **底图来源**：每个区域由用户放置本地图片。`MapHudSettings.MapImagePath` 指向用户本地的底图目录，约定目录结构 `{MapImagePath}/{regionKey}.png`（如 `dry_steppes.png`）；缺少某区域底图时该区域 fail-closed（不渲染并提示），程序集内不嵌入任何游戏资产。
2. **POI 数据**：导入社区 JSON。`MapHudSettings.PoiDataPath` 指向本地 JSON 文件，格式见 §5.3；坐标归一化 0–1，渲染时按底图实际尺寸缩放。D4Hub 不读取游戏内存，社区 JSON 的准确性与合规性由导入者负责。
3. **计时精度**：公开日程推算 + 允许手动偏移校准。`MapHudSettings.ScheduleOffsetSeconds` 允许用户整体平移三条计时线（负值提前、正值延后）；UI 标注"日程推算"，不声称游戏内部精度。

### 5.3 社区 POI JSON 格式（约定）

```json
{
  "formatVersion": 1,
  "regions": [
    {
      "key": "dry_steppes",
      "name": "干旱草原",
      "markers": [
        { "category": "chest", "x": 0.22, "y": 0.15 },
        { "category": "elite", "x": 0.35, "y": 0.18, "label": "精锐宝箱" },
        { "category": "event", "x": 0.18, "y": 0.18 }
      ]
    }
  ]
}
```

- `category` 枚举：`chest`（种秘宝箱）、`elite`（精锐宝箱）、`event`（事件）、`ritual`（仪式）、`dungeon`（地城虫）；
- `x`/`y` 为归一化坐标（0–1，相对底图左上角）；超出 `[0,1]` 的记录加载时拒绝；
- 未知 `category`、缺失 `x`/`y` 的记录拒绝但保留文件其余部分（单条失败不炸整个文件）；
- `formatVersion` 与当前实现不符时整体拒绝并提示升级数据。

### 5.4 世界事件日程推算（设计）

```csharp
public enum WorldEventKind { Helltide, WorldBoss, Legion }

public sealed record WorldEventScheduleEntry(
    WorldEventKind Kind,
    TimeSpan Cycle,     // 公开轮换周期
    TimeSpan Duration,  // 进行中时长
    TimeSpan PhaseOffset); // 相对整点/纪元起点的相位偏移

public sealed class WorldEventClock
{
    private readonly WorldEventScheduleEntry[] _entries;
    private TimeSpan _manualOffset; // 用户手动偏移校准

    public (TimeSpan Remaining, bool Active) Evaluate(WorldEventKind kind, DateTimeOffset now);
}
```

- 默认周期/时长来自公开社区共识，作为**可配置常量**，UI 允许用户校准 `_manualOffset`；
- `Evaluate` 纯函数（`now` 注入，可测试），不依赖网络；漂移风险由 §9 回退策略承接。
