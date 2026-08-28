using DicomRouter.Core.Models;

namespace DicomRouter.Infrastructure.Models;

public sealed class ListenerConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Listener";
    public string BindIp { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 104;
    public string CalledAeTitle { get; set; } = "IMAGEYEETER";
    public bool Enabled { get; set; } = true;
    public bool AutoStart { get; set; }
    public int MaxAssociations { get; set; } = 32;
    public int AssociationTimeoutSeconds { get; set; } = 30;
    public int ReceiveTimeoutSeconds { get; set; } = 60;
    public int MaxPduSize { get; set; } = 16 * 1024;
    public string Notes { get; set; } = string.Empty;
}

public sealed class RouterConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public List<ListenerConfiguration> Listeners { get; set; } = new();
    public List<Destination> Destinations { get; set; } = new();
    public List<DicomRouter.Core.Models.RoutingRule> Rules { get; set; } = new();
    public List<GraphNode> GraphNodes { get; set; } = new();
    public List<GraphEdge> GraphEdges { get; set; } = new();
}
