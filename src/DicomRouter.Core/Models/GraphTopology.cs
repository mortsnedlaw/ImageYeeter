using System;

namespace DicomRouter.Core.Models;

public sealed class GraphNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "Rule";
    public string ReferenceId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public bool Enabled { get; set; } = true;
    public string DisplayText { get; set; } = string.Empty;
}

public sealed class GraphEdge
{
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string Branch { get; set; } = "True";
}