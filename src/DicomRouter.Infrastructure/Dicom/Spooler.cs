using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Infrastructure.Dicom
{
    /// <summary>
    /// Simple spooler that persists incoming DICOM files to disk and processes them asynchronously.
    /// </summary>
    public class Spooler : IDisposable
    {
        private readonly string _spoolFolder;
        private readonly DicomForwarder _forwarder;
        private readonly Destination[] _destinations;
        private readonly int _baseRetrySeconds;
        private readonly int _maxAttempts;
        private CancellationTokenSource? _cts;
        private Task? _worker;

        public Spooler(string spoolFolder, DicomForwarder forwarder, Destination[] destinations, int baseRetrySeconds = 30, int maxAttempts = 5)
        {
            _spoolFolder = spoolFolder ?? throw new ArgumentNullException(nameof(spoolFolder));
            _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
            _destinations = destinations ?? Array.Empty<Destination>();
            _baseRetrySeconds = baseRetrySeconds;
            _maxAttempts = maxAttempts;

            Directory.CreateDirectory(_spoolFolder);
        }

        public async Task EnqueueAsync(NativeDicomDataset dataset, IEnumerable<string> destinationNames, IDictionary<string, string>? tagOverrides = null, string callingAET = "")
        {
            var id = Guid.NewGuid().ToString("N");
            var dcmPath = Path.Combine(_spoolFolder, id + ".dcm");
            var metaPath = Path.Combine(_spoolFolder, id + ".json");

            var tempPath = dcmPath + ".tmp";
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                await file.WriteAsync(dataset.OriginalBytes).ConfigureAwait(false);
                await file.FlushAsync().ConfigureAwait(false);
            }
            File.Move(tempPath, dcmPath);

            var item = new SpoolItem
            {
                Id = id,
                DicomFileName = Path.GetFileName(dcmPath),
                DestinationNames = destinationNames?.ToList() ?? new List<string>(),
                Destinations = (destinationNames ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Select(name => new DestinationDelivery { Name = name }).ToList(),
                TagOverrides = tagOverrides != null ? new Dictionary<string, string>(tagOverrides, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Attempts = 0,
                NextAttemptUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                CallingAET = callingAET ?? string.Empty
            };

            var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
            var metaTempPath = metaPath + ".tmp";
            await File.WriteAllTextAsync(metaTempPath, json).ConfigureAwait(false);
            File.Move(metaTempPath, metaPath);
        }

        public void StartProcessing()
        {
            if (_worker != null) return;
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => ProcessLoopAsync(_cts.Token));
        }

        public async Task StopProcessingAsync()
        {
            if (_cts == null) return;
            _cts.Cancel();
            try
            {
                if (_worker != null)
                    await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _worker = null;
                _cts.Dispose();
                _cts = null;
            }
        }

        private async Task ProcessLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var metaFiles = Directory.EnumerateFiles(_spoolFolder, "*.json");
                    foreach (var meta in metaFiles)
                    {
                        if (ct.IsCancellationRequested) break;
                        SpoolItem? item = null;
                        try
                        {
                            var txt = await File.ReadAllTextAsync(meta, ct).ConfigureAwait(false);
                            item = JsonSerializer.Deserialize<SpoolItem>(txt);
                        }
                        catch
                        {
                            // corrupted metadata - remove
                            try { File.Delete(meta); } catch { }
                            continue;
                        }

                        if (item == null) continue;
                        if (item.Destinations.Count == 0 && item.DestinationNames.Count > 0)
                            item.Destinations = item.DestinationNames.Select(name => new DestinationDelivery { Name = name }).ToList();

                        if (item.NextAttemptUtc > DateTime.UtcNow) continue;

                        var dcmPath = Path.Combine(_spoolFolder, item.DicomFileName);
                        if (!File.Exists(dcmPath))
                        {
                            // missing data - remove metadata
                            try { File.Delete(meta); } catch { }
                            continue;
                        }

                        var raw = await File.ReadAllBytesAsync(dcmPath, ct).ConfigureAwait(false);
                        var ds = NativeDicomDataset.Parse(raw, DicomTransferSyntax.ExplicitVrLittleEndian);
                        var pending = item.Destinations.Where(x => !x.Succeeded).Select(async status =>
                        {
                            var dest = _destinations.FirstOrDefault(d => string.Equals(d.Name, status.Name, StringComparison.OrdinalIgnoreCase));
                            status.Attempts++;
                            status.LastAttemptUtc = DateTime.UtcNow;
                            status.LastError = dest == null ? "Unknown destination" : string.Empty;
                            status.Succeeded = dest != null && await _forwarder.ForwardAsync(ds, dest, item.CallingAET, ct).ConfigureAwait(false);
                            if (!status.Succeeded && string.IsNullOrEmpty(status.LastError)) status.LastError = "C-STORE failed";
                            status.NextRetryUtc = DateTime.UtcNow.AddSeconds(_baseRetrySeconds * Math.Pow(2, Math.Max(0, status.Attempts - 1)));
                        });
                        await Task.WhenAll(pending).ConfigureAwait(false);
                        var allSucceeded = item.Destinations.All(x => x.Succeeded);

                        if (allSucceeded)
                        {
                            try
                            {
                                File.Delete(dcmPath);
                                File.Delete(meta);
                            }
                            catch { }
                        }
                        else
                        {
                            item.Attempts = item.Destinations.Count == 0 ? item.Attempts + 1 : item.Destinations.Max(x => x.Attempts);
                            if (item.Destinations.Where(x => !x.Succeeded).All(x => x.Attempts >= _maxAttempts))
                            {
                                // move to failed folder
                                var failedDir = Path.Combine(_spoolFolder, "failed");
                                Directory.CreateDirectory(failedDir);
                                try
                                {
                                    var destDcm = Path.Combine(failedDir, item.DicomFileName);
                                    var destMeta = Path.Combine(failedDir, Path.GetFileName(meta));
                                    File.Move(dcmPath, destDcm, overwrite: true);
                                    File.Move(meta, destMeta, overwrite: true);
                                }
                                catch { }
                            }
                            else
                            {
                                var backoffSeconds = _baseRetrySeconds * Math.Pow(2, item.Attempts - 1);
                                item.NextAttemptUtc = DateTime.UtcNow.AddSeconds(backoffSeconds);
                                var newJson = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
                                try { await File.WriteAllTextAsync(meta, newJson, ct).ConfigureAwait(false); } catch { }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // swallow and continue
                }

                try { await Task.Delay(5000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _worker?.Wait(1000); } catch { }
        }
    }
}
