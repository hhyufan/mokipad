using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MokiPad.Controls;
using MokiPad.Core;
using MokiPad.Services;

namespace MokiPad.Verification;

internal static class NativeScrollReplay
{
    internal sealed record RunResult(string File, string Mode, int Frames, double MaxForwardExcessPx,
        double MaxFrameStepPx, double FrameBudgetPx, double RequestedPixels, double VisibleTravelPixels, IReadOnlyList<object> Traces);

    internal static async Task<IReadOnlyList<RunResult>> Run(string output)
    {
        var window = new MainWindow(new Preferences { Background = false, Language = "zh" });
        var root = window.VerificationSurface;
        var content = window.Content;
        if (content is FrameworkElement element) element.UseLayoutRounding = window.UseLayoutRounding;
        window.Content = null;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        // Exercise WPF's actual presentation/layout queue, without showing a window on the desktop.
        using var source = new HwndSource(new HwndSourceParameters("MokiPad offscreen scroll verification")
        {
            Width = 1280, Height = 820, PositionX = -30000, PositionY = -30000,
            WindowStyle = unchecked((int)0x90000000), ExtendedWindowStyle = 0x80
        });
        source.RootVisual = (Visual)content;
        await Task.Delay(100);
        var remaining = typeof(MarkdownPreview).GetField("remainingScroll", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var active = typeof(MarkdownPreview).GetField("scrolling", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var runs = new List<RunResult>();
        try
        {
            foreach (var file in new[] { System.IO.Path.Combine(output, "scroll-mixed.md"), @"C:\Code\React\mokipad\docs\code-cleanup-changes.md" })
            {
                await window.OpenFileAsync(file); await window.RenderAsync();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var reader = Descendants(root).OfType<MarkdownPreview>().Single();
                var scroll = Descendants(reader).OfType<ScrollViewer>().First();
                foreach (var mode in new[] { "normal", "burst", "stalled-burst" })
                {
                    var burst = mode != "normal";
                    reader.Navigate(reader.Items.Count / 2);
                    await Task.Delay(150);
                    var previous = Positions(reader); var budget = Math.Abs((double)remaining.GetValue(reader)!);
                    var input = 0.0; var totalInput = 0.0; var totalTravel = 0.0;
                    var traces = new List<object>(); var worst = 0.0; var maxFrameStep = 0.0; var frames = 0; var lastTime = TimeSpan.MinValue;
                    void Record(object? sender, EventArgs args)
                    {
                        var time = ((RenderingEventArgs)args).RenderingTime;
                        if (time == lastTime) return;
                        lastTime = time; frames++;
                        var nextBudget = Math.Abs((double)remaining.GetValue(reader)!);
                        var requested = budget + input - nextBudget; input = 0; budget = nextBudget;
                        var positions = Positions(reader); var distances = new List<double>();
                        foreach (var (block, position) in positions)
                            if (previous.TryGetValue(block, out var old) && old.Y + old.Height > 0 && old.Y < reader.ActualHeight &&
                                position.Y + position.Height > 0 && position.Y < reader.ActualHeight)
                                distances.Add(position.Y - old.Y);
                        if (distances.Count > 0)
                        {
                            distances.Sort(); var moved = distances[distances.Count / 2]; var excess = moved - requested;
                            maxFrameStep = Math.Max(maxFrameStep, Math.Abs(moved));
                            totalTravel += moved; worst = Math.Max(worst, excess);
                            if (excess > 2) traces.Add(new { frames, requested, moved, excess, offset = scroll.VerticalOffset, extent = scroll.ExtentHeight });
                        }
                        previous = positions;
                    }
                    CompositionTarget.Rendering += Record;
                    try
                    {
                        for (var i = 0; i < 25; i++)
                        {
                            var distance = SystemParameters.WheelScrollLines < 0 ? scroll.ViewportHeight * .9 :
                                Math.Clamp(SystemParameters.WheelScrollLines, 1, 8) * (double)reader.GetValue(MarkdownPreview.ReadingFontSizeProperty) * 1.75 * 1.25;
                            input += distance; totalInput += distance;
                            reader.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, i, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                            if (mode == "stalled-burst" && i % 8 == 3) Thread.Sleep(110);
                            await Task.Delay(burst ? new[] { 1, 1, 1, 75, 1 }[i % 5] : 70);
                        }
                        var settle = Stopwatch.StartNew();
                        while ((bool)active.GetValue(reader)! && settle.ElapsedMilliseconds < 2500) await Task.Delay(16);
                        if ((bool)active.GetValue(reader)!) throw new InvalidOperationException("Scrolling did not settle after the wheel burst");
                        await Task.Delay(50); // Include the last queued scroll/layout in presented positions.
                    }
                    finally { CompositionTarget.Rendering -= Record; reader.FinishScroll(); }
                    runs.Add(new(System.IO.Path.GetFileName(file), mode, frames, worst, maxFrameStep,
                        Math.Clamp(scroll.ViewportHeight * .12, 48, 96), totalInput, totalTravel, traces));
                }
            }
        }
        finally
        {
            source.RootVisual = null;
            window.Content = content;
            window.EndVerification();
            window.Close();
        }
        return runs;
    }

    private static Dictionary<PreviewBlock, (double Y, double Height)> Positions(MarkdownPreview reader) =>
        Descendants(reader).OfType<ListBoxItem>().Where(item => item.DataContext is PreviewBlock).ToDictionary(
            item => (PreviewBlock)item.DataContext, item => (item.TransformToAncestor(reader).Transform(new Point()).Y, item.ActualHeight));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
