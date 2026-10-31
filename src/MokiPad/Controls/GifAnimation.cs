using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

namespace MokiPad.Controls;

/// <summary>
/// 在阅读器里播放 GIF。GIF 的解码放在后台线程（SkiaSharp），
/// 解码结果写入同一个 <see cref="WriteableBitmap"/>，再交给界面线程显示。
/// 因为始终是同一张位图，每一帧都不会触发布局，所以不会把阅读位置顶来顶去。
/// </summary>
internal sealed class GifAnimation : IDisposable
{
    /// <summary>
    /// 附加依赖属性：把“这个 Image 正在播放的动画对象”挂在 Image 上。
    /// 这样从任意一个 Image 都能找回它的动画，用来在离开可视区域时暂停。
    /// </summary>
    private static readonly DependencyProperty InstanceProperty =
        DependencyProperty.RegisterAttached("Instance", typeof(GifAnimation), typeof(GifAnimation));

    private readonly Image target;
    private readonly byte[] bytes;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? playback;
    private WriteableBitmap? surface;
    private bool disposed;

    /// <summary>GIF 总帧数（至少为 1）。</summary>
    private int FrameCount { get; set; }

    /// <summary>当前显示的帧序号，从 0 开始。</summary>
    public int CurrentFrame { get; private set; }

    /// <summary>从 Image 上取回它正在使用的动画对象；没有则返回 <c>null</c>。</summary>
    internal static GifAnimation? For(Image image) => image.GetValue(InstanceProperty) as GifAnimation;

    /// <summary>创建一个播放器，并把自身挂到目标 Image 上。</summary>
    public GifAnimation(Image target, byte[] bytes)
    {
        this.target = target;
        this.bytes = bytes;
        target.SetValue(InstanceProperty, this);
    }

    /// <summary>
    /// 开始播放。第一次调用会启动后台解码任务；返回的 Task 在首帧准备好后完成，
    /// 这样调用方可以等“第一帧已经显示”再继续布局或截图。
    /// </summary>
    public Task StartAsync()
    {
        playback ??= Task.Run(DecodeAsync);
        return ready.Task;
    }

    /// <summary>停止播放并释放取消令牌；目标 Image 上的附加属性也会被清除。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();

        if (For(target) == this) target.ClearValue(InstanceProperty);

        // 解码线程可能还在使用 Skia 的原生缓冲区，等它结束后再释放取消令牌。
        if (playback is not null) _ = playback.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        else cancellation.Dispose();
    }

    /// <summary>
    /// 后台解码循环：用 SkiaSharp 解出一帧 BGRA 像素，然后通过 Dispatcher 写进 WPF 位图。
    /// </summary>
    private async Task DecodeAsync()
    {
        var token = cancellation.Token;

        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Invalid GIF");

            FrameCount = Math.Max(1, codec.FrameCount);

            var width = codec.Info.Width;
            var height = codec.Info.Height;

            // 防止一个超大 GIF 把内存吃光：宽 × 高超过 6400 万像素就拒绝。
            if ((long)width * height > 64_000_000)
                throw new InvalidDataException("GIF dimensions exceed the decoding budget");

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKBitmap(info);
            canvas.Erase(SKColors.Transparent);

            var frames = codec.FrameInfo;

            // 创建 WPF 位图属于界面对象操作，必须回到 Image 所在的 Dispatcher 线程。
            await target.Dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();

                // Pbgra32 是 WPF 最常用的预乘透明度像素格式，和 Skia 的 BGRA 输出兼容。
                surface = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
                target.Source = surface;

                // 让缩放时使用高质量插值，避免大 GIF 缩小时出现锯齿。
                RenderOptions.SetBitmapScalingMode(target, BitmapScalingMode.HighQuality);
            }, DispatcherPriority.Background, token);

            var previous = -1;
            var index = 0;

            while (!token.IsCancellationRequested)
            {
                // 首帧完成后才检查可见性；不可见时暂停解码，节省处理器。
                if (ready.Task.IsCompleted)
                {
                    var visible = await target.Dispatcher.InvokeAsync(InReadingViewport,
                        DispatcherPriority.Background, token);
                    if (!visible)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }
                }

                var started = Stopwatch.GetTimestamp();

                // GIF 的“恢复上一帧”处置方式不能简单用上一帧做合成基底，
                // 否则透明区域会越叠越脏；这里只把普通处置的上一帧交给解码器。
                var prior = previous >= 0 && frames.Length > previous &&
                            frames[previous].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious
                    ? previous
                    : -1;

                var result = codec.GetPixels(info, canvas.GetPixels(), canvas.RowBytes,
                    new SKCodecOptions(index, prior));

                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                    throw new InvalidDataException("GIF decoding failed: " + result);

                await target.Dispatcher.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();

                    // WritePixels 直接更新位图内容，不更换 Source，因此不会重新测量/布局。
                    surface!.WritePixels(new Int32Rect(0, 0, width, height),
                        canvas.GetPixels(), canvas.ByteCount, canvas.RowBytes);

                    CurrentFrame = index;
                    ready.TrySetResult(true);
                }, DispatcherPriority.Background, token);

                if (FrameCount == 1) return;

                var delay = frames.Length > index && frames[index].Duration > 0
                    ? frames[index].Duration
                    : 100;

                previous = index;
                index = (index + 1) % FrameCount;

                // 循环回第一帧时清空画布，防止上一轮的残留像素。
                if (index == 0)
                {
                    previous = -1;
                    canvas.Erase(SKColors.Transparent);
                }

                // 扣除本帧解码和派发已经花掉的时间，尽量贴近 GIF 原始帧率。
                var remaining = delay - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), token);
            }
        }
        catch (OperationCanceledException)
        {
            ready.TrySetCanceled();
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
        }
    }

    /// <summary>
    /// 判断目标图片是否还在阅读器的可视范围内。
    /// TransformToAncestor 把图片坐标换算成阅读器坐标，再和阅读器矩形求交。
    /// </summary>
    private bool InReadingViewport()
    {
        var reading = target.FindParent<MarkdownPreview>();
        if (reading is null || target.ActualHeight == 0) return true;

        var bounds = target.TransformToAncestor(reading).TransformBounds(new Rect(target.RenderSize));
        return bounds.IntersectsWith(new Rect(0, 0, reading.ActualWidth, reading.ActualHeight));
    }
}
