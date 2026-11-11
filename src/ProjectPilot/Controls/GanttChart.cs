using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ProjectPilot.Converters;
using ProjectPilot.Models;

namespace ProjectPilot.Controls;

public enum GanttRowKind { Task, Milestone }

public sealed class GanttRow
{
    public GanttRowKind Kind { get; init; }
    public int Id { get; init; }
    public string Label { get; init; } = "";
    public string SubLabel { get; init; } = "";
    public int Indent { get; init; }
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }            // inclusive last day
    public DateTime? BaselineStart { get; init; }
    public DateTime? BaselineEnd { get; init; }
    public int Progress { get; init; }
    public WorkStatus Status { get; init; }
    public bool IsCritical { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsOverdue { get; init; }
    public bool IsSummary { get; init; }
    public IReadOnlyList<int> DependsOn { get; init; } = Array.Empty<int>();
    public string Tooltip { get; init; } = "";

    public bool HasBar => Start.HasValue || End.HasValue;
    public DateTime BarStart => (Start ?? End)!.Value.Date;
    public DateTime BarEnd => (End ?? Start)!.Value.Date;
}

public sealed class GanttData
{
    public List<GanttRow> Rows { get; init; } = new();
    public DateTime RangeStart { get; init; }
    public DateTime RangeEnd { get; init; }
    public DateTime? Target { get; init; }
}

public sealed class GanttRowEventArgs : EventArgs
{
    public GanttRowEventArgs(GanttRowKind kind, int id, DateTime newStart, DateTime newEnd)
    {
        Kind = kind; Id = id; NewStart = newStart; NewEnd = newEnd;
    }
    public GanttRowKind Kind { get; }
    public int Id { get; }
    public DateTime NewStart { get; }
    public DateTime NewEnd { get; }
}

/// <summary>
/// Custom-drawn interactive Gantt chart: task bars with progress, milestones, dependency arrows,
/// critical path, planned (baseline) bars, today line. Drag a bar to move it, drag its right edge to resize,
/// double-click to open. Scrolling offsets are supplied by the host (two ScrollBars).
/// </summary>
public sealed class GanttChart : FrameworkElement
{
    public const double LabelWidth = 270;
    public const double HeaderHeight = 46;
    public const double RowHeight = 32;
    private const double EdgeGrip = 7;

    // ---------------------------------------------------------------- dependency properties

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(GanttData), typeof(GanttChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutInputChanged));

    public static readonly DependencyProperty PixelsPerDayProperty = DependencyProperty.Register(
        nameof(PixelsPerDay), typeof(double), typeof(GanttChart),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutInputChanged));

    public static readonly DependencyProperty ShowCriticalProperty = DependencyProperty.Register(
        nameof(ShowCritical), typeof(bool), typeof(GanttChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowBaselineProperty = DependencyProperty.Register(
        nameof(ShowBaseline), typeof(bool), typeof(GanttChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset), typeof(double), typeof(GanttChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(GanttChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public GanttData? Data { get => (GanttData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double PixelsPerDay { get => (double)GetValue(PixelsPerDayProperty); set => SetValue(PixelsPerDayProperty, value); }
    public bool ShowCritical { get => (bool)GetValue(ShowCriticalProperty); set => SetValue(ShowCriticalProperty, value); }
    public bool ShowBaseline { get => (bool)GetValue(ShowBaselineProperty); set => SetValue(ShowBaselineProperty, value); }
    public double HorizontalOffset { get => (double)GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    public double VerticalOffset { get => (double)GetValue(VerticalOffsetProperty); set => SetValue(VerticalOffsetProperty, value); }

    /// <summary>Raised when a bar has been dragged (moved or resized) to new dates.</summary>
    public event EventHandler<GanttRowEventArgs>? RowMoved;
    /// <summary>Raised when a row is double-clicked.</summary>
    public event EventHandler<GanttRowEventArgs>? RowOpened;
    /// <summary>Raised when the scrollable size changes so the host can update its scroll bars.</summary>
    public event EventHandler? ExtentChanged;

    private static void OnLayoutInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((GanttChart)d).ExtentChanged?.Invoke(d, EventArgs.Empty);

    public GanttChart()
    {
        ClipToBounds = true;
        Focusable = true;
        SizeChanged += (_, _) => ExtentChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- geometry

    public double ContentWidth => Data == null ? 0 : ((Data.RangeEnd - Data.RangeStart).Days + 1) * PixelsPerDay;
    public double ContentHeight => Data == null ? 0 : Data.Rows.Count * RowHeight;
    public double BodyWidth => Math.Max(0, ActualWidth - LabelWidth);
    public double BodyHeight => Math.Max(0, ActualHeight - HeaderHeight);

    /// <summary>Content x-position (before scrolling) of the start of a day.</summary>
    public double ContentX(DateTime d) => Data == null ? 0 : (d.Date - Data.RangeStart).Days * PixelsPerDay;

    private double X(DateTime d) => LabelWidth + ContentX(d) - HorizontalOffset;
    private double RowTop(int i) => HeaderHeight + i * RowHeight - VerticalOffset;

    private Rect BarRect(int i, DateTime start, DateTime end, bool summary)
    {
        double x1 = X(start), x2 = X(end.AddDays(1));
        double h = summary ? 10 : RowHeight - 12;
        double y = RowTop(i) + (RowHeight - h) / 2;
        return new Rect(x1, y, Math.Max(3, x2 - x1), h);
    }

    // ---------------------------------------------------------------- drag state

    private enum DragMode { None, Move, Resize }
    private DragMode _drag = DragMode.None;
    private int _dragRow = -1;
    private double _dragStartX;
    private int _dragDelta;
    private int _hoverRow = -1;

    private (DateTime start, DateTime end) PreviewDates(GanttRow r)
    {
        if (_drag == DragMode.Move) return (r.BarStart.AddDays(_dragDelta), r.BarEnd.AddDays(_dragDelta));
        var end = r.BarEnd.AddDays(_dragDelta);
        if (end < r.BarStart) end = r.BarStart;
        return (r.BarStart, end);
    }

    // ---------------------------------------------------------------- rendering helpers

    private Brush B(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private static Brush WithOpacity(Brush b, double opacity)
    {
        var c = b.Clone();
        c.Opacity = opacity;
        c.Freeze();
        return c;
    }

    private void Text(DrawingContext dc, string text, double x, double y, Brush brush, double size = 12,
        bool bold = false, double maxWidth = 0, bool center = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (maxWidth > 1)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }
        dc.DrawText(ft, new Point(center ? x - ft.Width / 2 : x, y - ft.Height / 2));
    }

    // ---------------------------------------------------------------- render

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var surface = B("SurfaceBg");
        var surface2 = B("Surface2Bg");
        var line = B("BorderLine");
        var text = B("TextPrimary");
        var muted = B("TextSecondary");
        var danger = B("Danger");
        var accent = B("Accent");
        var linePen = new Pen(line, 1);

        dc.DrawRectangle(surface, null, new Rect(0, 0, w, h));
        var data = Data;
        if (data == null || data.Rows.Count == 0)
        {
            Text(dc, data == null ? "Choose a project to see its timeline." : "This project has no tasks or milestones yet.",
                w / 2, h / 2, muted, 14, center: true);
            return;
        }

        double ppd = PixelsPerDay;
        int firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / RowHeight));
        int lastRow = Math.Min(data.Rows.Count - 1, (int)Math.Ceiling((VerticalOffset + BodyHeight) / RowHeight));
        var firstDay = data.RangeStart.AddDays(Math.Max(0, (int)Math.Floor(HorizontalOffset / ppd) - 1));
        var lastDay = data.RangeStart.AddDays((int)Math.Ceiling((HorizontalOffset + BodyWidth) / ppd) + 1);
        if (lastDay > data.RangeEnd.AddDays(1)) lastDay = data.RangeEnd.AddDays(1);

        // ================= body =================
        dc.PushClip(new RectangleGeometry(new Rect(LabelWidth, HeaderHeight, BodyWidth, BodyHeight)));

        // weekends + grid
        if (ppd >= 6)
        {
            var weekend = WithOpacity(surface2, 0.7);
            for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    dc.DrawRectangle(weekend, null, new Rect(X(d), HeaderHeight, ppd, BodyHeight));
        }
        foreach (var d in GridLines(firstDay, lastDay, ppd))
            dc.DrawLine(linePen, new Point(X(d), HeaderHeight), new Point(X(d), h));
        for (int i = firstRow; i <= lastRow + 1; i++)
            dc.DrawLine(linePen, new Point(LabelWidth, RowTop(i)), new Point(w, RowTop(i)));
        if (_hoverRow >= 0 && _drag == DragMode.None)
            dc.DrawRectangle(WithOpacity(accent, 0.06), null, new Rect(LabelWidth, RowTop(_hoverRow), BodyWidth, RowHeight));

        // target date and today
        if (data.Target.HasValue)
        {
            var tx = X(data.Target.Value.AddDays(1));
            dc.DrawLine(new Pen(accent, 1.5) { DashStyle = DashStyles.Dash }, new Point(tx, HeaderHeight), new Point(tx, h));
        }
        var todayX = X(DateTime.Today) + ppd / 2;
        dc.DrawLine(new Pen(danger, 1.5), new Point(todayX, HeaderHeight), new Point(todayX, h));

        // index of rows/bars for arrows
        var rowOfTask = new Dictionary<int, int>();
        for (int i = 0; i < data.Rows.Count; i++)
            if (data.Rows[i].Kind == GanttRowKind.Task) rowOfTask[data.Rows[i].Id] = i;

        Rect RectFor(int i)
        {
            var r = data.Rows[i];
            var (s, e) = i == _dragRow && _drag != DragMode.None ? PreviewDates(r) : (r.BarStart, r.BarEnd);
            return BarRect(i, s, e, r.IsSummary);
        }

        // dependency arrows (drawn under bars)
        var arrowPen = new Pen(muted, 1.1);
        var critPen = new Pen(danger, 1.4);
        for (int i = 0; i < data.Rows.Count; i++)
        {
            var r = data.Rows[i];
            if (r.Kind != GanttRowKind.Task || !r.HasBar) continue;
            foreach (var dep in r.DependsOn)
            {
                if (!rowOfTask.TryGetValue(dep, out var pi) || !data.Rows[pi].HasBar) continue;
                // skip arrows that are entirely off-screen vertically
                if (Math.Max(i, pi) < firstRow - 1 || Math.Min(i, pi) > lastRow + 1) continue;
                var pr = RectFor(pi);
                var sr = RectFor(i);
                bool crit = ShowCritical && r.IsCritical && data.Rows[pi].IsCritical;
                DrawArrow(dc, pr, sr, crit ? critPen : arrowPen, crit ? danger : muted);
            }
        }

        // bars
        for (int i = firstRow; i <= lastRow; i++)
        {
            var r = data.Rows[i];
            if (!r.HasBar) continue;
            var top = RowTop(i);

            if (r.Kind == GanttRowKind.Milestone)
            {
                var (ms, _) = i == _dragRow && _drag != DragMode.None ? PreviewDates(r) : (r.BarStart, r.BarEnd);
                double cx = X(ms) + ppd / 2, cy = top + RowHeight / 2, s = 8;
                var fill = r.IsCompleted ? B("Success") : r.IsOverdue ? danger : B("Purple");
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(cx, cy - s), true, true);
                    ctx.LineTo(new Point(cx + s, cy), true, false);
                    ctx.LineTo(new Point(cx, cy + s), true, false);
                    ctx.LineTo(new Point(cx - s, cy), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(fill, new Pen(surface, 1.5), g);
                Text(dc, ms.ToString("dd MMM"), cx + s + 5, cy, muted, 11);
                continue;
            }

            if (ShowBaseline && r.BaselineStart.HasValue && r.BaselineEnd.HasValue)
            {
                double bx1 = X(r.BaselineStart.Value), bx2 = X(r.BaselineEnd.Value.AddDays(1));
                dc.DrawRoundedRectangle(WithOpacity(muted, 0.55), null,
                    new Rect(bx1, top + RowHeight - 7, Math.Max(3, bx2 - bx1), 4), 2, 2);
            }

            var rect = RectFor(i);
            var key = BrushKeyConverter.KeyFor(r.IsCompleted ? WorkStatus.Completed : r.IsOverdue ? WorkStatus.Overdue : r.Status);
            var color = B(key);
            if (r.IsSummary)
            {
                dc.DrawRectangle(WithOpacity(text, 0.75), null, rect);
                dc.DrawRectangle(text, null, new Rect(rect.Left, rect.Top, 4, rect.Height + 4));
                dc.DrawRectangle(text, null, new Rect(rect.Right - 4, rect.Top, 4, rect.Height + 4));
            }
            else
            {
                dc.DrawRoundedRectangle(WithOpacity(color, 0.28), null, rect, 4, 4);
                var pw = rect.Width * Math.Clamp(r.Progress, 0, 100) / 100.0;
                if (pw > 0) dc.DrawRoundedRectangle(color, null, new Rect(rect.Left, rect.Top, pw, rect.Height), 4, 4);
                Pen? outline = ShowCritical && r.IsCritical ? new Pen(danger, 2) : new Pen(color, 1);
                dc.DrawRoundedRectangle(null, outline, rect, 4, 4);
            }

            if (i == _dragRow && _drag != DragMode.None)
            {
                var (ps, pe) = PreviewDates(r);
                Text(dc, $"{ps:dd MMM} – {pe:dd MMM}  ({(pe - ps).Days + 1}d)", rect.Right + 8, top + RowHeight / 2, text, 11, bold: true);
            }
            else if (!r.IsSummary)
            {
                Text(dc, $"{r.Progress}%", rect.Right + 6, top + RowHeight / 2, muted, 11);
            }
        }
        dc.Pop();

        // ================= header =================
        dc.DrawRectangle(surface2, null, new Rect(LabelWidth, 0, BodyWidth, HeaderHeight));
        dc.PushClip(new RectangleGeometry(new Rect(LabelWidth, 0, BodyWidth, HeaderHeight)));
        DrawHeader(dc, firstDay, lastDay, ppd, text, muted, linePen);
        Text(dc, "Today", todayX, HeaderHeight - 8, danger, 9, bold: true, center: true);
        dc.Pop();

        // ================= labels =================
        dc.DrawRectangle(surface, null, new Rect(0, HeaderHeight, LabelWidth, BodyHeight));
        dc.PushClip(new RectangleGeometry(new Rect(0, HeaderHeight, LabelWidth, BodyHeight)));
        for (int i = firstRow; i <= lastRow; i++)
        {
            var r = data.Rows[i];
            var top = RowTop(i);
            if (i == _hoverRow) dc.DrawRectangle(WithOpacity(accent, 0.06), null, new Rect(0, top, LabelWidth, RowHeight));
            dc.DrawLine(linePen, new Point(0, top + RowHeight), new Point(LabelWidth, top + RowHeight));
            double x = 12 + r.Indent * 16;
            if (r.Kind == GanttRowKind.Milestone)
            {
                Text(dc, "◆", x, top + RowHeight / 2, r.IsCompleted ? B("Success") : r.IsOverdue ? danger : B("Purple"), 12);
                x += 16;
            }
            else if (ShowCritical && r.IsCritical)
            {
                dc.DrawRectangle(danger, null, new Rect(0, top + 4, 3, RowHeight - 8));
            }
            bool hasSub = !string.IsNullOrEmpty(r.SubLabel);
            var labelBrush = r.IsCompleted ? muted : text;
            Text(dc, r.Label, x, top + (hasSub ? 11 : RowHeight / 2), labelBrush, 12.5,
                bold: r.IsSummary || r.Kind == GanttRowKind.Milestone, maxWidth: LabelWidth - x - 8);
            if (hasSub)
                Text(dc, r.SubLabel, x, top + 23, r.IsOverdue ? danger : muted, 10.5, maxWidth: LabelWidth - x - 8);
        }
        dc.Pop();

        // ================= corner + dividers =================
        dc.DrawRectangle(surface2, null, new Rect(0, 0, LabelWidth, HeaderHeight));
        Text(dc, "Task / milestone", 12, HeaderHeight / 2, muted, 12, bold: true);
        dc.DrawLine(linePen, new Point(LabelWidth, 0), new Point(LabelWidth, h));
        dc.DrawLine(linePen, new Point(0, HeaderHeight), new Point(w, HeaderHeight));
    }

    private static IEnumerable<DateTime> GridLines(DateTime from, DateTime to, double ppd)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (ppd >= 18) yield return d;
            else if (ppd >= 6) { if (d.DayOfWeek == DayOfWeek.Monday) yield return d; }
            else if (d.Day == 1) yield return d;
        }
    }

    private void DrawHeader(DrawingContext dc, DateTime from, DateTime to, double ppd, Brush text, Brush muted, Pen linePen)
    {
        double mid = HeaderHeight / 2;
        dc.DrawLine(linePen, new Point(LabelWidth, mid), new Point(ActualWidth, mid));

        // Top row: months (or years when zoomed far out)
        var monthStart = new DateTime(from.Year, from.Month, 1);
        for (var m = monthStart; m <= to; m = m.AddMonths(1))
        {
            double x1 = X(m), x2 = X(m.AddMonths(1));
            dc.DrawLine(linePen, new Point(x1, 0), new Point(x1, mid));
            var label = ppd >= 6 ? m.ToString("MMMM yyyy") : (m.Month == 1 || m == monthStart ? m.ToString("yyyy") : "");
            double lx = Math.Max(x1, LabelWidth) + 6;
            if (lx < x2 - 20) Text(dc, label, lx, mid / 2, text, 12, bold: true, maxWidth: Math.Max(2, x2 - lx - 4));
        }

        // Bottom row
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            double x = X(d);
            if (ppd >= 18)
            {
                dc.DrawLine(linePen, new Point(x, mid), new Point(x, HeaderHeight));
                var wk = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                Text(dc, d.Day.ToString(), x + ppd / 2, mid + mid / 2, wk ? muted : text, 11, center: true);
            }
            else if (ppd >= 6 && d.DayOfWeek == DayOfWeek.Monday)
            {
                dc.DrawLine(linePen, new Point(x, mid), new Point(x, HeaderHeight));
                Text(dc, d.ToString("d MMM"), x + 4, mid + mid / 2, muted, 10.5, maxWidth: ppd * 7 - 6);
            }
            else if (ppd < 6 && d.Day == 1)
            {
                dc.DrawLine(linePen, new Point(x, mid), new Point(x, HeaderHeight));
                Text(dc, d.ToString("MMM"), x + 4, mid + mid / 2, muted, 10.5, maxWidth: Math.Max(2, ppd * 28 - 6));
            }
        }
    }

    private static void DrawArrow(DrawingContext dc, Rect from, Rect to, Pen pen, Brush headBrush)
    {
        double sx = from.Right, sy = from.Top + from.Height / 2;
        double ex = to.Left, ey = to.Top + to.Height / 2;
        var pts = new List<Point> { new(sx, sy), new(sx + 8, sy) };
        if (ex - 8 >= sx + 8)
        {
            pts.Add(new Point(sx + 8, ey));
        }
        else
        {
            double midY = ey > sy ? to.Top - 4 : to.Bottom + 4;
            pts.Add(new Point(sx + 8, midY));
            pts.Add(new Point(ex - 10, midY));
            pts.Add(new Point(ex - 10, ey));
        }
        pts.Add(new Point(ex - 1, ey));

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i], true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(new Point(ex, ey), true, true);
            ctx.LineTo(new Point(ex - 6, ey - 4), true, false);
            ctx.LineTo(new Point(ex - 6, ey + 4), true, false);
        }
        head.Freeze();
        dc.DrawGeometry(headBrush, null, head);
    }

    // ---------------------------------------------------------------- mouse

    private int RowAt(Point p)
    {
        if (Data == null || p.Y < HeaderHeight) return -1;
        int i = (int)Math.Floor((p.Y - HeaderHeight + VerticalOffset) / RowHeight);
        return i >= 0 && i < Data.Rows.Count ? i : -1;
    }

    private DragMode HitBar(int i, Point p)
    {
        if (Data == null || i < 0 || p.X < LabelWidth) return DragMode.None;
        var r = Data.Rows[i];
        if (!r.HasBar) return DragMode.None;
        if (r.Kind == GanttRowKind.Milestone)
        {
            double cx = X(r.BarStart) + PixelsPerDay / 2;
            return Math.Abs(p.X - cx) <= 10 ? DragMode.Move : DragMode.None;
        }
        var rect = BarRect(i, r.BarStart, r.BarEnd, r.IsSummary);
        var hit = new Rect(rect.Left - 2, RowTop(i), rect.Width + 4, RowHeight);
        if (!hit.Contains(p)) return DragMode.None;
        return p.X >= rect.Right - EdgeGrip ? DragMode.Resize : DragMode.Move;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_drag != DragMode.None && Data != null)
        {
            int delta = (int)Math.Round((p.X - _dragStartX) / PixelsPerDay);
            if (delta != _dragDelta) { _dragDelta = delta; InvalidateVisual(); }
            return;
        }
        int row = RowAt(p);
        if (row != _hoverRow)
        {
            _hoverRow = row;
            ToolTip = row >= 0 && Data != null && !string.IsNullOrEmpty(Data.Rows[row].Tooltip) ? Data.Rows[row].Tooltip : null;
            InvalidateVisual();
        }
        Cursor = HitBar(row, p) switch
        {
            DragMode.Resize => Cursors.SizeWE,
            DragMode.Move => Cursors.SizeAll,
            _ => null
        };
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag == DragMode.None && _hoverRow != -1) { _hoverRow = -1; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        int row = RowAt(p);
        if (row < 0 || Data == null) return;
        var r = Data.Rows[row];
        if (e.ClickCount >= 2)
        {
            _drag = DragMode.None;
            RowOpened?.Invoke(this, new GanttRowEventArgs(r.Kind, r.Id, r.HasBar ? r.BarStart : DateTime.Today, r.HasBar ? r.BarEnd : DateTime.Today));
            e.Handled = true;
            return;
        }
        var mode = HitBar(row, p);
        if (mode == DragMode.None) return;
        _drag = mode;
        _dragRow = row;
        _dragStartX = p.X;
        _dragDelta = 0;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == DragMode.None) return;
        var data = Data;
        int row = _dragRow, delta = _dragDelta;
        var r = data != null && row >= 0 && row < data.Rows.Count ? data.Rows[row] : null;
        (DateTime s, DateTime en) dates = r != null ? PreviewDates(r) : (DateTime.Today, DateTime.Today);
        EndDrag();
        if (r != null && delta != 0)
            RowMoved?.Invoke(this, new GanttRowEventArgs(r.Kind, r.Id, dates.s, dates.en));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag != DragMode.None) EndDrag();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _drag != DragMode.None) { EndDrag(); e.Handled = true; }
    }

    private void EndDrag()
    {
        _drag = DragMode.None;
        _dragRow = -1;
        _dragDelta = 0;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }
}
