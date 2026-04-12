using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DicomRouter.Core.Models;
using DicomRouter.Core.Services;
using DicomRouter.Infrastructure.Dicom;
using DicomRouter.Infrastructure.Models;
using Microsoft.Extensions.Configuration;

namespace DicomRouter.Service
{
    /// <summary>
    /// Minimal host to run the DICOM listener in interactive mode.
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Starting DicomRouter (interactive)...");

            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .Build();

            var local = config.GetSection("LocalSCP");
            var ae = local.GetValue("AETitle", "DICROUTER");
            var ip = local.GetValue("Ip", "0.0.0.0");
            var port = local.GetValue("Port", 104);

            var dests = config.GetSection("Destinations").Get<Destination[]>();

            var rulesFile = Path.Combine(AppContext.BaseDirectory, "rules.json");
            var rules = Array.Empty<RoutingRule>();
            if (File.Exists(rulesFile))
            {
                rules = JsonSerializer.Deserialize<RoutingRule[]>(await File.ReadAllTextAsync(rulesFile)) ?? Array.Empty<RoutingRule>();
            }

            var listener = new FoDicomListener();
            var evaluator = new RuleEvaluator();
            var forwarder = new DicomForwarder();

            // spool folder - configurable later
            var spoolFolder = Path.Combine(AppContext.BaseDirectory, "spool");
            Directory.CreateDirectory(spoolFolder);

            var spooler = new Spooler(spoolFolder, forwarder, dests ?? Array.Empty<Destination>());
            spooler.StartProcessing();

            listener.OnDicomReceived += async (args) =>
            {
                try
                {
                    var matches = evaluator.Evaluate(args.Metadata, rules);
                    Console.WriteLine($"Received SOPClass={args.Dataset.GetSingleValueOrDefault(Dicom.DicomTag.SOPClassUID, "")}, Matches={string.Join(',', matches)}");

                    // Collect destination names and tag overrides from matched rules
                    var destNames = new List<string>();
                    var tagOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var name in matches)
                    {
                        var rule = rules.FirstOrDefault(r => r.Name == name);
                        if (rule == null) continue;
                        foreach (var dn in rule.DestinationNames)
                        {
                            if (!destNames.Contains(dn, StringComparer.OrdinalIgnoreCase))
                                destNames.Add(dn);
                        }

                        if (rule.TagOverrides != null)
                        {
                            foreach (var kv in rule.TagOverrides)
                                tagOverrides[kv.Key] = kv.Value;
                        }
                    }

                    if (destNames.Count > 0)
                    {
                        // Enqueue to spool for asynchronous forwarding
                        await spooler.EnqueueAsync(args.Dataset, destNames, tagOverrides, args.RemoteAET);
                        Console.WriteLine($"Enqueued to spool for destinations: {string.Join(',', destNames)}");
                    }
                    else
                    {
                        Console.WriteLine("No matching destinations - dropping or sending to fallback if configured.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error processing received dataset: " + ex);
                }
            };

            await listener.StartAsync(ae, ip, port);

            Console.WriteLine($"Listening on {ip}:{port} AE={ae}. Press Enter to stop.");
            Console.ReadLine();

            Console.WriteLine("Stopping...");
            await listener.StopAsync();
            await spooler.StopProcessingAsync();
        }
    }
}
