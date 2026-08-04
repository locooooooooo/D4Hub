# 开荒地图 HUD 本地数据样例

本目录提供 D4Hub 开荒地图 HUD 的本地数据格式参考。**所有坐标均为演示数据**，不代表游戏内真实位置。

## 目录结构约定

地图底图目录（`MapHudSettings.MapImagePath`）：

```text
{MapImagePath}/
  dry_steppes.png        # 干旱草原
  fractured_peaks.png    # 碎峰岭
  scosglenn.png          # 斯科斯格伦
  hawezar.png            # 哈维泽
  kegistan.png           # 凯基斯坦
```

- 区域 key 固定为 `dry_steppes` / `fractured_peaks` / `scosglenn` / `hawezar` / `kegistan`；
- 缺少某区域图片时，该区域 HUD 只显示计时条不显示底图（fail-closed）。

## 社区 POI JSON

`MapHudSettings.PoiDataPath` 指向 `poi-sample.json` 同格式文件：

- `category` 枚举：`chest`（种秘宝箱）、`elite`（精锐宝箱）、`event`（事件）、`ritual`（仪式）、`dungeon`（地城虫）；
- `x` / `y` 为归一化坐标（0–1，相对底图左上角）；
- 非法单条（坐标越界、未知类别）会被拒绝，其余数据保留；
- `formatVersion` 必须为 1，否则整体拒绝。

## 世界事件计时条

三条计时（地狱狂潮 / 世界 Boss / 军团 Boss）基于公开轮换日程推算：

- 地狱狂潮：约 2 小时 15 分一轮，进行约 55 分钟；
- 世界 Boss：约 3 小时 30 分一轮，进行约 15 分钟；
- 军团：约 30 分钟一轮，进行约 10 分钟。

日程为**推算基线**，`MapHudSettings.ScheduleOffsetSeconds` 可整体平移校准（负值提前、正值延后）。计时条不声称游戏内部精度。

## 安全边界

- 底图、POI、音频全部只接受用户本地显式提供的路径；
- 禁止把 D4 游戏贴图、地图二进制或未审核资产放入程序集或随包分发；
- D4Hub 不读取游戏内存、不注入、不发送自动输入。
