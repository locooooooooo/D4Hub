# D4Hub 图像识别工具箱整理

## 目标

把两个外部仓库中可复用的静态图像识别能力整理进 D4Hub，形成一个输入截图、输出识别回执和标注图的独立工具箱。工具箱服务于截图调试、标注、离线回放和算法对比，不替代 D4Hub 当前的 Windows OCR 实时基线。

## 上游记录

| 来源 | 当前提交 | 可吸收内容 | 不接入内容 |
| --- | --- | --- | --- |
| [D4_vrs](https://github.com/HUANGYIHAO382/D4_vrs) | `400ff7137f0503b674ed092f708255540d6ee142` | `inventory_grid.py` 的全局网格拟合、`detector.py` 的空格/已善变/待善变分类、Unicode 图像 I/O | 未来自动点击配置、原始项目脚本入口 |
| [Diablo-IV-auto](https://github.com/Chucwv/Diablo-IV-auto) | `740b099bae5ceaedd71332235704b2e0bfab0ee3` | `vision.py` 的静态模板匹配、`loot.py` 的颜色掩膜/密度过滤思路 | `control.py`、`navigate.py`、`party.py`、`recorder.py`、`loot.py` 中的点击/按键/状态机/自动拾取 |

两个上游仓库在检查时都没有独立的 `LICENSE` 文件。D4Hub 因此没有把上游源码、自动化资源、个人路径、模板图片或 Tesseract 语言数据直接复制进默认发布内容，而是在 `tools/D4Hub.VisionToolbox` 中提供独立适配实现，并保留本表作为来源和差异记录。后续若需要直接再分发上游代码，应先取得明确许可并单独完成许可证审查。

## 现有边界

- 输入只允许本地静态图像或模板路径；工具不自动抓取窗口，也不要求管理员权限。
- 输出是 `schemaVersion=1` 的诊断 JSON 和标注 PNG；输入只绑定文件名、尺寸和 SHA-256，不上传图像。
- `quality=heuristic` 表示阈值/模板/颜色启发式结果，不是准确率、召回率、精确 DPS 或游戏状态证明。
- 工具箱不引用 `pydirectinput`、`pyautogui`、`keyboard`、`mouse`，不激活窗口、不点击、不发送按键、不录制输入、不执行路径回放。
- 识别结果不能直接进入 D4Hub 统计总量；需要经过现有 ROI、质量标签和多帧确认合同，或先进入离线 shadow/replay 证据。

## 目录

```text
tools/D4Hub.VisionToolbox/
  config/transmute.yaml
  d4hub_vision_toolbox/
    io.py              # Unicode 路径、哈希和 JSON 回执
    template_match.py  # 静态模板匹配
    loot_labels.py     # 掉落颜色与密度候选
    transmute.py       # 背包网格与善变状态
    gold_road.py       # 黄门流程场景证据
    cli.py             # 统一命令入口
  tests/test_toolbox.py
```

## 验收边界

当前实现验收的是模块可运行、输入输出合同、Unicode 路径和合成图行为。真实分辨率、语言、HDR、UI 缩放、掉落标签 precision/recall 和游戏窗口 provenance 仍需使用经过隐私清理且有人工标注的回放单独验证。
