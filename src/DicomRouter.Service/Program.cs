using DicomRouter.Core.Services;
using DicomRouter.Infrastructure;
using DicomRouter.Infrastructure.Dicom;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Starting ImageYeeter service...");
        var store = new ConfigurationStore();
        var configuration = await store.LoadAsync();
        if (configuration.Listeners.Count == 0)
            configuration.Listeners.Add(new ListenerConfiguration { Name = "ImageYeeter Main" });

        var planner = new RoutingPlanner();
        var configurationGate = new object();
        var runtimeEvents = new RuntimeEventBus();
        var forwarder = new DicomForwarder { Events = runtimeEvents };
        var spoolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImageYeeter", "spool");
        using var spooler = new Spooler(spoolPath, forwarder, configuration.Destinations.ToArray());
        spooler.StartProcessing();
        await using var listeners = new ListenerManager(ReceiveAsync, runtimeEvents);

        async Task ReceiveAsync(DicomReceivedEventArgs args)
        {
            RouterConfiguration active;
            lock (configurationGate) active = configuration;
            var plan = planner.Plan(args.ListenerId, args.Metadata, active.Rules, active.GraphNodes, active.GraphEdges);
            var destinations = active.Destinations.Where(destination => plan.DestinationIds.Contains(destination.Id, StringComparer.OrdinalIgnoreCase)).Select(destination => destination.Name).ToList();
            if (destinations.Count > 0)
                await spooler.EnqueueAsync(args.Dataset, destinations, callingAET: args.RemoteAET).ConfigureAwait(false);
            Console.WriteLine($"Received {args.Dataset.Get(DicomTag.SOPInstanceUid)}; evaluated {string.Join(", ", plan.Evaluations.Select(x => $"{x.RuleId}={x.Result}"))}; destinations {string.Join(", ", destinations)}");
        }

        async Task ApplyConfigurationAsync(RouterConfiguration next, CancellationToken cancellationToken)
        {
            RouterConfiguration previous;
            lock (configurationGate) { previous = configuration; configuration = next; }
            spooler.UpdateDestinations(next.Destinations);

            var oldListeners = previous.Listeners.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var newListeners = next.Listeners.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var oldListener in previous.Listeners)
            {
                if (!newListeners.TryGetValue(oldListener.Id, out var replacement) || !replacement.Enabled || ListenerChanged(oldListener, replacement))
                    await listeners.StopAsync(oldListener.Id).ConfigureAwait(false);
            }
            foreach (var listener in next.Listeners.Where(x => x.Enabled && x.AutoStart))
            {
                if (!oldListeners.TryGetValue(listener.Id, out var old) || ListenerChanged(old, listener) || !listeners.RunningIds.Contains(listener.Id, StringComparer.OrdinalIgnoreCase))
                    await listeners.StartAsync(listener, cancellationToken).ConfigureAwait(false);
            }
        }

        static bool ListenerChanged(ListenerConfiguration left, ListenerConfiguration right) =>
            left.Name != right.Name || left.BindIp != right.BindIp || left.Port != right.Port || left.CalledAeTitle != right.CalledAeTitle ||
            left.Enabled != right.Enabled || left.AutoStart != right.AutoStart || left.MaxAssociations != right.MaxAssociations ||
            left.AssociationTimeoutSeconds != right.AssociationTimeoutSeconds || left.ReceiveTimeoutSeconds != right.ReceiveTimeoutSeconds || left.MaxPduSize != right.MaxPduSize;

        await using var management = new ManagementPipeServer(async (request, cancellationToken) =>
        {
            if (request.Command.Equals("get-config", StringComparison.OrdinalIgnoreCase))
            {
                RouterConfiguration active;
                lock (configurationGate) active = configuration;
                return new ManagementResponse(true, "Configuration loaded", active, new Dictionary<string, string> { ["listeners"] = listeners.RunningIds.Count.ToString() });
            }
            if (request.Command.Equals("save-config", StringComparison.OrdinalIgnoreCase) && request.Configuration != null)
            {
                await store.SaveAsync(request.Configuration, cancellationToken).ConfigureAwait(false);
                await ApplyConfigurationAsync(request.Configuration, cancellationToken).ConfigureAwait(false);
                return new ManagementResponse(true, "Configuration saved");
            }
            return new ManagementResponse(false, $"Unknown management command: {request.Command}");
        });
        management.Start();
        foreach (var listener in configuration.Listeners.Where(x => x.Enabled && x.AutoStart))
        {
            try { await listeners.StartAsync(listener).ConfigureAwait(false); }
            catch (Exception ex) { Console.Error.WriteLine($"Listener {listener.Name} failed: {ex.Message}"); }
        }

        Console.WriteLine("ImageYeeter service is running. Press Ctrl+C to stop.");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
