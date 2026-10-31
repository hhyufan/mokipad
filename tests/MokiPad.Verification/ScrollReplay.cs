using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MokiPad.Controls;
using MokiPad.Core;

namespace MokiPad.Verification;

internal static class ScrollReplay
{
    internal sealed record Result(double MaxBacktrackPx, double MaxCachedBacktrackPx, double MaxForwardExcessPx, IReadOnlyList<object> Runs, IReadOnlyList<object> Backtracks, IReadOnlyList<object> Excesses, int Realized);

    internal static async Task<Result> Run(MainWindow window, string output)
    {
        var path = System.IO.Path.Combine(output, "scroll-mixed.md");
        var sample = string.Concat(Enumerable.Range(0, 70).Select(i => $"## Section {i}\n\n" +
            string.Concat(Enumerable.Repeat("Variable paragraph height with inline `code` and words. ", 3 + i % 15)) +
            "\n\n> Quote one  \n> Quote two\n\n```javascript\n" + string.Concat(Enumerable.Repeat("console.log('mixed height');\n", 2 + i % 25)) + "```\n\n"));
        await System.IO.File.WriteAllTextAsync(path, sample);
        await window.OpenFileAsync(path); await window.RenderAsync();
        var root = window.VerificationSurface; Layout(root);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(root);
        var reader = Descendants(root).OfType<MarkdownPreview>().Single();
        var scroll = Descendants(reader).OfType<ScrollViewer>().First();
        var tickMethod = typeof(MarkdownPreview).GetMethod("AdvanceScroll", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tick = (EventHandler)tickMethod.CreateDelegate(typeof(EventHandler), reader);
        var lastTick = typeof(MarkdownPreview).GetField("lastScrollTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var active = typeof(MarkdownPreview).GetField("scrolling", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var remaining = typeof(MarkdownPreview).GetField("remainingScroll", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var records = new List<object>(); var excesses = new List<object>(); double worst = 0; double worstCached = 0; double worstExcess = 0; var runs = new List<object>();
        // Inspect document identities, never recycled container identities.
        Dictionary<PreviewBlock, (double Y, double Height)> Positions() => Descendants(reader).OfType<ListBoxItem>()
            .Where(item => item.DataContext is PreviewBlock)
            .ToDictionary(item => (PreviewBlock)item.DataContext,
                item => (item.TransformToAncestor(reader).Transform(new Point()).Y, item.ActualHeight));
        try
        {
            reader.Navigate(reader.Items.Count / 2); Layout(root);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(root);
            foreach (var mode in new[] { "up", "small-up", "down", "small-down", "reversals", "late-height" })
            {
                reader.Navigate(reader.Items.Count / 2); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(root);
                var small = mode.StartsWith("small", StringComparison.Ordinal) || mode == "reversals";
                var start = scroll.VerticalOffset;
                var changedHeight = false;
                var direction = mode.Contains("down", StringComparison.Ordinal) ? -1 : 1;
                var travel = 0.0;
                var previous = Positions();
                for (var frame = 0; frame < 360; frame++)
                {
                    if ((small && frame % 2 == 0) || (!small && frame % 18 == 0))
                    {
                        if (mode == "reversals") direction = (frame / 26) % 2 == 0 ? 1 : -1;
                        var delta = direction * (small ? new[] { 3, 7, 15, 24, 9, 1, 18, 5 }[(frame / 2) % 8] : 120);
                        reader.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, frame, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                        // Drive the real animation callback with deterministic frame times offscreen.
                        CompositionTarget.Rendering -= tick;
                    }
                    if (mode == "late-height" && frame == 60)
                    {
                        var above = Descendants(reader).OfType<ListBoxItem>().LastOrDefault(item => item.TransformToAncestor(reader).Transform(new Point()).Y + item.ActualHeight < 0);
                        if (above is not null) { above.Height = above.ActualHeight + 160; changedHeight = true; }
                    }
                    var seconds = small ? new[] { .008, .016, .005, .032, .010, .004 }[frame % 6] : .016;
                    var distanceBefore = (double)remaining.GetValue(reader)!;
                    lastTick.SetValue(reader, Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * seconds));
                    if ((bool)active.GetValue(reader)!) tick(null, EventArgs.Empty);
                    var requestedStep = Math.Abs(distanceBefore - (double)remaining.GetValue(reader)!);
                    await Dispatcher.Yield(DispatcherPriority.Background); Layout(root);
                    var current = Positions(); var backward = 0.0; var motion = new List<double>(); var forward = 0.0;
                    foreach (var (block, position) in current)
                        if (previous.TryGetValue(block, out var old))
                        {
                            worstCached = Math.Max(worstCached, direction * (old.Y - position.Y));
                            if (old.Y + old.Height > 0 && old.Y < reader.ActualHeight && position.Y + position.Height > 0 && position.Y < reader.ActualHeight)
                            {
                                backward = Math.Max(backward, direction * (old.Y - position.Y));
                                forward = Math.Max(forward, direction * (position.Y - old.Y));
                                motion.Add(position.Y - old.Y);
                            }
                        }
                    if (motion.Count > 0) { motion.Sort(); travel += Math.Abs(motion[motion.Count / 2]); }
                    var excess = forward - requestedStep;
                    if (excess > 1) excesses.Add(new { mode, frame, requestedStep, forward, excess, offset = scroll.VerticalOffset, extent = scroll.ExtentHeight });
                    worstExcess = Math.Max(worstExcess, excess);
                    if (backward > .5) records.Add(new { mode, frame, backward, offset = scroll.VerticalOffset, target = reader.ScrollTargetOffset, extent = scroll.ExtentHeight });
                    worst = Math.Max(worst, backward); previous = current;
                    if (frame % 30 == 0) { await Task.Delay(1); Layout(root); }
                }
                reader.FinishScroll(); await Dispatcher.Yield(DispatcherPriority.Background); Layout(root);
                if (start < 1000 || travel < 100) throw new InvalidOperationException("Replay must actually move visible document content");
                if (mode == "late-height" && !changedHeight) throw new InvalidOperationException("The delayed-height scenario must actually resize a cached item");
                runs.Add(new { mode, start, end = scroll.VerticalOffset, viewportTravelPx = travel, changedHeight });
            }
            return new(worst, worstCached, worstExcess, runs, records, excesses, reader.RealizedCount);
        }
        finally { reader.FinishScroll(); Layout(root); }
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1280, 820)); root.Arrange(new Rect(0, 0, 1280, 820)); root.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
