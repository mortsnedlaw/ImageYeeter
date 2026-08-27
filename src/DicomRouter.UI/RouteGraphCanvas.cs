using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.UI;

public sealed class RouteGraphCanvas : Canvas
{
    public static readonly DependencyProperty NodesProperty = DependencyProperty.Register(nameof(Nodes), typeof(IEnumerable), typeof(RouteGraphCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EdgesProperty = DependencyProperty.Register(nameof(Edges), typeof(IEnumerable), typeof(RouteGraphCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    private GraphNode? _dragNode;
    private Point _dragOffset;
    public IEnumerable? Nodes { get => (IEnumerable?)GetValue(NodesProperty); set => SetValue(NodesProperty, value); }
    public IEnumerable? Edges { get => (IEnumerable?)GetValue(EdgesProperty); set => SetValue(EdgesProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 22, 29)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var nodes = Nodes?.Cast<GraphNode>().ToDictionary(x => x.Id) ?? new();
        foreach (var edge in Edges?.Cast<GraphEdge>() ?? Enumerable.Empty<GraphEdge>())
        {
            if (!nodes.TryGetValue(edge.FromNodeId, out var from) || !nodes.TryGetValue(edge.ToNodeId, out var to)) continue;
            var start = new Point(from.X + 170, from.Y + 42); var end = new Point(to.X, to.Y + 42);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(82, 214, 161)), 2);
            drawingContext.DrawGeometry(null, pen, new PathGeometry(new[] { new PathFigure(start, new[] { new BezierSegment(new Point(start.X + 70, start.Y), new Point(end.X - 70, end.Y), end, true) }, false) }));
        }
        foreach (var node in nodes.Values)
        {
            var brush = node.Enabled ? new SolidColorBrush(Color.FromRgb(29, 42, 53)) : new SolidColorBrush(Color.FromRgb(49, 40, 43));
            drawingContext.DrawRoundedRectangle(brush, new Pen(new SolidColorBrush(Color.FromRgb(73, 101, 116)), 1), new Rect(node.X, node.Y, 170, 84), 6, 6);
            var label = new FormattedText($"{node.Type.ToUpperInvariant()}\n{node.ReferenceId}\n● {(node.Enabled ? "ENABLED" : "DISABLED")}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(label, new Point(node.X + 12, node.Y + 10));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        _dragNode = Nodes?.Cast<GraphNode>().LastOrDefault(node => new Rect(node.X, node.Y, 170, 84).Contains(point));
        if (_dragNode != null) { _dragOffset = new Point(point.X - _dragNode.X, point.Y - _dragNode.Y); CaptureMouse(); }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragNode == null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this); _dragNode.X = Math.Max(0, point.X - _dragOffset.X); _dragNode.Y = Math.Max(0, point.Y - _dragOffset.Y); InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (_dragNode != null) { ReleaseMouseCapture(); _dragNode = null; } }
}
