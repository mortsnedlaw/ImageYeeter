using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FellowOakDicom;
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

        public async Task EnqueueAsync(DicomDataset dataset, IEnumerable<string> destinationNames, IDictionary<string, string>? tagOverrides = null, string callingAET = "")
        {
            var id = Guid.NewGuid().ToString("N");
            var dcmPath = Path.Combine(_spoolFolder, id + ".dcm");
            var metaPath = Path.Combine(_spoolFolder, id + ".json");

            var file = new DicomFile(dataset);
            await Task.Run(() => file.Save(dcmPath)).ConfigureAwait(false);

            var item = new SpoolItem
            {
                Id = id,
                DicomFileName = Path.GetFileName(dcmPath),
                DestinationNames = destinationNames?.ToList() ?? new List<string>(),
                TagOverrides = tagOverrides != null ? new Dictionary<string, string>(tagOverrides, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Attempts = 0,
                NextAttemptUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
                CallingAET = callingAET ?? string.Empty
            };

            var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metaPath, json).ConfigureAwait(false);
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

                        if (item.NextAttemptUtc > DateTime.UtcNow) continue;

                        var dcmPath = Path.Combine(_spoolFolder, item.DicomFileName);
                        if (!File.Exists(dcmPath))
                        {
                            // missing data - remove metadata
                            try { File.Delete(meta); } catch { }
                            continue;
                        }

                        var file = await Task.Run(() => DicomFile.Open(dcmPath), ct).ConfigureAwait(false);
                        var ds = file.Dataset;

                        bool allSucceeded = true;

                        foreach (var destName in item.DestinationNames)
                        {
                            var dest = _destinations.FirstOrDefault(d => string.Equals(d.Name, destName, StringComparison.OrdinalIgnoreCase));
                            if (dest == null)
                            {
                                allSucceeded = false; // destination unknown - treat as failure so it will retry (or admin can fix)
                                continue;
                            }

                            try
                            {
                                var success = await _forwarder.ForwardAsync(ds, dest, item.CallingAET).ConfigureAwait(false);
                                if (!success)
                                {
                                    allSucceeded = false;
                                }
                            }
                            catch
                            {
                                allSucceeded = false;
                            }
                        }

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
                            item.Attempts++;
                            if (item.Attempts >= _maxAttempts)
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
