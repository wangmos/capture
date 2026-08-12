# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

WeCapture — a WPF (.NET 10, `net10.0-windows10.0.19041.0`) Windows tray app that replicates the WeChat screenshot tool: global hotkey → freeze all screens → select region (with window/control hover snapping) → annotate → copy / save / pin / OCR. Single project, no solution file, no test project. UI strings and XML doc comments are Chinese; keep that convention when editing.

## Build & run

```bash
dotnet build D:/works/c++/capture/WeCapture.csproj
```

The exe lands at `bin/Debug/net10.0-windows10.0.19041.0/WeCapture.exe` (or `bin/Release/...`). Args:

- no args — resident tray icon only
- `--capture` — start a capture session immediately
- `--settings` — open the settings window

**A second process never runs.** `SingleInstance` (Mutex + named EventWaitHandles) makes instance #2 signal the resident instance (`--capture` → start a session) and exit. So after rebuilding, kill the old process first or you are testing stale code:

```bash
taskkill //F //IM WeCapture.exe
```

WinForms is referenced only for tray/screen APIs; `WeCapture.csproj` removes the implicit `System.Windows.Forms` / `System.Drawing` usings and `GlobalUsings.cs` pins `Application`/`MessageBox`/`Clipboard` to the WPF types. Fully qualify the WinForms/GDI+ counterparts.

## Testing (no unit tests — driven UI tests)

There is no test framework. Behavior is verified by PowerShell driver scripts in `C:\temp` (`test_number.ps1`, `test_expand.ps1`, `test_mosaic.ps1`, `test_reselect.ps1`, `test_ocr2.ps1`, …). The pattern each script follows:

1. snapshot the line count of `%TEMP%\wec_log.txt` (written by `Core/TraceLog.cs` — the app has no console, so this file is the only runtime signal);
2. `Start-Process WeCapture.exe --capture` *from inside the script* (launching `--capture` before the script starts causes `OVERLAY_TIMEOUT`);
3. poll the log tail for `OverlayWindow shown`, then drive the UI with `SendInput`/`SetCursorPos` P/Invoke at fixed screen coordinates;
4. assert on new log lines (`Number placed idx=…`, `Selection expanded to …`, `SetTool …`) and on clipboard image dimensions.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\temp\test_number.ps1'
```

Because the log lines are the test contract, do not rename or reformat existing `TraceLog.Log` messages without updating the scripts that grep them.

Gotchas learned the hard way on this machine:

- Scripts must be **pure ASCII** — PowerShell 5.1 reads `.ps1` as ANSI; emit Chinese via `[char]` codes.
- Never call UIAutomation `FindAll` in a loop here; it hangs for minutes crawling unresponsive desktop windows. Use fixed coordinates + log polling. (`GetCursorInfo` + `LoadCursor` handle comparison *is* safe and is how cursor-shape assertions are done.)
- The toolbar is **right-anchored and content-sized**: adding/removing a button moves every button to its *left*. Recalibrate coordinates after touching `ToolbarControl.BuildButtons`. A blind misclick outside the toolbar now expands the selection, so probe for the hand cursor before clicking.
- A human may be using this machine concurrently — drag endpoints jitter ±1px; allow ±2 on size assertions and retry on log lines rather than assuming a single attempt lands.

## Architecture

**One coordinate system is the truth: virtual-screen *physical* pixels**, expressed as `Core/RectI.cs` (`RectI`/`PointI`, origin may be negative). Every model, annotation and export coordinate is in that space. DIPs exist only at the WPF boundary — `OverlayWindow.ToGlobalPx`/`ToLocalDip` convert per monitor using that monitor's own DPI scale, and `AnnotationLayer` wraps itself in `ScaleTransform(1/dpiScale)` so all drawing code below it is in local physical pixels. The manifest declares PerMonitorV2.

**Session lifecycle** — `Session/CaptureSession.cs` is the singleton for one capture:
`MonitorSet.CaptureAll()` (per-monitor `CreateDC` + `BitBlt` with `CAPTUREBLT`, plus a BGRA buffer for O(1) color picking) → one `OverlayWindow` per monitor, each placed with `PlaceExactly` + `CorrectPlacement` (WPF rounds; the correction pass re-reads `GetWindowRect` and nudges) → interaction → export → `ExitAll` closes every window.

**Model / view split** — `Session/SessionModel.cs` holds the whole state machine (`UIState`, `Tool`, `DragMode` in `Session/UIState.cs`) and *all* interaction logic; it owns no WPF window. `OverlayWindow` is pure routing: it converts mouse/keyboard events to global px, calls the model, then paints. All overlay windows share one model and subscribe to its `Changed` event, so a selection spanning two monitors stays consistent. The model raises `CopyConfirmed` / `ExitRequested` / `TextEditRequested`; `CaptureSession` wires those and every toolbar event.

Routing details worth knowing before changing input code: `OverlayWindow.IsChrome` walks the visual tree and lets clicks on the toolbar/popup/textbox through untouched; WPF reports `ClickCount == 1` on ButtonUp, so double-click detection borrows `_downClickCount` from the Down event; text edit is committed before any other click is routed.

**Selection semantics** live in `SelectionHitTester` (8 handles > inside > outside) and `SessionModel.OnLeftDown/OnLeftUp`. Clicking *outside* an existing selection is deliberately ambiguous: `DragMode.ExpandPending` defers, a release without movement extends the nearest edge(s) to the click point (keeping annotations and the active tool), while dragging >4px converts to a fresh `NewSelect` that clears annotations — matching WeChat. The Number tool overrides this: an outside click places a badge and unions the selection with the badge bounds.

**Annotations** (`Annotations/`) subclass `Annotation` with `BoundsPx` + `Render(DrawingContext, in RenderEnv)`. `RenderEnv(Selection, Mosaic, PixelsPerDip)` is the only context they get, which is why the *same* render call serves both the live overlay (`Overlay/AnnotationLayer.OnRender`, clipped to selection ∩ monitor, translated by `-monitor.origin`) and the final image (`Export/ImageExporter.Render`, translated by `-selection.origin`, rendered into a 96-dpi `RenderTargetBitmap` so output pixels == selection pixels exactly). Add a new tool by adding an `Annotation` subclass, a `Tool` enum member, a toolbar entry, and the `FinishStroke`/`BuildPreviewAnnotation` cases.

Mosaic is special: `MosaicAnnotation` just clips and draws a full-selection pre-blocked bitmap, produced by `MosaicImageFactory` (16px nearest-neighbor blocks over the frozen BGRA buffers) and cached per-selection on `CaptureSession.GetMosaicImage()`. Change the selection and the cache rebuilds.

**Hover snapping** (`Overlay/HoverDetector.cs`) — `EnumWindows` in Z order for the topmost visible window containing the point, then `RealChildWindowFromPoint` drill-down; UIA `FromPoint` is used only to *refine* (accepted only if contained in the Win32 rect), all exceptions silently degrade. Candidates must be stable 60 ms before committing; a 50 ms dispatcher timer keeps detection advancing while the mouse is still.

**Toolbar** (`Toolbar/ToolbarControl.xaml.cs`) is built in code, not XAML: 7 tool toggles + undo/OCR/pin/save/copy/exit, with a style sub-panel (color/thickness/font size/mosaic radius) rebuilt only when a state key changes. Exactly one overlay window shows it — the one containing the selection's bottom-right corner (`RefreshChrome`); `PlaceToolbar` flips it above the selection when it would fall off-screen.

**OCR** (`Ocr/OcrService.cs`) — PaddleOCR (`PaddleOCRSharp` + `Paddle.Runtime.win_x64`, PP-OCRv5, lazily created engine) is the primary path; any failure or empty result falls back to Windows.Media.Ocr with multiple language engines scored by chars-per-word. Dark-theme captures (avg luma < 110) are inverted and small images upscaled 2× before recognition.

**Settings** are JSON at `%AppData%\WeCapture\settings.json` (`Core/AppSettings.cs`: hotkey, autostart, last save dir); the global hotkey is registered on an `HWND_MESSAGE` window in `Hotkey/HotkeyManager.cs` and re-registered live from the settings window.

Removed on purpose: the long-shot / scrolling-capture feature was deleted at the user's request. Do not reintroduce it unless asked.
