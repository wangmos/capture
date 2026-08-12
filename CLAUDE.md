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

The csproj pins `RuntimeIdentifier=win-x64` with `AppendRuntimeIdentifierToOutputPath=false`. Do not remove it: `Microsoft.ML.OnnxRuntime` ships every platform's native library, and without the RID the output directory grows by ~250MB (Android `.aar`, Apple `.xcframework.zip`, Linux `.so`). Keeping the RID out of the output path is what lets the test scripts hardcode the exe path.

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
- The toolbar is **right-anchored and content-sized**: adding/removing a button moves every button to its *left*. Do not hardcode button coordinates — `ToolbarControl.LogButtonRects` writes a `ToolbarRects Rectangle=x,y … TextSelect=x,y undo=x,y ocr=x,y …` line (physical px, deduplicated, re-emitted whenever the layout moves) and the scripts parse it. `test_number.ps1` / `test_textselect.ps1` show the pattern with a `BtnXY` helper. A blind misclick outside the toolbar expands the selection, so never guess.
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

**Toolbar** (`Toolbar/ToolbarControl.xaml.cs`) is built in code, not XAML: 7 tool toggles + undo, the 取字 toggle, OCR/pin/save/copy/exit, with a style sub-panel (color/thickness/font size/mosaic radius) rebuilt only when a state key changes. Exactly one overlay window shows it — the one containing the selection's bottom-right corner (`RefreshChrome`); `PlaceToolbar` flips it above the selection when it would fall off-screen.

**OCR** (`Ocr/`) — PP-OCRv6 **small** running on ONNX Runtime; there is no Paddle runtime and no Windows.Media.Ocr fallback any more (both were removed deliberately — see git history if you need them back).

Pipeline: `OcrService.Recognize` → `TextDetector` (DB, input `[1,3,H,W]`, 32-aligned, scaled by **total pixels** — capping the long side instead squashes a long screenshot like 645×5671 down to 174×1536 and detects nothing — and short images upscaled) → `DbPostProcessor` (own implementation: flood-fill connected components → convex hull + rotating-caliper min-area rect → `d = area·ratio/perimeter` expansion, replacing PaddleOCR's Clipper offset) → `TextRecognizer` (crop each quad to height 48, width-sorted batches of 8, CTC greedy). Thresholds come from the model's own `inference.yml`: `thresh 0.2` / `box_thresh 0.45` / `unclip_ratio 1.4`.

Two things worth knowing before touching this code:

- **The charset comes from the rec model's `character` metadata**, not the `.txt` (which is only a fallback). 18708 dict entries → 18710 output classes = `blank` + dict + `space`; index 0 is CTC blank and the last index is a space. A mismatch here decodes to garbage, so never hardcode the count.
- **`OcrLine.Chars` carries per-character x-ranges**, reverse-derived from CTC timesteps (timestep ↔ input width is linear). That is what makes on-image text selection possible; keep it populated when changing the decoder.

**On-image text selection** (`Tool.TextSelect`, the I-beam toggle between undo and OCR) is the WeChat-style path: entering the mode runs one OCR on the *un-annotated* selection in the background (`CaptureSession.OnTextLayerRequested`), then `Ocr/TextLayer.cs` flattens the result into a single sequence of selectable characters in global px. That flattening is the design pivot — a cross-line selection becomes one `[a, b)` range, so hit-testing, word/line expansion, highlight rectangles and copy all fall out of it. While the mode is active **the selection is frozen** (`OnLeftDown` returns before the hit-tester runs), which is what removes the ambiguity between "drag to move the selection" and "drag to select text"; leave the mode to resize again. Double-click = word, triple-click = line, Ctrl+A = all, Ctrl+C / the copy button = copy text and end the session. The layer is rebuilt if the selection changed since it was built (`_textLayerSource`).

`OcrService.RecognizeFixedRegion` is the rec-only path (no detection) and single-line-looking selections (`h ≤ 64 && w ≥ 4h`) take it automatically. Dark captures (avg luma < 110) are inverted first. Models live in `Models/` and are copied to the output; `OcrService.Warmup()` fires when a capture session starts so the first recognition doesn't pay model load.

**Settings** are JSON at `%AppData%\WeCapture\settings.json` (`Core/AppSettings.cs`: hotkey, autostart, last save dir); the global hotkey is registered on an `HWND_MESSAGE` window in `Hotkey/HotkeyManager.cs` and re-registered live from the settings window.

**Long-shot / scrolling capture** (`LongShot/`) is app-driven, never user-driven — the old "follow the user's manual scroll" version was deleted because fast scrolling leaves consecutive frames with no overlap, which cannot be located and silently mis-stitches. Do not restore it.

`LongShotRunner` hides the overlays (the stitcher needs the *live* screen, not the frozen session capture), then loops: send N wheel notches → `CaptureStableAsync` polls until two consecutive captures are identical → `ScrollStitcher.AddFrame`. It never assumes a pixel step: it divides the measured delta by the notches sent to learn that app's pixels-per-notch (EMA) and re-aims each step at 45% of the viewport. `LowConfidence` rolls the wheel back, halves the step and retries; after 3 retries it fails with a message rather than emitting a wrong image.

`ScrollStitcher` locates each frame in two stages — row signatures (32 luma buckets per row) over candidate offsets, then a score combining absolute cost with peak sharpness so that a blank region, where every offset "matches", scores low. The match band shrinks adaptively with the offset; a fixed band caps the maximum detectable scroll and was the bug that made large steps fail. Rows that stay identical across frames at the top/bottom are treated as fixed header/footer and excluded.

Both halves are covered by probes in the scratchpad `ocrprobe` project: `stitch` replays synthetic scroll frames (asserting pixel-exact reconstruction) and `verify <png>` OCRs a real stitched image and checks line numbers for duplicates plus a linear line-number↔y fit, which is what actually distinguishes "OCR missed a row" from "the stitcher dropped a band".
