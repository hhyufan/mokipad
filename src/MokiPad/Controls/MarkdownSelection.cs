using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MokiPad.Core;
using MokiPad.Services;

namespace MokiPad.Controls;

internal sealed record CopyFragment(SourceRange Range, bool Atomic = false);

internal static class MarkdownSource
{
    private static readonly DependencyProperty FragmentProperty = DependencyProperty.RegisterAttached("Fragment", typeof(CopyFragment), typeof(MarkdownSource));
    internal static CopyFragment? Get(DependencyObject target) => target.GetValue(FragmentProperty) as CopyFragment;
    internal static void Bind(DependencyObject target, int start, int end, bool atomic = false)
    {
        if (start < 0 || end <= start) return;
        var fragment = new CopyFragment(new(start, end), atomic); target.SetValue(FragmentProperty, fragment);
        if (target is InlineUIContainer { Child: { } child }) child.SetValue(FragmentProperty, fragment);
    }
}

public sealed partial class MarkdownPreview
{
    private IReadOnlyList<PreviewBlock> sourceBlocks = [];
    private string documentSource = "";
    private ReadingPosition anchor, focus;
    private bool selectAll, selectionPresent, dragging, pressed;
    private Point pressPoint;
    private ReadingPosition? shiftAnchor;
    private SelectionHit? initialHit;
    private SelectionAdorner? selectionAdorner;
    private DispatcherTimer? dragScroll;
    private int selectionVersion, lastLayoutStamp;
    internal bool HasMarkdownSelection => selectionPresent;
    internal event Action? CopyFailed;

    private sealed record SelectionHit(int Index, int Start, int End, bool Atomic, bool Link = false)
    {
        public ReadingPosition Before => new(Index, Start);
        public ReadingPosition After => new(Index, End);
    }

    private void InitializeSelection()
    {
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(SelectionMouseDown), true);
        AddHandler(PreviewMouseMoveEvent, new MouseEventHandler(SelectionMouseMove), true);
        AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(SelectionMouseUp), true);
        PreviewMouseRightButtonDown += (_, e) =>
        {
            var hit = HitSelection(e.GetPosition(this));
            if (hit is not null && !Contains(hit.Before)) ClearMarkdownSelection();
        };
        LostMouseCapture += (_, e) => { if (ReferenceEquals(e.OriginalSource, this) && !IsMouseCaptured) EndDrag(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && HasMarkdownSelection) { ClearMarkdownSelection(); e.Handled = true; } };
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { CopySelection(); e.Handled = true; }, (_, e) => e.CanExecute = HasMarkdownSelection));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, e) => { SelectAllMarkdown(); e.Handled = true; }, (_, e) => e.CanExecute = sourceBlocks.Count > 0));
        LayoutUpdated += (_, _) => RefreshSelectionVisual();
        Unloaded += (_, _) => EndDrag();
    }

    internal void SelectRange(ReadingPosition start, ReadingPosition end, bool entireDocument = false)
    {
        if (anchor == start && focus == end && selectAll == entireDocument) return;
        anchor = start; focus = end; selectAll = entireDocument;
        selectionPresent = entireDocument || start != end;
        selectionVersion++;
        EnsureSelectionAdorner(); selectionAdorner?.InvalidateVisual();
        CommandManager.InvalidateRequerySuggested();
    }

    public void SelectAllMarkdown()
    {
        if (sourceBlocks.Count > 0) SelectRange(new(0, 0), new(sourceBlocks.Count - 1, documentSource.Length), true);
    }

    public void ClearMarkdownSelection() => SelectRange(default, default);
    public string GetSelectedMarkdown() => selectionPresent ? MarkdownCopy.Selection(documentSource, sourceBlocks, anchor, focus, selectAll) : "";
    public string GetBlockMarkdown(PreviewBlock block) => MarkdownCopy.Block(documentSource, block);
    public bool CopySelection() => selectionPresent && WriteMarkdown(GetSelectedMarkdown());
    public bool CopyBlock(PreviewBlock block) => WriteMarkdown(GetBlockMarkdown(block));
    private bool WriteMarkdown(string value)
    {
        if (value.Length == 0) return false;
        if (MarkdownClipboard.Write(value)) return true;
        CopyFailed?.Invoke(); return false;
    }

    private void SelectionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindParent<ButtonBase>() is not null ||
            (e.OriginalSource as DependencyObject)?.FindParent<ScrollBar>() is not null) return;
        if (BeginSelection(e.GetPosition(this), Keyboard.Modifiers, e.ClickCount)) e.Handled = true;
    }

    internal bool BeginSelection(Point point, ModifierKeys modifiers = ModifierKeys.None, int clicks = 1)
    {
        var hit = HitSelection(point); if (hit is null) return false;
        StopScrolling(); initialHit = hit; pressPoint = point; pressed = true;
        shiftAnchor = (modifiers & ModifierKeys.Shift) != 0 && selectionPresent ? anchor : null;
        if ((modifiers & ModifierKeys.Shift) != 0 && selectionPresent)
            SelectRange(anchor, MarkdownCopy.Compare(hit.Before, anchor) < 0 ? hit.Before : hit.After);
        else if (clicks >= 3)
            SelectRange(new(hit.Index, sourceBlocks[hit.Index].CopyStart), new(hit.Index, sourceBlocks[hit.Index].CopyEnd));
        else if (clicks == 2 && !hit.Atomic)
        {
            var start = hit.Start; var end = hit.End;
            while (start > sourceBlocks[hit.Index].CopyStart && IsWord(documentSource[start - 1])) start--;
            while (end < sourceBlocks[hit.Index].CopyEnd && IsWord(documentSource[end])) end++;
            SelectRange(new(hit.Index, start), new(hit.Index, end));
        }
        else SelectRange(hit.Before, hit.After);
        // Normal link clicks still navigate; dragging over a link selects its source.
        return !hit.Link || clicks > 1;
    }

    private void SelectionMouseMove(object sender, MouseEventArgs e)
    {
        if (!pressed || e.LeftButton != MouseButtonState.Pressed || initialHit is null) return;
        var point = e.GetPosition(this);
        if (!dragging && (point - pressPoint).Length < SystemParameters.MinimumHorizontalDragDistance) return;
        dragging = true;
        if (!IsMouseCaptured) Mouse.Capture(this, CaptureMode.SubTree);
        DragSelectionTo(point); e.Handled = true;
        dragScroll ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(24) };
        dragScroll.Tick -= AutoScrollSelection; dragScroll.Tick += AutoScrollSelection;
        if (point.Y < 24 || point.Y > ActualHeight - 24) dragScroll.Start(); else dragScroll.Stop();
    }

    private void ExtendDrag(Point point)
    {
        if (initialHit is null || HitSelection(point) is not { } current) return;
        var backwards = MarkdownCopy.Compare(current.Before, initialHit.Before) < 0;
        if (shiftAnchor is { } fixedAnchor)
            SelectRange(fixedAnchor, MarkdownCopy.Compare(current.Before, fixedAnchor) < 0 ? current.Before : current.After);
        else SelectRange(backwards ? initialHit.After : initialHit.Before, backwards ? current.Before : current.After);
    }

    internal void DragSelectionTo(Point point) { dragging = true; ExtendDrag(point); }

    private void SelectionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!pressed) return;
        if (dragging) { ExtendDrag(e.GetPosition(this)); e.Handled = true; }
        else if (initialHit?.Link == true) ClearMarkdownSelection();
        EndDrag();
    }

    internal void EndDrag()
    {
        pressed = dragging = false; initialHit = null; shiftAnchor = null; dragScroll?.Stop();
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void AutoScrollSelection(object? sender, EventArgs e)
    {
        if (!dragging || Mouse.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        readingScroll ??= DescendantScroll(this); if (readingScroll is null) return;
        var point = Mouse.GetPosition(this);
        var distance = point.Y < 24 ? point.Y - 24 : point.Y > ActualHeight - 24 ? point.Y - (ActualHeight - 24) : 0;
        if (distance == 0) { dragScroll?.Stop(); return; }
        readingScroll.ScrollToVerticalOffset(readingScroll.VerticalOffset + Math.Clamp(distance, -72, 72));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ExtendDrag(Mouse.GetPosition(this))));
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' || char.IsSurrogate(c);

    private SelectionHit? HitSelection(Point point)
    {
        var target = InputHitTest(new Point(Math.Clamp(point.X, 0, Math.Max(0, ActualWidth - 1)), Math.Clamp(point.Y, 0, Math.Max(0, ActualHeight - 1)))) as DependencyObject;
        var presenter = target?.FindParent<BlockPresenter>();
        presenter ??= VisiblePresenters().Where(p => p.Block?.CopyEnd > p.Block?.CopyStart).MinBy(p =>
        {
            var bounds = p.TransformToAncestor(this).TransformBounds(new Rect(p.RenderSize));
            return point.Y < bounds.Top ? bounds.Top - point.Y : point.Y > bounds.Bottom ? point.Y - bounds.Bottom : 0;
        });
        if (presenter?.Block is not { } block || !MarkdownCopy.Range(block).Valid) return null;
        var container = presenter.FindParent<ListBoxItem>();
        if (container is null) return null;
        var index = ItemContainerGenerator.IndexFromContainer(container); if (index < 0) return null;
        if (MarkdownCopy.IsAtomic(block)) return new(index, block.CopyStart, block.CopyEnd, true);
        for (var node = target; node is not null && node != presenter; node = node is FrameworkContentElement element ? element.Parent : node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (MarkdownSource.Get(node) is { Atomic: true } atom) return new(index, atom.Range.Start, atom.Range.End, true, node is Hyperlink);
        var text = target?.FindParent<TextBlock>();
        if (text is null || !text.IsDescendantOf(presenter)) text = SelectionDescendants(presenter).OfType<TextBlock>()
            .Where(t => Fragments(t.Inlines).Any()).MinBy(t => Math.Abs(t.TransformToAncestor(this).Transform(new Point()).Y - point.Y));
        if (text is not null && text.GetPositionFromPoint(TranslatePoint(point, text), true) is { } position)
        {
            DependencyObject? node = position.Parent; CopyFragment? fragment = null; Run? run = null; var link = false;
            while (node is not null && node != text)
            {
                if (node is Hyperlink) link = true;
                if (node is Run value) run = value;
                if (MarkdownSource.Get(node) is { } candidate && (fragment is null || candidate.Atomic)) fragment = candidate;
                node = node is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(node);
            }
            if (fragment is { Atomic: true }) return new(index, fragment.Range.Start, fragment.Range.End, true, link);
            if (fragment is not null && run is not null)
            {
                var display = Math.Clamp(run.ContentStart.GetOffsetToPosition(position), 0, run.Text.Length);
                var offset = MarkdownCopy.SourceOffset(documentSource, fragment.Range, display);
                return new(index, offset, offset, false, link);
            }
        }
        // Embedded images/math have their own visual hit target.
        for (var node = target; node is not null && node != presenter; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (MarkdownSource.Get(node) is { } fragment) return new(index, fragment.Range.Start, fragment.Range.End, true);
        var relative = presenter.TransformToAncestor(this).TransformBounds(new Rect(presenter.RenderSize));
        var edge = point.Y <= relative.Top ? block.CopyStart : block.CopyEnd;
        return new(index, edge, edge, false);
    }

    internal ReadingPosition? HitPosition(Point point) => HitSelection(point)?.Before;
    internal void SelectAt(Point point)
    {
        if (HitSelection(point) is { } hit) SelectRange(hit.Before, hit.After);
    }

    private bool Contains(ReadingPosition position)
    {
        if (!selectionPresent) return false;
        if (selectAll) return true;
        var (start, end) = MarkdownCopy.Compare(anchor, focus) <= 0 ? (anchor, focus) : (focus, anchor);
        return MarkdownCopy.Compare(position, start) >= 0 && MarkdownCopy.Compare(position, end) <= 0;
    }

    private SourceRange SelectedRange(int index, PreviewBlock block)
    {
        if (!selectionPresent) return default;
        var whole = MarkdownCopy.Range(block); if (selectAll) return whole;
        var (start, end) = MarkdownCopy.Compare(anchor, focus) <= 0 ? (anchor, focus) : (focus, anchor);
        if (MarkdownCopy.IsAtomic(block) && new[] { start, end }.Any(p => p.Block >= 0 && p.Block < sourceBlocks.Count && MarkdownCopy.Range(sourceBlocks[p.Block]) == whole)) return whole;
        if (index < start.Block || index > end.Block) return default;
        if (MarkdownCopy.IsAtomic(block)) return whole;
        return new(Math.Max(whole.Start, index == start.Block ? start.Offset : whole.Start), Math.Min(whole.End, index == end.Block ? end.Offset : whole.End));
    }

    private void EnsureSelectionAdorner()
    {
        if (selectionAdorner is null && AdornerLayer.GetAdornerLayer(this) is { } layer)
        { selectionAdorner = new SelectionAdorner(this) { IsHitTestVisible = false }; layer.Add(selectionAdorner); }
    }

    private void RefreshSelectionVisual()
    {
        if (!selectionPresent) return;
        EnsureSelectionAdorner();
        var hash = new HashCode(); hash.Add(selectionVersion);
        foreach (var presenter in VisiblePresenters())
        {
            hash.Add(presenter.Block?.Offset); hash.Add(presenter.RenderSize);
            hash.Add(presenter.TransformToAncestor(this).Transform(new Point()));
        }
        var stamp = hash.ToHashCode();
        if (stamp == lastLayoutStamp) return;
        lastLayoutStamp = stamp;
        selectionAdorner?.InvalidateVisual();
    }

    private IEnumerable<BlockPresenter> VisiblePresenters() => SelectionDescendants(this).OfType<BlockPresenter>();
    internal static IEnumerable<DependencyObject> SelectionDescendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var node in SelectionDescendants(child)) yield return node; }
    }
    private static IEnumerable<Inline> Fragments(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        { if (MarkdownSource.Get(inline) is not null && inline is not Span) yield return inline; if (inline is Span span) foreach (var child in Fragments(span.Inlines)) yield return child; }
    }

    private sealed class SelectionAdorner(MarkdownPreview owner) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext drawing)
        {
            IsHitTestVisible = false;
            if (!owner.selectionPresent) return;
            var accent = WpfApp.Current.FindResource("Accent") as SolidColorBrush ?? Brushes.DodgerBlue;
            var brush = new SolidColorBrush(Color.FromArgb(58, accent.Color.R, accent.Color.G, accent.Color.B));
            drawing.PushClip(new RectangleGeometry(new Rect(owner.RenderSize)));
            foreach (var presenter in owner.VisiblePresenters())
            {
                if (presenter.Block is not { } block || presenter.FindParent<ListBoxItem>() is not { } container) continue;
                var selected = owner.SelectedRange(owner.ItemContainerGenerator.IndexFromContainer(container), block);
                if (!selected.Valid) continue;
                if (MarkdownCopy.IsAtomic(block) || selected == MarkdownCopy.Range(block))
                {
                    if (presenter.Content is FrameworkElement content)
                        drawing.DrawRoundedRectangle(brush, null, content.TransformToAncestor(owner).TransformBounds(new Rect(content.RenderSize)), 3, 3);
                    continue;
                }
                foreach (var text in SelectionDescendants(presenter).OfType<TextBlock>())
                    foreach (var inline in Fragments(text.Inlines))
                    {
                        var fragment = MarkdownSource.Get(inline)!;
                        if (!fragment.Range.Intersects(selected)) continue;
                        if (inline is InlineUIContainer { Child: { } child })
                        { drawing.DrawRoundedRectangle(brush, null, child.TransformToAncestor(owner).TransformBounds(new Rect(child.RenderSize)), 3, 3); continue; }
                        if (inline is not Run run) continue;
                        var from = fragment.Atomic ? 0 : MarkdownCopy.DisplayOffset(owner.documentSource, fragment.Range, selected.Start, run.Text.Length);
                        var to = fragment.Atomic ? run.Text.Length : MarkdownCopy.DisplayOffset(owner.documentSource, fragment.Range, selected.End, run.Text.Length);
                        var start = run.ContentStart.GetPositionAtOffset(from); var end = run.ContentStart.GetPositionAtOffset(to);
                        if (start is null || end is null) continue;
                        var cursor = start;
                        while (cursor.CompareTo(end) < 0)
                        {
                            var next = cursor.GetLineStartPosition(1);
                            var last = next is not null && next.CompareTo(end) < 0 ? next.GetInsertionPosition(LogicalDirection.Backward) : end;
                            var firstRect = cursor.GetCharacterRect(LogicalDirection.Forward); var lastRect = last.GetCharacterRect(LogicalDirection.Backward);
                            if (!firstRect.IsEmpty && !lastRect.IsEmpty)
                            {
                                var rect = new Rect(new Point(Math.Min(firstRect.Left, lastRect.Left), firstRect.Top), new Point(Math.Max(firstRect.Right, lastRect.Right), Math.Max(firstRect.Bottom, lastRect.Bottom)));
                                drawing.DrawRectangle(brush, null, text.TransformToAncestor(owner).TransformBounds(rect));
                            }
                            if (next is null || next.CompareTo(cursor) <= 0 || last == end) break;
                            cursor = next;
                        }
                    }
            }
            drawing.Pop();
        }
    }
}
