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

        var evaluator = new RuleEvaluator();
        var runtimeEvents = new RuntimeEventBus();
        var forwarder = new DicomForwarder { Events = runtimeEvents };
        var spoolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImageYeeter", "spool");
        using var spooler = new Spooler(spoolPath, forwarder, configuration.Destinations.ToArray());
        spooler.StartProcessing();

        async Task ReceiveAsync(DicomReceivedEventArgs args)
        {
            var matches = evaluator.Evaluate(args.Metadata, configuration.Rules);
            var destinations = configuration.Rules.Where(rule => matches.Contains(rule.Name, StringComparer.OrdinalIgnoreCase))
                .SelectMany(rule => rule.DestinationNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (destinations.Count > 0)
                await spooler.EnqueueAsync(args.Dataset, destinations, callingAET: args.RemoteAET).ConfigureAwait(false);
            Console.WriteLine($"Received {args.Dataset.Get(DicomTag.SOPInstanceUid)}; matched {string.Join(", ", matches)}");
        }

        await using var listeners = new ListenerManager(ReceiveAsync, runtimeEvents);
        await using var management = new ManagementPipeServer(async (request, cancellationToken) =>
        {
            if (request.Command.Equals("get-config", StringComparison.OrdinalIgnoreCase))
                return new ManagementResponse(true, "Configuration loaded", configuration, new Dictionary<string, string> { ["listeners"] = listeners.RunningIds.Count.ToString() });
            if (request.Command.Equals("save-config", StringComparison.OrdinalIgnoreCase) && request.Configuration != null)
            {
                await store.SaveAsync(request.Configuration, cancellationToken).ConfigureAwait(false);
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
