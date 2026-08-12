using System.Windows.Media.Imaging;
using WeCapture.Core;

namespace WeCapture.LongShot;

public sealed record LongShotOutcome(bool Success, BitmapSource? Image, int Height, string Message);

/// <summary>
/// 长截图执行器：应用自己驱动滚动，逐帧定位并拼接。
///
/// 关键点是"步长自适应"——每个应用一格滚轮滚多少像素都不一样（还受系统设置影响），
/// 所以不预设像素步长，而是由拼接器反馈的实际位移反推每格像素数，再调整下一步的格数，
/// 使每步位移稳定在视口高度的 45% 左右（重叠足够多，又不至于太慢）。
///
/// 任何一步定位不可信，就回滚一步、减半步长重试；重试仍失败则整体失败并明确报错，
/// 绝不输出一张静默拼错的图。
/// </summary>
internal static class LongShotRunner
{
    private const double TargetStepFraction = 0.45;   // 每步期望位移 / 视口高度
    private const int MaxNotches = 15;
    private const int MaxFrames = 300;
    private const int MaxTotalHeight = 30000;
    private const int StableTimeoutMs = 700;
    private const int StablePollMs = 40;

    public static async Task<LongShotOutcome> RunAsync(
        RectI region, IProgress<string> progress, CancellationToken ct)
    {
        var stitcher = new ScrollStitcher(region.W, region.H);
        var center = new PointI(region.X + region.W / 2, region.Y + region.H / 2);

        var first = ScreenGrabber.CaptureRegion(region);
        stitcher.AddFrame(first);

        int notches = 3;                 // 起步保守，随后按实测调整
        double pixelsPerNotch = 0;       // 指数滑动平均
        int noMoveCount = 0;
        int retries = 0;
        bool recovering = false;

        TraceLog.Log($"LongShot start region={region}");

        for (int frame = 0; frame < MaxFrames; frame++)
        {
            ct.ThrowIfCancellationRequested();

            ScreenGrabber.SendWheel(center, -notches);   // 负数 = 向下滚
            var shot = await CaptureStableAsync(region, ct);
            var step = stitcher.AddFrame(shot);

            switch (step.Status)
            {
                case StitchStatus.Appended:
                    noMoveCount = 0;
                    retries = 0;
                    recovering = false;

                    // 反推每格像素数并调整下一步（EMA 抑制抖动）
                    double perNotch = (double)step.Delta / Math.Max(1, notches);
                    pixelsPerNotch = pixelsPerNotch <= 0 ? perNotch : pixelsPerNotch * 0.7 + perNotch * 0.3;
                    int target = (int)(region.H * TargetStepFraction);
                    notches = Math.Clamp((int)Math.Round(target / Math.Max(1, pixelsPerNotch)), 1, MaxNotches);

                    progress.Report($"已拼接 {stitcher.TotalHeight} px");
                    break;

                case StitchStatus.NoMovement:
                    if (recovering)
                    {
                        // 回滚后本就应该没位移，不算"到底"
                        recovering = false;
                        break;
                    }
                    if (++noMoveCount >= 2)
                    {
                        TraceLog.Log($"LongShot bottom reached height={stitcher.TotalHeight}");
                        return Done(stitcher, region, "已到底部");
                    }
                    break;

                case StitchStatus.LowConfidence:
                    if (++retries > 3 || notches <= 1)
                    {
                        TraceLog.Log($"LongShot FAILED score={step.Score:0.000} height={stitcher.TotalHeight}");
                        return new LongShotOutcome(false, null, stitcher.TotalHeight,
                            $"无法可靠定位滚动位置（匹配度 {step.Score:0.00}）。\n" +
                            "可能是页面内容在滚动中发生了变化，或该窗口不支持滚轮滚动。");
                    }

                    // 滚过头了：先滚回去把跳过的内容找回来，再用一半的步长重试
                    TraceLog.Log($"LongShot low confidence {step.Score:0.000}, rolling back {notches} notches");
                    progress.Report("重新定位…");
                    ScreenGrabber.SendWheel(center, notches);
                    await CaptureStableAsync(region, ct);
                    notches = Math.Max(1, notches / 2);
                    recovering = true;
                    break;
            }

            if (stitcher.TotalHeight >= MaxTotalHeight)
            {
                TraceLog.Log($"LongShot stopped at max height {stitcher.TotalHeight}");
                return Done(stitcher, region, "已达长度上限");
            }
        }

        return Done(stitcher, region, "已达帧数上限");
    }

    private static LongShotOutcome Done(ScrollStitcher stitcher, RectI region, string message)
    {
        var bmp = ScreenGrabber.ToBitmap(stitcher.ToBgra(), region.W, stitcher.TotalHeight);
        TraceLog.Log($"LongShot done height={stitcher.TotalHeight} header={stitcher.HeaderRows} " +
                     $"footer={stitcher.FooterRows} reason={message}");
        return new LongShotOutcome(true, bmp, stitcher.TotalHeight, message);
    }

    /// <summary>反复抓取直到连续两帧一致（平滑滚动/动画期结束），超时则用最后一帧。</summary>
    private static async Task<byte[]> CaptureStableAsync(RectI region, CancellationToken ct)
    {
        var prev = ScreenGrabber.CaptureRegion(region);
        int elapsed = 0;

        while (elapsed < StableTimeoutMs)
        {
            await Task.Delay(StablePollMs, ct);
            elapsed += StablePollMs;

            var cur = ScreenGrabber.CaptureRegion(region);
            if (ScreenGrabber.SameFrame(prev, cur)) return cur;
            prev = cur;
        }

        TraceLog.Log("LongShot frame did not settle, using last capture");
        return prev;
    }
}
