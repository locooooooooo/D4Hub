# D4Hub Vision Toolbox

一个只读的 Diablo IV 图像识别工具箱。它接收本地截图和模板，输出 JSON 识别回执与标注图，不捕获窗口、不发送键鼠输入、不录制全局输入，也不执行自动刷图流程。

## 能力

- `transmute`：识别善变界面的背包网格，区分空格、已善变和待善变物品，并生成网格/待处理遮罩。
- `template`：对静态截图做单个或多个模板匹配，支持阈值和空间去重。
- `loot`：按掉落文字颜色和几何密度识别可能的地面掉落标签，只输出候选框和颜色类别。
- `gold-road`：用用户提供的模板目录识别黄门流程中的可见场景，只输出场景状态和证据。

所有结果都标为 `evidenceClass=diagnostic`、`quality=heuristic`。它们用于调试、标注和离线回放，不代表精度、召回率、精确 DPS 或游戏状态。

## 安装

需要 Python 3.10+：

```powershell
python -m pip install -r .\requirements.txt
```

## 用法

从工具箱目录运行：

```powershell
Set-Location .\tools\D4Hub.VisionToolbox

python -m d4hub_vision_toolbox transmute `
  --image "C:\path\善变截图.png" `
  --config .\config\transmute.yaml `
  --out-dir "..\..\.artifacts\vision-toolbox"

python -m d4hub_vision_toolbox template `
  --image "C:\path\screen.png" `
  --template "C:\path\button.png" `
  --threshold 0.80 `
  --all `
  --out-dir "..\..\.artifacts\vision-toolbox"

python -m d4hub_vision_toolbox loot `
  --image "C:\path\screen.png" `
  --roi 0,0,1920,1080 `
  --out-dir "..\..\.artifacts\vision-toolbox"

python -m d4hub_vision_toolbox gold-road `
  --image "C:\path\screen.png" `
  --template-dir "C:\path\yellow-door-templates" `
  --threshold 0.80 `
  --out-dir "..\..\.artifacts\vision-toolbox"
```

每次运行都会写出 `<stem>.<detector>.json` 和 `<stem>.<detector>.png`。JSON 只保存本地输入的 SHA-256、尺寸、参数和识别结果，不上传截图。

## 上游整理

本工具箱吸收了两个上游仓库中的静态图像识别思路：`D4_vrs` 的背包网格/善变分类和 `Diablo-IV-auto` 的模板匹配/掉落颜色掩膜。上游没有提供独立许可证文件，因此当前采用“来源记录 + 独立适配实现”，没有把上游自动化代码、录制轨迹、模板资产或 Tesseract 数据打包进 D4Hub。映射记录见 `docs/product/2026-08-06-vision-toolbox.md`。

## 安全边界

本工具箱明确不包含以下能力：`pydirectinput`、`pyautogui`、`keyboard`、`mouse`、窗口激活、点击、按键、路径回放、组队状态机、自动拾取和自动刷图。自动化仓库中的这些模块不属于 D4Hub 的识别层。
