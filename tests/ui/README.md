# UI 驱动测试

本项目没有单元测试框架。行为由这些 PowerShell 脚本驱动真实进程来验证：
启动 `WeCapture.exe --capture`，用 `SendInput` 操作界面，再对 `%TEMP%\wec_log.txt`
的新增日志行和剪贴板内容做断言。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\ui\test_number.ps1
```

跑之前先 `taskkill /F /IM WeCapture.exe`——单实例机制会让第二个进程把信号转交给
常驻实例后自行退出，不杀掉的话测的是旧代码。

## 脚本

| 脚本 | 覆盖 |
|---|---|
| `test_number.ps1` | 标号工具、区域外点击扩展选区、撤销、双击复制 |
| `test_expand.ps1` | 选区外单击扩展、方向光标、画笔笔迹 |
| `test_reselect.ps1` | 选区外拖动 = 重新框选（清空标注） |
| `test_textselect.ps1` | 自动进入取字、手动开关、拖选文字、Ctrl+C 复制 |
| `test_shortcuts.ps1` | 默认快捷键（工具单字母 + Ctrl 组合） |
| `test_shortcut_custom.ps1` | 自定义快捷键的持久化与生效（会备份/还原真实设置） |
| `test_longshot.ps1` | 应用驱动滚动截屏与拼接 |
| `test_ocr_onnx.ps1` | OCR 全链路（模型加载、字典、识别） |
| `test_viewer_edit.ps1` | 查看窗：打开、选工具、画标注、复制烧录图 |
| `test_viewer_fixes.ps1` | 查看窗：窗口拖动、马赛克实时显示 |
| `shot_*.ps1` | 截图辅助，用于人工检查界面外观 |

## 约定与坑

- **脚本必须是纯 ASCII**：PowerShell 5.1 按 ANSI 读取 `.ps1`，中文要用 `[char]` 拼。
- **按钮坐标一律从日志读**，不要硬编码。工具条右对齐且按内容伸缩，新增一个按钮
  会让它左侧所有按钮平移；查看窗同理。应用会输出 `ToolbarRects …` 与
  `ViewerRects …` 两行坐标（物理像素），脚本从中解析。
- **`$x = @(TailLines '…')` 的 `@()` 不能省**：PowerShell 5.1 会把单元素结果拆成
  标量，`$x[-1]` 取到的会是字符串的最后一个字符。
- **不要在循环里调用 UIAutomation `FindAll`**：在这台机器上会卡住数分钟。
- **日志轮转**：日志超过 1MB 会在进程启动时轮转，`TailLines` 已处理"新文件比基线短"
  的情况。
- 脚本里的屏幕坐标按 3440×1440 主屏写死，换分辨率需要调整；有人同时在用这台机器时
  拖拽端点会抖动 ±1px，尺寸断言留了余量。
