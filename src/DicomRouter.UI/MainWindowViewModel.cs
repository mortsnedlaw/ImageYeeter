using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using DicomRouter.Core.Models;
using DicomRouter.Core.Services;
using DicomRouter.Infrastructure.Dicom;
using DicomRouter.Infrastructure.Models;
using DicomRouter.Infrastructure;

namespace DicomRouter.UI;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ConfigurationStore _configurationStore = new();
    private readonly ListenerManager _listenerManager;
    private RouterConfiguration _configuration = new();
    private readonly DicomForwarder _forwarder;
    private readonly RuleEvaluator _evaluator = new();
    private readonly Spooler _spooler;
    private readonly RuntimeEventBus _runtimeEvents = new();
    private readonly DispatcherTimer _timer;
    private bool _scpRunning;
    private string _scpState = "STOPPED";
    private string _lastError = "No errors recorded";
    private string _selectedTab = "Dashboard";
    private int _activeAssociations;
    private long _incomingImages;
    private long _outgoingImages;
    private double _throughput;
    private string _toolResult = "Ready";
    private string _inspectorFilter = string.Empty;
    private NativeDicomDataset? _inspectedDataset;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<TrafficRow> Traffic { get; } = new();
    public ObservableCollection<RouteRow> RouteFlow { get; } = new();
    public ObservableCollection<RuleEditorRow> Rules { get; } = new();
    public ObservableCollection<Destination> Destinations { get; } = new();
    public ObservableCollection<SpoolRow> Spool { get; } = new();
    public ObservableCollection<LogRow> Events { get; } = new();
    public ObservableCollection<InspectorTag> InspectorTags { get; } = new();
    public ObservableCollection<ListenerRow> Listeners { get; } = new();
    public ObservableCollection<GraphNode> GraphNodes { get; } = new();
    public ObservableCollection<GraphEdge> GraphEdges { get; } = new();
    public IReadOnlyList<string> DicomTags { get; } = new[] { "Modality (0008,0060)", "Station Name (0008,1010)", "Manufacturer (0008,0070)", "Manufacturer Model Name (0008,1090)", "Institution Name (0008,0080)", "Study Description (0008,1030)", "Series Description (0008,103E)", "Body Part Examined (0018,0015)", "Protocol Name (0018,1030)", "Performing Physician (0008,1050)", "Patient Sex (0010,0040)", "SOP Class UID (0008,0016)", "Study Instance UID (0020,000D)", "Series Instance UID (0020,000E)", "Custom Tag (gggg,eeee)" };
    public Array ConditionOperators => Enum.GetValues<ConditionOperator>();

    public string[] Tabs { get; } = { "Dashboard", "Traffic", "Routes", "Rules", "Destinations", "Spool", "Inspector", "Tools", "Event Log" };
    public string SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }
    public bool ScpRunning { get => _scpRunning; private set => Set(ref _scpRunning, value); }
    public string ScpState { get => _scpState; private set => Set(ref _scpState, value); }
    public int ActiveAssociations { get => _activeAssociations; private set => Set(ref _activeAssociations, value); }
    public long IncomingImages { get => _incomingImages; private set => Set(ref _incomingImages, value); }
    public long OutgoingImages { get => _outgoingImages; private set => Set(ref _outgoingImages, value); }
    public double Throughput { get => _throughput; private set => Set(ref _throughput, value); }
    public string LastError { get => _lastError; private set => Set(ref _lastError, value); }
    public int QueueDepth => Spool.Count(x => x.State is "Pending" or "Sending");
    public int FailedDeliveries => Spool.Count(x => x.State is "Failed" or "Dead Letter");
    public string ToolResult { get => _toolResult; private set => Set(ref _toolResult, value); }
    public string InspectorFilter { get => _inspectorFilter; set { Set(ref _inspectorFilter, value); RefreshInspector(); } }

    public string LocalAeTitle { get; set; } = "IMAGEYEETER";
    public string LocalIp { get; set; } = "0.0.0.0";
    public int LocalPort { get; set; } = 104;
    public string TestHost { get; set; } = "localhost";
    public int TestPort { get; set; } = 104;
    public string TestAeTitle { get; set; } = "PACS";
    public string TestCallingAe { get; set; } = "IMAGEYEETER";
    public string TestFilePath { get; set; } = string.Empty;

    public ICommand StartScpCommand { get; }
    public ICommand StopScpCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand MoveRuleUpCommand { get; }
    public ICommand MoveRuleDownCommand { get; }
    public ICommand AddDestinationCommand { get; }
    public ICommand DeleteDestinationCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand RetryAllCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand InspectCommand { get; }
    public ICommand TestEchoCommand { get; }
    public ICommand TestDestinationEchoCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand DumpTagsCommand { get; }
    public ICommand WhichRoutesCommand { get; }
    public ICommand SaveConfigurationCommand { get; }
    public ICommand AddListenerCommand { get; }
    public ICommand DeleteListenerCommand { get; }

    public MainWindowViewModel()
    {
        _forwarder = new DicomForwarder { Events = _runtimeEvents };
        _listenerManager = new ListenerManager(OnReceivedAsync, _runtimeEvents);
        StartScpCommand = new AsyncCommand(StartScpAsync, () => !ScpRunning);
        StopScpCommand = new AsyncCommand(StopScpAsync, () => ScpRunning);
        AddRuleCommand = new ActionCommand(() => { var rule = new RuleEditorRow { Name = "New rule", Priority = Rules.Count + 1 }; rule.RootGroup.Conditions.Add(new ConditionEditorRow()); Rules.Add(rule); EnsureGraph(); PersistChanges(); });
        DeleteRuleCommand = new ActionCommand(() => { if (Rules.Count > 0) Rules.Remove(Rules[^1]); EnsureGraph(); PersistChanges(); });
        MoveRuleUpCommand = new ActionCommand(() => MoveRule(-1));
        MoveRuleDownCommand = new ActionCommand(() => MoveRule(1));
        AddDestinationCommand = new ActionCommand(() => { Destinations.Add(new Destination { Name = "new-destination", AeTitle = "REMOTE", Host = "localhost" }); _spooler?.UpdateDestinations(Destinations); EnsureGraph(); PersistChanges(); });
        DeleteDestinationCommand = new ActionCommand(() => { if (Destinations.Count > 0) Destinations.RemoveAt(Destinations.Count - 1); EnsureGraph(); PersistChanges(); });
        RetryCommand = new AsyncCommand(RetrySpoolAsync);
        RetryAllCommand = new AsyncCommand(RetrySpoolAsync);
        CancelCommand = new AsyncCommand(CancelSpoolAsync);
        InspectCommand = new ActionCommand(() => { SelectedTab = "Inspector"; RefreshInspector(); });
        TestEchoCommand = new AsyncCommand(TestEchoAsync);
        TestDestinationEchoCommand = new AsyncObjectCommand(TestDestinationEchoAsync);
        OpenFileCommand = new ActionCommand(OpenLocalFile);
        DumpTagsCommand = new ActionCommand(DumpSelectedFile);
        WhichRoutesCommand = new ActionCommand(SimulateSelectedFile);
        SaveConfigurationCommand = new AsyncCommand(SaveConfigurationAsync);
        AddListenerCommand = new ActionCommand(() => { var listener = new ListenerConfiguration { Name = $"Listener {Listeners.Count + 1}", Port = 104 + Listeners.Count + 1 }; Listeners.Add(new ListenerRow { Configuration = listener }); EnsureGraph(); PersistChanges(); });
        DeleteListenerCommand = new ActionCommand(() => { if (Listeners.Count > 1) Listeners.RemoveAt(Listeners.Count - 1); EnsureGraph(); PersistChanges(); });
        LoadConfiguration();
        _spooler = new Spooler(Path.Combine(AppContext.BaseDirectory, "spool"), _forwarder, Destinations.ToArray());
        _spooler.StartProcessing();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { EnsureGraph(); _spooler.UpdateDestinations(Destinations); DrainRuntimeEvents(); Throughput = IncomingImages == 0 ? 0 : Math.Round(0.02 + QueueDepth * 0.001, 2); RefreshSpool(); OnPropertyChanged(nameof(QueueDepth)); OnPropertyChanged(nameof(FailedDeliveries)); };
        _timer.Start();
    }

    private async Task StartScpAsync()
    {
        try { var listener = Listeners.FirstOrDefault() ?? new ListenerRow { Configuration = new ListenerConfiguration { BindIp = LocalIp, Port = LocalPort, CalledAeTitle = LocalAeTitle }, Status = "Stopped" }; await _listenerManager.StartAsync(listener.Configuration); listener.Status = "Running"; ScpRunning = true; ScpState = "LISTENING"; AddEvent("Association", $"SCP listening on {listener.Configuration.BindIp}:{listener.Configuration.Port}", "Info"); }
        catch (Exception ex) { LastError = ex.Message; ScpState = "ERROR"; AddEvent("Error", ex.Message, "Error"); }
    }

    private async Task StopScpAsync() { foreach (var listener in Listeners.ToArray()) await _listenerManager.StopAsync(listener.Configuration.Id); foreach (var listener in Listeners) listener.Status = "Stopped"; ScpRunning = false; ScpState = "STOPPED"; AddEvent("Association", "SCP stopped", "Info"); }
    private async Task TestEchoAsync() { var watch = System.Diagnostics.Stopwatch.StartNew(); ToolResult = $"C-ECHO to {TestAeTitle}@{TestHost}:{TestPort}..."; var ok = await _forwarder.EchoAsync(TestHost, TestPort, TestAeTitle, TestCallingAe); watch.Stop(); ToolResult = ok ? $"Success · {watch.ElapsedMilliseconds} ms · 1 presentation context" : "Failed · no association"; AddEvent("DIMSE", $"C-ECHO {TestAeTitle}: {ToolResult}", ok ? "Info" : "Error"); }
    private async Task TestDestinationEchoAsync(object? parameter) { if (parameter is not Destination destination) return; var ok = await _forwarder.EchoAsync(destination.Host, destination.Port, destination.AeTitle, destination.CallingAeTitle); ToolResult = ok ? $"C-ECHO {destination.Name}: Success" : $"C-ECHO {destination.Name}: Failed"; }
    private Task RetrySpoolAsync() => _spooler == null ? Task.CompletedTask : _spooler.RetryAsync();
    private Task CancelSpoolAsync() => _spooler == null ? Task.CompletedTask : _spooler.CancelAsync();
    private void DrainRuntimeEvents() { var count = 0; while (count++ < 250 && _runtimeEvents.TryRead(out var runtimeEvent)) AddEvent(runtimeEvent.Type.ToString(), runtimeEvent.Message, runtimeEvent.Type == RuntimeEventType.Error || runtimeEvent.Type == RuntimeEventType.ForwardFailed ? "Error" : "Info"); }

    private async Task OnReceivedAsync(DicomReceivedEventArgs args)
    {
        IncomingImages++; var size = args.RawDataset.Length; var sop = args.Dataset.Get(DicomTag.SOPInstanceUid); _inspectedDataset = args.Dataset;
        var listenerNode = GraphNodes.FirstOrDefault(x => x.Type == "Listener" && x.ReferenceId == args.ListenerId);
        var allowedRuleIds = listenerNode == null ? new HashSet<string>() : GraphEdges.Where(x => x.FromNodeId == listenerNode.Id).Select(x => GraphNodes.FirstOrDefault(node => node.Id == x.ToNodeId)).Where(x => x?.Type == "Rule").Select(x => x!.ReferenceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directDestinationIds = listenerNode == null ? Enumerable.Empty<string>() : GraphEdges.Where(x => x.FromNodeId == listenerNode.Id).Select(x => GraphNodes.FirstOrDefault(node => node.Id == x.ToNodeId)).Where(x => x?.Type == "Destination").Select(x => x!.ReferenceId);
        var matches = _evaluator.Evaluate(args.Metadata, _configuration.Rules.Where(x => allowedRuleIds.Contains(x.Id)));
        var destinations = _configuration.Rules.Where(x => allowedRuleIds.Contains(x.Id)).SelectMany(rule => GraphEdges.Where(edge => edge.FromNodeId == GraphNodes.FirstOrDefault(node => node.Type == "Rule" && node.ReferenceId == rule.Id)?.Id && string.Equals(edge.Branch, _evaluator.EvaluateRule(args.Metadata, rule) ? "True" : "False", StringComparison.OrdinalIgnoreCase)).Select(edge => Destinations.FirstOrDefault(destination => destination.Id == GraphNodes.FirstOrDefault(node => node.Id == edge.ToNodeId)?.ReferenceId)?.Name).OfType<string>()).Concat(Destinations.Where(x => directDestinationIds.Contains(x.Id)).Select(x => x.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (destinations.Count > 0) await _spooler.EnqueueAsync(args.Dataset, destinations, callingAET: args.RemoteAET);
        Traffic.Insert(0, new TrafficRow { CallingAe = args.RemoteAET, Destination = string.Join(", ", destinations), Ip = "remote", Port = 0, SopClass = args.Dataset.Get(DicomTag.SOPClassUid), StudyUid = args.Dataset.Get(DicomTag.StudyInstanceUid), SeriesUid = args.Dataset.Get(DicomTag.SeriesInstanceUid), SopUid = sop, Size = $"{size / 1024.0:0.0} KB", Duration = "accepted", Status = "Stored" });
        foreach (var destination in destinations) RouteFlow.Insert(0, new RouteRow { Source = args.RemoteAET, Destination = destination, Status = "Pending", Rule = string.Join(", ", matches) });
        RefreshInspector();
        AddEvent("Spool", $"Stored {sop} ({size} bytes)", "Info");
        OnPropertyChanged(nameof(QueueDepth));
    }

    private void RefreshInspector() { InspectorTags.Clear(); if (_inspectedDataset == null) return; foreach (var element in _inspectedDataset.Elements.Where(x => string.IsNullOrWhiteSpace(InspectorFilter) || $"{x.Tag} {x.VR} {x.Text}".Contains(InspectorFilter, StringComparison.OrdinalIgnoreCase))) InspectorTags.Add(new InspectorTag($"({element.Tag})", element.Tag.ToString(), element.Value.Length > 1024 ? $"<binary {element.Value.Length} bytes>" : element.Text, false)); }
    private void OpenLocalFile() { var dialog = new OpenFileDialog { Filter = "DICOM files (*.dcm;*.*)|*.dcm;*.*", Multiselect = false }; if (dialog.ShowDialog() == true) { TestFilePath = dialog.FileName; DumpSelectedFile(); } }
    private void DumpSelectedFile() { try { var file = DicomFileParser.Parse(File.ReadAllBytes(TestFilePath)); _inspectedDataset = file.Dataset; RefreshInspector(); ToolResult = $"Parsed {_inspectedDataset.Elements.Count} elements ({file.TransferSyntax}) from {Path.GetFileName(TestFilePath)}"; } catch (Exception ex) { ToolResult = $"DICOM parse failed: {ex.Message}"; } }
    private void SimulateSelectedFile() { try { if (_inspectedDataset == null) DumpSelectedFile(); if (_inspectedDataset == null) return; var metadata = DicomMetadataForSimulation(_inspectedDataset); var matches = _evaluator.Evaluate(metadata, _configuration.Rules); var details = _configuration.Rules.SelectMany(rule => rule.Conditions.Select(condition => $"{rule.Name}: {condition.Field} {condition.Operator} {condition.Value} => {_evaluator.EvaluateCondition(metadata, condition)}")); ToolResult = string.Join(Environment.NewLine, details.Concat(new[] { matches.Count == 0 ? "No rules matched. Nothing would be sent." : $"Matched without sending: {string.Join(", ", matches)}" })); } catch (Exception ex) { ToolResult = $"Route simulation failed: {ex.Message}"; } }
    private static IDictionary<string, string> DicomMetadataForSimulation(NativeDicomDataset dataset) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Modality"] = dataset.Get(DicomTag.Modality), ["SeriesDescription"] = dataset.Get(DicomTag.SeriesDescription), ["PatientID"] = dataset.Get(DicomTag.PatientId), ["BodyPartExamined"] = dataset.Get(DicomTag.BodyPartExamined), ["SOPClassUID"] = dataset.Get(DicomTag.SOPClassUid), ["StudyDate"] = dataset.Get(DicomTag.StudyDate) }.Concat(dataset.Elements.ToDictionary(x => $"({x.Tag.Group:X4},{x.Tag.Element:X4})", x => x.Text, StringComparer.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    private void RefreshSpool()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "spool"); if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json")) try { var item = JsonSerializer.Deserialize<SpoolItem>(File.ReadAllText(file)); if (item == null) continue; foreach (var delivery in item.Destinations) { var existing = Spool.FirstOrDefault(x => x.SopUid == item.Id && x.Destination == delivery.Name); if (existing == null) Spool.Add(new SpoolRow { SopUid = item.Id, Destination = delivery.Name }); if (existing != null) { existing.Attempts = delivery.Attempts; existing.Error = delivery.LastError; existing.State = delivery.Succeeded ? "Delivered" : delivery.Attempts == 0 ? "Pending" : "Retry scheduled"; } } } catch { }
    }
    private void MoveRule(int direction) { if (Rules.Count < 2) return; var index = Rules.Count - 1; var target = index + direction; if (target < 0 || target >= Rules.Count) return; (Rules[index], Rules[target]) = (Rules[target], Rules[index]); }
    private void LoadConfiguration()
    {
        _configuration = _configurationStore.LoadAsync().GetAwaiter().GetResult();
        if (_configuration.Listeners.Count == 0) _configuration.Listeners.Add(new ListenerConfiguration { Name = "ImageYeeter Main" });
        foreach (var listener in _configuration.Listeners) Listeners.Add(new ListenerRow { Configuration = listener });
        foreach (var destination in _configuration.Destinations) Destinations.Add(destination);
        foreach (var rule in _configuration.Rules) { var row = new RuleEditorRow(rule); row.PropertyChanged += (_, _) => { EnsureGraph(); PersistChanges(); }; Rules.Add(row); }
        foreach (var node in _configuration.GraphNodes) GraphNodes.Add(node); foreach (var edge in _configuration.GraphEdges) GraphEdges.Add(edge);
        if (Destinations.Count == 0) Destinations.Add(new Destination { Name = "PACS", AeTitle = "PACS01", Host = "localhost" });
        EnsureGraph();
    }
    private void EnsureGraph()
    {
        var references = Listeners.Select(x => (Type: "Listener", Id: x.Configuration.Id)).Concat(Rules.Select(x => ("Rule", x.Id))).Concat(Destinations.Select(x => ("Destination", x.Id))).ToHashSet();
        foreach (var stale in GraphNodes.Where(x => !references.Contains((x.Type, x.ReferenceId))).ToList()) { foreach (var edge in GraphEdges.Where(edge => edge.FromNodeId == stale.Id || edge.ToNodeId == stale.Id).ToList()) GraphEdges.Remove(edge); GraphNodes.Remove(stale); }
        foreach (var listener in Listeners) UpsertNode("Listener", listener.Configuration.Id, Friendly(listener), 40, listener.Configuration.Enabled);
        foreach (var rule in Rules) UpsertNode("Rule", rule.Id, $"{rule.Name}  P{rule.Priority}\n{rule.Summary}", 330, rule.Enabled);
        foreach (var destination in Destinations) UpsertNode("Destination", destination.Id, Friendly(destination), 650, destination.Enabled);
    }
    private void UpsertNode(string type, string referenceId, string display, double x, bool enabled) { var node = GraphNodes.FirstOrDefault(x => x.Type == type && x.ReferenceId == referenceId); if (node == null) GraphNodes.Add(node = new GraphNode { Type = type, ReferenceId = referenceId, X = x, Y = 40 + GraphNodes.Count(x => x.Type == type) * 110 }); node.DisplayText = display; node.Enabled = enabled; }
    private static string Friendly(ListenerRow row) => $"{row.Name}\n{row.AeTitle}  {row.Endpoint}\n{row.Status}";
    private static string Friendly(Destination destination) => $"{destination.Name}\n{destination.AeTitle}  {destination.Host}:{destination.Port}\n{(destination.Enabled ? "Enabled" : "Disabled")}";
    private static string Summary(IEnumerable<ConditionEditorRow> conditions) => string.Join(" AND ", conditions.Select(x => $"{x.TagName.Split(" (")[0]} {x.Operator} {x.Value}"));
    private void PersistChanges() => _ = SaveConfigurationAsync();
    public void ConnectEdge(GraphEdge edge)
    {
        var from = GraphNodes.FirstOrDefault(x => x.Id == edge.FromNodeId);
        var to = GraphNodes.FirstOrDefault(x => x.Id == edge.ToNodeId);
        if (from == null || to == null || (from.Type == "Listener" && to.Type is not ("Rule" or "Destination")) || (from.Type == "Rule" && to.Type != "Destination") || from.Type == "Destination") return;
        if (!GraphEdges.Any(x => x.FromNodeId == edge.FromNodeId && x.ToNodeId == edge.ToNodeId && x.Branch == edge.Branch)) GraphEdges.Add(edge);
        if (from.Type == "Rule")
        {
            var rule = Rules.FirstOrDefault(x => x.Id == from.ReferenceId);
            var destination = Destinations.FirstOrDefault(x => x.Id == to.ReferenceId);
            if (rule != null && destination != null && !rule.Destination.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(destination.Name, StringComparer.OrdinalIgnoreCase)) rule.Destination = string.Join(", ", rule.Destination.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Append(destination.Name));
        }
        PersistChanges();
    }
    public void RemoveEdge(GraphEdge edge)
    {
        GraphEdges.Remove(edge);
        var from = GraphNodes.FirstOrDefault(x => x.Id == edge.FromNodeId);
        var to = GraphNodes.FirstOrDefault(x => x.Id == edge.ToNodeId);
        if (from?.Type == "Rule" && to?.Type == "Destination")
        {
            var rule = Rules.FirstOrDefault(x => x.Id == from.ReferenceId);
            var destination = Destinations.FirstOrDefault(x => x.Id == to.ReferenceId);
            if (rule != null && destination != null) rule.Destination = string.Join(", ", rule.Destination.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(x => !string.Equals(x, destination.Name, StringComparison.OrdinalIgnoreCase)));
        }
        PersistChanges();
    }
    public void PersistGraph() => PersistChanges();
    private async Task SaveConfigurationAsync()
    {
        _configuration.Listeners = Listeners.Select(x => x.Configuration).ToList();
        _configuration.Destinations = Destinations.ToList();
        _configuration.Rules = Rules.Select(ToCoreRule).ToList();
        EnsureGraph();
        _configuration.GraphNodes = GraphNodes.ToList(); _configuration.GraphEdges = GraphEdges.ToList();
        await _configurationStore.SaveAsync(_configuration); AddEvent("Configuration", "Configuration saved atomically", "Info");
    }
    private static RoutingRule ToCoreRule(RuleEditorRow row) => new() { Id = row.Id, Name = row.Name, Priority = row.Priority, Enabled = row.Enabled, StopOnMatch = true, ConditionTree = row.RootGroup.ToConditionGroup(), Conditions = new(), DestinationNames = row.Destination.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(), ConditionSummary = row.Summary };
    private static Condition? ParseCondition(string expression)
    {
        var parts = expression.Split(' ', 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); if (parts.Length < 2) return null;
        if (!Enum.TryParse<ConditionOperator>(parts[1], true, out var operation)) return null;
        return new Condition { Field = parts[0], Operator = operation, Value = parts.Length == 3 ? parts[2] : string.Empty };
    }
    private void AddEvent(string category, string message, string level) => Events.Insert(0, new LogRow(DateTime.Now.ToString("HH:mm:ss"), category, message, level));
    public void Dispose() { _timer.Stop(); _spooler.StopProcessingAsync().GetAwaiter().GetResult(); _spooler.Dispose(); _listenerManager.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; OnPropertyChanged(name); } }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TrafficRow { public string CallingAe { get; set; } = "-"; public string Destination { get; set; } = "-"; public string Ip { get; set; } = "-"; public int Port { get; set; } public string SopClass { get; set; } = "-"; public string StudyUid { get; set; } = "-"; public string SeriesUid { get; set; } = "-"; public string SopUid { get; set; } = "-"; public string Size { get; set; } = "-"; public string Duration { get; set; } = "-"; public string Status { get; set; } = "-"; }
public sealed class ListenerRow : INotifyPropertyChanged { private string _status = "Stopped"; public ListenerConfiguration Configuration { get; init; } = new(); public string Name => Configuration.Name; public string Endpoint => $"{Configuration.BindIp}:{Configuration.Port}"; public string AeTitle => Configuration.CalledAeTitle; public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } } public int Associations { get; set; } public long ImagesReceived { get; set; } public long BytesReceived { get; set; } public string LastConnection { get; set; } = "-"; public string LastError { get; set; } = "-"; public event PropertyChangedEventHandler? PropertyChanged; }
public sealed class RouteRow { public string Source { get; set; } = "-"; public string Destination { get; set; } = "-"; public string Status { get; set; } = "-"; public string Rule { get; set; } = "-"; }
public sealed class RuleEditorRow : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _name = "";
    private int _priority;
    private string _destination = "";
    public RuleEditorRow() { RootGroup.Changed += () => Changed(nameof(Summary)); Conditions.CollectionChanged += (_, _) => { RootGroup.Conditions.Clear(); foreach (var condition in Conditions) RootGroup.Conditions.Add(condition); Changed(nameof(Summary)); }; }
    public RuleEditorRow(RoutingRule rule) : this() { Id = rule.Id; Name = rule.Name; Priority = rule.Priority; Enabled = rule.Enabled; Destination = string.Join(", ", rule.DestinationNames); RootGroup = rule.ConditionTree == null ? new ConditionGroupEditorRow() : new ConditionGroupEditorRow(rule.ConditionTree); foreach (var condition in rule.Conditions) RootGroup.Conditions.Add(new ConditionEditorRow(condition)); RootGroup.Changed += () => Changed(nameof(Summary)); }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set { _name = value; Changed(nameof(Name)); } }
    public int Priority { get => _priority; set { _priority = value; Changed(nameof(Priority)); } }
    public bool Enabled { get => _enabled; set { _enabled = value; Changed(nameof(Enabled)); } }
    public string Destination { get => _destination; set { _destination = value; Changed(nameof(Destination)); } }
    public ConditionGroupEditorRow RootGroup { get; private set; } = new();
    public ObservableCollection<ConditionEditorRow> Conditions { get; } = new();
    public string Summary => RootGroup.Summary;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class ConditionEditorRow : INotifyPropertyChanged
{
    private string _tagName = "Modality (0008,0060)";
    private ConditionOperator _operator = ConditionOperator.Equals;
    private string _value = "";
    public ConditionEditorRow() { }
    public ConditionEditorRow(Condition condition) { TagName = condition.Tag switch { 0x00080060 => "Modality (0008,0060)", 0x00081010 => "Station Name (0008,1010)", 0x00080070 => "Manufacturer (0008,0070)", 0x00081090 => "Manufacturer Model Name (0008,1090)", 0x00080080 => "Institution Name (0008,0080)", 0x00081030 => "Study Description (0008,1030)", 0x0008103E => "Series Description (0008,103E)", 0x00180015 => "Body Part Examined (0018,0015)", 0x00181030 => "Protocol Name (0018,1030)", 0x00081050 => "Performing Physician (0008,1050)", 0x00100040 => "Patient Sex (0010,0040)", 0x00080016 => "SOP Class UID (0008,0016)", 0x0020000D => "Study Instance UID (0020,000D)", 0x0020000E => "Series Instance UID (0020,000E)", _ => condition.Field }; Operator = condition.Operator; Value = condition.Value; }
    public string TagName { get => _tagName; set { _tagName = value; PropertyChanged?.Invoke(this, new(nameof(TagName))); } }
    public ConditionOperator Operator { get => _operator; set { _operator = value; PropertyChanged?.Invoke(this, new(nameof(Operator))); } }
    public string Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
    public IReadOnlyList<string> ValueOptions => TagName.StartsWith("Modality", StringComparison.OrdinalIgnoreCase) ? new[] { "CT", "MR", "CR", "DX", "US", "XA", "RF", "NM", "PT", "MG", "SC", "OT" } : Array.Empty<string>();
    public Condition ToCondition() => new() { Tag = TagNumber, FriendlyName = TagName.Split(" (")[0], Field = TagName.Split(" (")[0], Operator = Operator, Value = Value };
    public uint TagNumber => TagName switch { "Modality (0008,0060)" => 0x00080060, "Station Name (0008,1010)" => 0x00081010, "Manufacturer (0008,0070)" => 0x00080070, "Manufacturer Model Name (0008,1090)" => 0x00081090, "Institution Name (0008,0080)" => 0x00080080, "Study Description (0008,1030)" => 0x00081030, "Series Description (0008,103E)" => 0x0008103E, "Body Part Examined (0018,0015)" => 0x00180015, "Protocol Name (0018,1030)" => 0x00181030, "Performing Physician (0008,1050)" => 0x00081050, "Patient Sex (0010,0040)" => 0x00100040, "SOP Class UID (0008,0016)" => 0x00080016, "Study Instance UID (0020,000D)" => 0x0020000D, "Series Instance UID (0020,000E)" => 0x0020000E, _ => ParseCustomTag() };
    private uint ParseCustomTag() { var parts = TagName.Split('(', ')', ','); return parts.Length >= 3 && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var group) && uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var element) ? (group << 16) | element : 0; }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class ConditionGroupEditorRow
{
    public ConditionGroupEditorRow() { }
    public ConditionGroupEditorRow(ConditionGroup group) { Operator = group.Operator; Negate = group.Negate; foreach (var condition in group.Conditions) Conditions.Add(new ConditionEditorRow(condition)); foreach (var child in group.Groups) Groups.Add(new ConditionGroupEditorRow(child)); }
    public ConditionGroupOperator Operator { get; set; } = ConditionGroupOperator.And;
    public ConditionGroupEditorRow? Parent { get; set; }
    public bool Negate { get; set; }
    public ObservableCollection<ConditionEditorRow> Conditions { get; } = new();
    public ObservableCollection<ConditionGroupEditorRow> Groups { get; } = new();
    public Action? Changed { get; set; }
    public ConditionGroup ToConditionGroup() => new() { Operator = Operator, Negate = Negate, Conditions = Conditions.Select(x => x.ToCondition()).ToList(), Groups = Groups.Select(x => x.ToConditionGroup()).ToList() };
    public string Summary => (Negate ? "NOT (" : "") + string.Join($" {Operator.ToString().ToUpperInvariant()} ", Conditions.Select(x => $"{x.TagName.Split(" (")[0]} {x.Operator} {x.Value}").Concat(Groups.Select(x => x.Summary))) + (Negate ? ")" : "");
}
public sealed class SpoolRow : INotifyPropertyChanged { private string _state = "Pending"; public string State { get => _state; set { _state = value; PropertyChanged?.Invoke(this, new(nameof(State))); } } public string SopUid { get; set; } = ""; public string Destination { get; set; } = ""; public int Attempts { get; set; } public string NextRetry { get; set; } = ""; public string Error { get; set; } = ""; public event PropertyChangedEventHandler? PropertyChanged; }
public sealed record LogRow(string Time, string Category, string Message, string Level);
public sealed record InspectorTag(string Tag, string Name, string Value, bool UsedInRouting);
public sealed class ActionCommand(Action action) : ICommand { event EventHandler? ICommand.CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => action(); }
public sealed class AsyncCommand(Func<Task> action, Func<bool>? canExecute = null) : ICommand { event EventHandler? ICommand.CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true; public async void Execute(object? parameter) => await action(); }
public sealed class AsyncObjectCommand(Func<object?, Task> action) : ICommand { event EventHandler? ICommand.CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => true; public async void Execute(object? parameter) => await action(parameter); }
