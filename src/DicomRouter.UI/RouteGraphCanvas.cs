using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.UI;

public sealed class RouteGraphCanvas : FrameworkElement
{
    public static readonly DependencyProperty NodesProperty = DependencyProperty.Register(nameof(Nodes), typeof(IEnumerable), typeof(RouteGraphCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EdgesProperty = DependencyProperty.Register(nameof(Edges), typeof(IEnumerable), typeof(RouteGraphCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    private const double NodeWidth = 190;
    private const double NodeHeight = 96;
    private GraphNode? _dragNode;
    private GraphNode? _connectFrom;
    private GraphEdge? _selectedEdge;
    private Point _dragOffset;
    private Point _connectPoint;
    public IEnumerable? Nodes { get => (IEnumerable?)GetValue(NodesProperty); set => SetValue(NodesProperty, value); }
    public IEnumerable? Edges { get => (IEnumerable?)GetValue(EdgesProperty); set => SetValue(EdgesProperty, value); }
    public Action<GraphEdge>? EdgeCreated { get; set; }
    public Action<GraphEdge>? EdgeDeleted { get; set; }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == EdgesProperty || e.Property == NodesProperty)
        {
            if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= CollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += CollectionChanged;
            InvalidateVisual();
        }
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 22, 29)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var nodes = Nodes?.Cast<GraphNode>().ToDictionary(x => x.Id) ?? new();
        foreach (var edge in Edges?.Cast<GraphEdge>() ?? Enumerable.Empty<GraphEdge>())
        {
            if (nodes.TryGetValue(edge.FromNodeId, out var from) && nodes.TryGetValue(edge.ToNodeId, out var to))
                DrawEdge(dc, Output(from), Input(to), edge == _selectedEdge ? Brushes.White : new SolidColorBrush(Color.FromRgb(82, 214, 161)), edge == _selectedEdge ? 3 : 2);
        }
        if (_connectFrom != null)
        {
            foreach (var node in nodes.Values)
                if (node != _connectFrom && IsValidTarget(_connectFrom, node))
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(180, 85, 214, 160)), new Pen(Brushes.White, 1), Input(node), 10, 10);
            DrawEdge(dc, Output(_connectFrom), _connectPoint, new SolidColorBrush(Color.FromRgb(242, 184, 75)), 2);
        }
        foreach (var node in nodes.Values)
        {
            var rect = new Rect(node.X, node.Y, NodeWidth, NodeHeight);
            dc.DrawRoundedRectangle(node.Enabled ? new SolidColorBrush(Color.FromRgb(29, 42, 53)) : new SolidColorBrush(Color.FromRgb(49, 40, 43)), new Pen(new SolidColorBrush(Color.FromRgb(73, 101, 116)), 1), rect, 8, 8);
            if (node.Type is "Rule" or "Destination") DrawPort(dc, Input(node));
            if (node.Type is "Listener" or "Rule") DrawPort(dc, Output(node));
            var label = new FormattedText($"{node.Type.ToUpperInvariant()}\n{node.DisplayText}\n{(node.Enabled ? "ENABLED" : "DISABLED")}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = NodeWidth - 30, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(label, new Point(node.X + 15, node.Y + 12));
        }
    }

    private static void DrawPort(DrawingContext dc, Point point) => dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(82, 214, 161)), new Pen(Brushes.White, 1), point, 7, 7);
    private static Point Input(GraphNode node) => new(node.X, node.Y + NodeHeight / 2);
    private static Point Output(GraphNode node) => new(node.X + NodeWidth, node.Y + NodeHeight / 2);
    private static bool IsValidTarget(GraphNode from, GraphNode to) => (from.Type == "Listener" && to.Type is "Rule" or "Destination") || (from.Type == "Rule" && to.Type == "Destination");
    private static bool Near(Point point, Point target) => (point - target).Length <= 16;
    private static bool NearSegment(Point point, Point start, Point end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y; var length = dx * dx + dy * dy;
        if (length == 0) return Near(point, start);
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length, 0, 1);
        return (point - new Point(start.X + t * dx, start.Y + t * dy)).Length < 10;
    }
    private static void DrawEdge(DrawingContext dc, Point start, Point end, Brush brush, double width)
    {
        var bend = Math.Max(60, Math.Abs(end.X - start.X) * .45);
        var geometry = new PathGeometry(new[] { new PathFigure(start, new[] { new BezierSegment(new Point(start.X + bend, start.Y), new Point(end.X - bend, end.Y), end, true) }, false) });
        dc.DrawGeometry(null, new Pen(brush, width), geometry);
        var tangent = new Vector(Math.Max(1, end.X - (end.X - bend)), end.Y - end.Y); tangent.Normalize();
        var side = new Vector(-tangent.Y, tangent.X) * 5;
        var arrow = new StreamGeometry(); using (var context = arrow.Open()) { context.BeginFigure(end, true, true); context.LineTo(end - tangent * 11 + side, true, false); context.LineTo(end - tangent * 11 - side, true, false); } arrow.Freeze(); dc.DrawGeometry(brush, null, arrow);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var point = e.GetPosition(this); var nodes = Nodes?.Cast<GraphNode>().ToList() ?? new();
        _connectFrom = nodes.LastOrDefault(node => (node.Type is "Listener" or "Rule") && Near(point, Output(node)));
        if (_connectFrom != null) { _connectPoint = point; CaptureMouse(); InvalidateVisual(); return; }
        _selectedEdge = Edges?.Cast<GraphEdge>().FirstOrDefault(edge => { var from = nodes.FirstOrDefault(x => x.Id == edge.FromNodeId); var to = nodes.FirstOrDefault(x => x.Id == edge.ToNodeId); return from != null && to != null && NearSegment(point, Output(from), Input(to)); });
        _dragNode = nodes.LastOrDefault(node => new Rect(node.X, node.Y, NodeWidth, NodeHeight).Contains(point));
        if (_dragNode != null) { _dragOffset = new Point(point.X - _dragNode.X, point.Y - _dragNode.Y); CaptureMouse(); }
        InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_connectFrom != null) { _connectPoint = e.GetPosition(this); InvalidateVisual(); return; }
        if (_dragNode != null && e.LeftButton == MouseButtonState.Pressed) { var point = e.GetPosition(this); _dragNode.X = Math.Max(0, point.X - _dragOffset.X); _dragNode.Y = Math.Max(0, point.Y - _dragOffset.Y); InvalidateVisual(); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_connectFrom != null)
        {
            var point = e.GetPosition(this); var target = Nodes?.Cast<GraphNode>().FirstOrDefault(node => node != _connectFrom && IsValidTarget(_connectFrom, node) && Near(point, Input(node)));
            if (target != null)
            {
                var edge = new GraphEdge { FromNodeId = _connectFrom.Id, ToNodeId = target.Id };
                EdgeCreated?.Invoke(edge);
                if (EdgeCreated == null && DataContext is MainWindowViewModel viewModel) viewModel.ConnectEdge(edge);
            }
            _connectFrom = null;
        }
        if (_dragNode != null) { _dragNode = null; if (DataContext is MainWindowViewModel viewModel) viewModel.PersistGraph(); }
        ReleaseMouseCapture(); InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back && _selectedEdge != null)
        {
            EdgeDeleted?.Invoke(_selectedEdge);
            if (EdgeDeleted == null && DataContext is MainWindowViewModel viewModel) viewModel.RemoveEdge(_selectedEdge);
            _selectedEdge = null; e.Handled = true; InvalidateVisual();
        }
        base.OnKeyDown(e);
    }
}
