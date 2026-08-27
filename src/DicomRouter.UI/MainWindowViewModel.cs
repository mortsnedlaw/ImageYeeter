using System.Collections.ObjectModel;
using System.ComponentModel;
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
        AddRuleCommand = new ActionCommand(() => Rules.Add(new RuleEditorRow { Name = "New rule", Priority = Rules.Count + 1 }));
        DeleteRuleCommand = new ActionCommand(() => { if (Rules.Count > 0) Rules.Remove(Rules[^1]); });
        MoveRuleUpCommand = new ActionCommand(() => MoveRule(-1));
        MoveRuleDownCommand = new ActionCommand(() => MoveRule(1));
        AddDestinationCommand = new ActionCommand(() => Destinations.Add(new Destination { Name = "new-destination", AeTitle = "REMOTE", Host = "localhost" }));
        DeleteDestinationCommand = new ActionCommand(() => { if (Destinations.Count > 0) Destinations.RemoveAt(Destinations.Count - 1); });
        RetryCommand = new ActionCommand(() => { foreach (var item in Spool.Where(x => x.State == "Failed")) item.State = "Pending"; OnPropertyChanged(nameof(QueueDepth)); });
        RetryAllCommand = new ActionCommand(() => { foreach (var item in Spool.Where(x => x.State is "Failed" or "Dead Letter")) item.State = "Pending"; OnPropertyChanged(nameof(QueueDepth)); });
        CancelCommand = new ActionCommand(() => { if (Spool.Count > 0) Spool[0].State = "Cancelled"; });
        InspectCommand = new ActionCommand(() => { SelectedTab = "Inspector"; RefreshInspector(); });
        TestEchoCommand = new AsyncCommand(TestEchoAsync);
        OpenFileCommand = new ActionCommand(OpenLocalFile);
        DumpTagsCommand = new ActionCommand(DumpSelectedFile);
        WhichRoutesCommand = new ActionCommand(SimulateSelectedFile);
        SaveConfigurationCommand = new AsyncCommand(SaveConfigurationAsync);
        AddListenerCommand = new ActionCommand(() => { var listener = new ListenerConfiguration { Name = $"Listener {Listeners.Count + 1}", Port = 104 + Listeners.Count + 1 }; Listeners.Add(new ListenerRow { Configuration = listener }); EnsureGraph(); });
        DeleteListenerCommand = new ActionCommand(() => { if (Listeners.Count > 1) Listeners.RemoveAt(Listeners.Count - 1); EnsureGraph(); });
        LoadConfiguration();
        _spooler = new Spooler(Path.Combine(AppContext.BaseDirectory, "spool"), _forwarder, Destinations.ToArray());
        _spooler.StartProcessing();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { DrainRuntimeEvents(); Throughput = IncomingImages == 0 ? 0 : Math.Round(0.02 + QueueDepth * 0.001, 2); RefreshSpool(); OnPropertyChanged(nameof(QueueDepth)); OnPropertyChanged(nameof(FailedDeliveries)); };
        _timer.Start();
    }

    private async Task StartScpAsync()
    {
        try { var listener = Listeners.FirstOrDefault() ?? new ListenerRow { Configuration = new ListenerConfiguration { BindIp = LocalIp, Port = LocalPort, CalledAeTitle = LocalAeTitle }, Status = "Stopped" }; await _listenerManager.StartAsync(listener.Configuration); listener.Status = "Running"; ScpRunning = true; ScpState = "LISTENING"; AddEvent("Association", $"SCP listening on {listener.Configuration.BindIp}:{listener.Configuration.Port}", "Info"); }
        catch (Exception ex) { LastError = ex.Message; ScpState = "ERROR"; AddEvent("Error", ex.Message, "Error"); }
    }

    private async Task StopScpAsync() { foreach (var listener in Listeners.ToArray()) await _listenerManager.StopAsync(listener.Configuration.Id); foreach (var listener in Listeners) listener.Status = "Stopped"; ScpRunning = false; ScpState = "STOPPED"; AddEvent("Association", "SCP stopped", "Info"); }
    private async Task TestEchoAsync() { var watch = System.Diagnostics.Stopwatch.StartNew(); ToolResult = $"C-ECHO to {TestAeTitle}@{TestHost}:{TestPort}..."; var ok = await _forwarder.EchoAsync(TestHost, TestPort, TestAeTitle, TestCallingAe); watch.Stop(); ToolResult = ok ? $"Success · {watch.ElapsedMilliseconds} ms · 1 presentation context" : "Failed · no association"; AddEvent("DIMSE", $"C-ECHO {TestAeTitle}: {ToolResult}", ok ? "Info" : "Error"); }
    private void DrainRuntimeEvents() { var count = 0; while (count++ < 250 && _runtimeEvents.TryRead(out var runtimeEvent)) AddEvent(runtimeEvent.Type.ToString(), runtimeEvent.Message, runtimeEvent.Type == RuntimeEventType.Error || runtimeEvent.Type == RuntimeEventType.ForwardFailed ? "Error" : "Info"); }

    private async Task OnReceivedAsync(DicomReceivedEventArgs args)
    {
        IncomingImages++; var size = args.RawDataset.Length; var sop = args.Dataset.Get(DicomTag.SOPInstanceUid); _inspectedDataset = args.Dataset;
        var matches = _evaluator.Evaluate(args.Metadata, _configuration.Rules); var destinations = _configuration.Rules.Where(x => matches.Contains(x.Name)).SelectMany(x => x.DestinationNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
    private void SimulateSelectedFile() { try { if (_inspectedDataset == null) DumpSelectedFile(); if (_inspectedDataset == null) return; var metadata = DicomMetadataForSimulation(_inspectedDataset); var matches = _evaluator.Evaluate(metadata, _configuration.Rules); ToolResult = matches.Count == 0 ? "No rules matched. Nothing would be sent." : $"Matched without sending: {string.Join(", ", matches)}"; } catch (Exception ex) { ToolResult = $"Route simulation failed: {ex.Message}"; } }
    private static IDictionary<string, string> DicomMetadataForSimulation(NativeDicomDataset dataset) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Modality"] = dataset.Get(DicomTag.Modality), ["SeriesDescription"] = dataset.Get(DicomTag.SeriesDescription), ["PatientID"] = dataset.Get(DicomTag.PatientId), ["BodyPartExamined"] = dataset.Get(DicomTag.BodyPartExamined), ["SOPClassUID"] = dataset.Get(DicomTag.SOPClassUid), ["StudyDate"] = dataset.Get(DicomTag.StudyDate) };
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
        foreach (var rule in _configuration.Rules) Rules.Add(new RuleEditorRow { Name = rule.Name, Priority = rule.Priority, Enabled = rule.Enabled, Expression = string.Join(" AND ", rule.Conditions.Select(x => $"{x.Field} {x.Operator} {x.Value}")), Destination = string.Join(", ", rule.DestinationNames) });
        foreach (var node in _configuration.GraphNodes) GraphNodes.Add(node); foreach (var edge in _configuration.GraphEdges) GraphEdges.Add(edge);
        if (Destinations.Count == 0) Destinations.Add(new Destination { Name = "PACS", AeTitle = "PACS01", Host = "localhost" });
        EnsureGraph();
    }
    private void EnsureGraph()
    {
        foreach (var listener in Listeners) if (!GraphNodes.Any(x => x.ReferenceId == listener.Configuration.Id)) GraphNodes.Add(new GraphNode { Type = "Listener", ReferenceId = listener.Configuration.Id, X = 40, Y = 40 + GraphNodes.Count * 120 });
        foreach (var rule in Rules) if (!GraphNodes.Any(x => x.ReferenceId == rule.Name)) GraphNodes.Add(new GraphNode { Type = "Rule", ReferenceId = rule.Name, X = 300, Y = 60 + GraphNodes.Count * 100 });
        foreach (var destination in Destinations) if (!GraphNodes.Any(x => x.ReferenceId == destination.Id)) GraphNodes.Add(new GraphNode { Type = "Destination", ReferenceId = destination.Id, X = 600, Y = 60 + GraphNodes.Count * 100 });
        var listenerNodes = GraphNodes.Where(x => x.Type == "Listener").ToList();
        var ruleNodes = GraphNodes.Where(x => x.Type == "Rule").ToList();
        var destinationNodes = GraphNodes.Where(x => x.Type == "Destination").ToList();
        foreach (var listener in listenerNodes)
            foreach (var rule in ruleNodes)
                AddEdgeIfMissing(listener.Id, rule.Id);
        foreach (var rule in Rules)
        {
            var ruleNode = ruleNodes.FirstOrDefault(x => x.ReferenceId == rule.Name);
            if (ruleNode == null) continue;
            foreach (var destinationName in rule.Destination.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var destination = Destinations.FirstOrDefault(x => string.Equals(x.Name, destinationName, StringComparison.OrdinalIgnoreCase));
                var destinationNode = destinationNodes.FirstOrDefault(x => x.ReferenceId == destination?.Id);
                if (destinationNode != null) AddEdgeIfMissing(ruleNode.Id, destinationNode.Id);
            }
        }
    }
    private void AddEdgeIfMissing(string from, string to) { if (!GraphEdges.Any(x => x.FromNodeId == from && x.ToNodeId == to)) GraphEdges.Add(new GraphEdge { FromNodeId = from, ToNodeId = to }); }
    private async Task SaveConfigurationAsync()
    {
        _configuration.Listeners = Listeners.Select(x => x.Configuration).ToList();
        _configuration.Destinations = Destinations.ToList();
        _configuration.Rules = Rules.Select(ToCoreRule).ToList();
        EnsureGraph();
        _configuration.GraphNodes = GraphNodes.ToList(); _configuration.GraphEdges = GraphEdges.ToList();
        await _configurationStore.SaveAsync(_configuration); AddEvent("Configuration", "Configuration saved atomically", "Info");
    }
    private static RoutingRule ToCoreRule(RuleEditorRow row) => new() { Name = row.Name, Priority = row.Priority, Enabled = row.Enabled, StopOnMatch = true, Conditions = row.Expression.Split(" AND ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(ParseCondition).Where(x => x != null).Cast<Condition>().ToList(), DestinationNames = row.Destination.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() };
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
public sealed class RuleEditorRow : INotifyPropertyChanged { private bool _enabled = true; public string Name { get; set; } = ""; public int Priority { get; set; } public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); } } public string Expression { get; set; } = ""; public string Destination { get; set; } = ""; public string Operators { get; set; } = "Equals · Contains · StartsWith · EndsWith · Regex · Exists · > · <"; public event PropertyChangedEventHandler? PropertyChanged; }
public sealed class SpoolRow : INotifyPropertyChanged { private string _state = "Pending"; public string State { get => _state; set { _state = value; PropertyChanged?.Invoke(this, new(nameof(State))); } } public string SopUid { get; set; } = ""; public string Destination { get; set; } = ""; public int Attempts { get; set; } public string NextRetry { get; set; } = ""; public string Error { get; set; } = ""; public event PropertyChangedEventHandler? PropertyChanged; }
public sealed record LogRow(string Time, string Category, string Message, string Level);
public sealed record InspectorTag(string Tag, string Name, string Value, bool UsedInRouting);
public sealed class ActionCommand(Action action) : ICommand { event EventHandler? ICommand.CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => action(); }
public sealed class AsyncCommand(Func<Task> action, Func<bool>? canExecute = null) : ICommand { event EventHandler? ICommand.CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true; public async void Execute(object? parameter) => await action(); }
