using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DicomRouter.Core.Services;
using DicomRouter.Infrastructure;
using DicomRouter.Infrastructure.Dicom;
using DicomRouter.Infrastructure.Logging;
using DicomRouter.Infrastructure.Metrics;
using DicomRouter.Infrastructure.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DicomRouter.Service;

/// <summary>
/// Core DICOM router service that manages listeners, routing, and spooling.
/// Runs as a hosted service for dependency injection and graceful shutdown.
/// </summary>
public class DicomRouterService : BackgroundService
{
    private readonly IConfigurationStore _configStore;
    private readonly ILogger<DicomRouterService> _logger;
    private readonly object _configLock = new();
    private RouterConfiguration _configuration = new();
    
    private RoutingPlanner? _planner;
    private DicomForwarder? _forwarder;
    private Spooler? _spooler;
    private ListenerManager? _listeners;
    private ManagementPipeServer? _managementPipe;
    private IMetricsCollector? _metrics;
    private ICorrelationIdManager? _correlationIdManager;

    public DicomRouterService(IConfigurationStore configStore, ILogger<DicomRouterService> logger)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DicomRouter service starting...");

        try
        {
            // Load configuration
            _configuration = await _configStore.LoadAsync();
            if (_configuration.Listeners.Count == 0)
            {
                _configuration.Listeners.Add(new ListenerConfiguration { Name = "ImageYeeter Main" });
                await _configStore.SaveAsync(_configuration, stoppingToken);
            }

            // Initialize components
            _planner = new RoutingPlanner();
            var runtimeEvents = new RuntimeEventBus();
            _forwarder = new DicomForwarder { Events = runtimeEvents };
            
            var spoolPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ImageYeeter", "spool");
            
            _spooler = new Spooler(spoolPath, _forwarder, _configuration.Destinations.ToArray());
            _spooler.StartProcessing();
            _logger.LogInformation("Spooler started at {SpoolPath}", spoolPath);

            // Initialize metrics and correlation tracking
            _metrics = new MetricsCollector();
            _correlationIdManager = new AsyncLocalCorrelationIdManager();

            // Setup listeners with DICOM receive callback
            _listeners = new ListenerManager(ReceiveAsync, runtimeEvents);
            _logger.LogInformation("Listener manager initialized");

            // Setup management pipe for UI communication
            _managementPipe = new ManagementPipeServer(HandleManagementCommandAsync);
            _managementPipe.Start();
            _logger.LogInformation("Management pipe server started");

            // Start configured listeners
            foreach (var listener in _configuration.Listeners.Where(x => x.Enabled && x.AutoStart))
            {
                try
                {
                    await _listeners.StartAsync(listener, stoppingToken);
                    _logger.LogInformation("Listener {ListenerName} started on {IP}:{Port}", 
                        listener.Name, listener.BindIp, listener.Port);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start listener {ListenerName}", listener.Name);
                }
            }

            _logger.LogInformation("DicomRouter service is running");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DicomRouter service cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "DicomRouter service encountered a fatal error");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DicomRouter service stopping...");

        // Graceful shutdown sequence
        try
        {
            // Stop accepting new associations
            if (_listeners != null)
            {
                var listenerIds = _listeners.RunningIds.ToList();
                foreach (var id in listenerIds)
                {
                    await _listeners.StopAsync(id);
                    _logger.LogInformation("Listener {Id} stopped", id);
                }
            }

            // Allow in-flight DICOM transactions to complete
            _logger.LogInformation("Waiting for in-flight transactions to complete...");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            // Drain spool gracefully
            if (_spooler != null)
            {
                _logger.LogInformation("Stopping spooler gracefully...");
                await _spooler.StopProcessingAsync();
            }

            // Close management pipe
            if (_managementPipe != null)
            {
                await _managementPipe.DisposeAsync();
                _logger.LogInformation("Management pipe closed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during graceful shutdown");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("DicomRouter service stopped");
    }

    private async Task ReceiveAsync(DicomReceivedEventArgs args)
    {
        try
        {
            RouterConfiguration active;
            lock (_configLock) active = _configuration;

            if (_planner == null || _spooler == null)
            {
                _logger.LogWarning("Router not ready to receive DICOM");
                return;
            }

            // Propagate correlation ID
            var correlationId = _correlationIdManager?.GetOrCreateCorrelationId();

            var plan = _planner.Plan(args.ListenerId, args.Metadata, active.Rules, 
                active.GraphNodes, active.GraphEdges);
            
            var destinations = active.Destinations
                .Where(d => plan.DestinationIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
                .Select(d => d.Name)
                .ToList();

            if (destinations.Count > 0)
            {
                await _spooler.EnqueueAsync(args.Dataset, destinations, callingAET: args.RemoteAET);
                
                // Record metrics
                _metrics?.RecordImageReceived(100);
                
                _logger.LogInformation("DICOM received: {SOPInstanceUid} with CorrelationId {CorrelationId} → {DestinationCount} destinations",
                    args.Dataset.Get(DicomTag.SOPInstanceUid), correlationId, destinations.Count);
            }
            else
            {
                _logger.LogWarning("DICOM received: {SOPInstanceUid} but no destinations matched",
                    args.Dataset.Get(DicomTag.SOPInstanceUid));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing received DICOM");
        }
    }

    private async Task<ManagementResponse> HandleManagementCommandAsync(
        ManagementRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Command.Equals("get-config", StringComparison.OrdinalIgnoreCase))
            {
                RouterConfiguration active;
                lock (_configLock) active = _configuration;
                
                var metadata = new Dictionary<string, string>
                {
                    ["running-listeners"] = _listeners?.RunningIds.Count.ToString() ?? "0"
                };
                
                return new ManagementResponse(true, "Configuration loaded", active, metadata);
            }

            if (request.Command.Equals("save-config", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Configuration == null)
                    return new ManagementResponse(false, "Configuration required");

                await _configStore.SaveAsync(request.Configuration, cancellationToken);
                await ApplyConfigurationAsync(request.Configuration, cancellationToken);
                
                _logger.LogInformation("Configuration updated via management pipe");
                return new ManagementResponse(true, "Configuration saved and applied");
            }

            if (request.Command.Equals("health", StringComparison.OrdinalIgnoreCase))
            {
                var systemHealth = _metrics?.GetSystemHealth();
                var metadata = new Dictionary<string, string>
                {
                    ["status"] = (systemHealth?.OverallStatus ?? HealthStatus.Unhealthy).ToString(),
                    ["listeners"] = _listeners?.RunningIds.Count.ToString() ?? "0",
                    ["spool-processing"] = _spooler != null ? "true" : "false",
                    ["spool-depth"] = systemHealth?.Metrics.SpoolQueueDepth.ToString() ?? "0",
                    ["avg-latency-ms"] = systemHealth?.Metrics.AverageDeliveryLatencyMs.ToString("F1") ?? "0"
                };
                return new ManagementResponse(true, "Service is healthy", Status: metadata);
            }

            return new ManagementResponse(false, $"Unknown command: {request.Command}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Management command failed: {Command}", request.Command);
            return new ManagementResponse(false, ex.Message);
        }
    }

    private async Task ApplyConfigurationAsync(RouterConfiguration next, CancellationToken cancellationToken)
    {
        RouterConfiguration previous;
        lock (_configLock)
        {
            previous = _configuration;
            _configuration = next;
        }

        if (_spooler != null)
            _spooler.UpdateDestinations(next.Destinations);

        if (_listeners == null) return;

        var oldListeners = previous.Listeners.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var newListeners = next.Listeners.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        // Stop removed or modified listeners
        foreach (var oldListener in previous.Listeners)
        {
            if (!newListeners.TryGetValue(oldListener.Id, out var replacement) ||
                !replacement.Enabled ||
                ListenerChanged(oldListener, replacement))
            {
                await _listeners.StopAsync(oldListener.Id);
                _logger.LogInformation("Listener {Id} stopped", oldListener.Id);
            }
        }

        // Start new or modified listeners
        foreach (var listener in next.Listeners.Where(x => x.Enabled && x.AutoStart))
        {
            if (!oldListeners.TryGetValue(listener.Id, out var old) ||
                ListenerChanged(old, listener) ||
                !_listeners.RunningIds.Contains(listener.Id, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await _listeners.StartAsync(listener, cancellationToken);
                    _logger.LogInformation("Listener {Id} started", listener.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start listener {Id}", listener.Id);
                }
            }
        }
    }

    private static bool ListenerChanged(ListenerConfiguration left, ListenerConfiguration right) =>
        left.Name != right.Name ||
        left.BindIp != right.BindIp ||
        left.Port != right.Port ||
        left.CalledAeTitle != right.CalledAeTitle ||
        left.Enabled != right.Enabled ||
        left.AutoStart != right.AutoStart ||
        left.MaxAssociations != right.MaxAssociations ||
        left.AssociationTimeoutSeconds != right.AssociationTimeoutSeconds ||
        left.ReceiveTimeoutSeconds != right.ReceiveTimeoutSeconds ||
        left.MaxPduSize != right.MaxPduSize;
}
