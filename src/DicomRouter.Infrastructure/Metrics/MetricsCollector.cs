using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DicomRouter.Infrastructure.Metrics;

/// <summary>
/// Default implementation of metrics collection with moving averages.
/// </summary>
public class MetricsCollector : IMetricsCollector
{
    private readonly object _lock = new();
    private readonly DateTime _startTimeUtc = DateTime.UtcNow;
    private readonly ConcurrentDictionary<string, DestinationMetrics> _destinationMetrics;
    
    private long _totalImagesReceived;
    private long _totalBytesReceived;
    private long _totalDeliveryAttempts;
    private long _totalSuccessfulDeliveries;
    private long _totalFailedDeliveries;
    
    // Moving average tracking (5-minute windows)
    private readonly Queue<(DateTime, long, long)> _receivedHistory = new(); // timestamp, count, bytes
    private readonly Queue<(DateTime, long)> _deliveryHistory = new(); // timestamp, elapsed ms
    private readonly TimeSpan _historyWindow = TimeSpan.FromMinutes(5);
    
    private int _activeAssociations;
    private int _spoolQueueDepth;
    private long _spoolOldestAgeMs;
    private long _spoolDiskUsageBytes;
    private long _spoolAvailableSpaceBytes;

    public MetricsCollector()
    {
        _destinationMetrics = new ConcurrentDictionary<string, DestinationMetrics>(StringComparer.OrdinalIgnoreCase);
    }

    public void RecordImageReceived(long fileSizeBytes)
    {
        lock (_lock)
        {
            _totalImagesReceived++;
            _totalBytesReceived += fileSizeBytes;
            _receivedHistory.Enqueue((DateTime.UtcNow, 1, fileSizeBytes));
            PruneHistory();
        }
    }

    public void RecordDeliveryAttempt(string destination, bool succeeded, long elapsedMs)
    {
        lock (_lock)
        {
            _totalDeliveryAttempts++;
            if (succeeded)
                _totalSuccessfulDeliveries++;
            else
                _totalFailedDeliveries++;

            _deliveryHistory.Enqueue((DateTime.UtcNow, elapsedMs));
            PruneHistory();
        }

        _destinationMetrics.AddOrUpdate(destination,
            new DestinationMetrics { Name = destination, PendingItems = 1 },
            (_, existing) =>
            {
                if (succeeded)
                {
                    existing.LastSuccessUtc = DateTime.UtcNow;
                    existing.TotalDeliveredLifetime++;
                    if (existing.PendingItems > 0) existing.PendingItems--;
                }
                else
                {
                    existing.TotalFailedLifetime++;
                    existing.LastErrorMessage = "Delivery failed";
                }
                existing.LastAttemptUtc = DateTime.UtcNow;
                existing.LatencyMs = elapsedMs;
                existing.SuccessRatePercent = existing.TotalDeliveredLifetime * 100.0 /
                    (existing.TotalDeliveredLifetime + existing.TotalFailedLifetime);
                return existing;
            });
    }

    public void RecordSpoolOperation(int itemCount, long totalSizeBytes)
    {
        lock (_lock)
        {
            _spoolQueueDepth = itemCount;
            _spoolDiskUsageBytes = totalSizeBytes;
        }
    }

    public OperationalMetrics GetMetrics()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var recentReceived = _receivedHistory.Where(x => (now - x.Item1) <= _historyWindow).ToList();
            var recentDeliveries = _deliveryHistory.Where(x => (now - x.Item1) <= _historyWindow).ToList();

            double imagesPerSecond = 0;
            double mbPerSecond = 0;
            if (_historyWindow.TotalSeconds > 0)
            {
                imagesPerSecond = recentReceived.Count / _historyWindow.TotalSeconds;
                mbPerSecond = recentReceived.Sum(x => x.Item3) / (1024 * 1024) / _historyWindow.TotalSeconds;
            }

            double avgLatencyMs = recentDeliveries.Count > 0
                ? recentDeliveries.Average(x => x.Item2)
                : 0;

            double failureRatePerSecond = 0;
            if (_historyWindow.TotalSeconds > 0)
                failureRatePerSecond = _totalFailedDeliveries / _historyWindow.TotalSeconds;

            return new OperationalMetrics
            {
                ActiveAssociations = _activeAssociations,
                ImagesPerSecond = imagesPerSecond,
                MegabytesPerSecond = mbPerSecond,
                SpoolQueueDepth = _spoolQueueDepth,
                OldestQueueAgeMs = _spoolOldestAgeMs,
                AverageDeliveryLatencyMs = avgLatencyMs,
                FailureRatePerSecond = failureRatePerSecond,
                ItemsWaitingForRetry = _destinationMetrics.Values.Sum(d => d.RetryingItems),
                SpoolDiskUsageBytes = _spoolDiskUsageBytes,
                SpoolAvailableSpaceBytes = _spoolAvailableSpaceBytes
            };
        }
    }

    public SystemHealth GetSystemHealth()
    {
        var metrics = GetMetrics();
        var uptime = DateTime.UtcNow - _startTimeUtc;

        var components = new Dictionary<string, ComponentHealth>
        {
            ["Listener"] = new()
            {
                Name = "Listener",
                Status = metrics.ActiveAssociations > 0 ? HealthStatus.Healthy : HealthStatus.Degraded,
                Message = $"{metrics.ActiveAssociations} active associations",
                Details = new() { ["ActiveAssociations"] = metrics.ActiveAssociations }
            },
            ["Spool"] = new()
            {
                Name = "Spool",
                Status = GetSpoolHealth(),
                Message = $"Queue depth: {metrics.SpoolQueueDepth}",
                Details = new()
                {
                    ["QueueDepth"] = metrics.SpoolQueueDepth,
                    ["OldestAgeMs"] = metrics.OldestQueueAgeMs,
                    ["DiskUsageBytes"] = metrics.SpoolDiskUsageBytes,
                    ["AvailableSpaceBytes"] = metrics.SpoolAvailableSpaceBytes
                }
            },
            ["Forwarder"] = new()
            {
                Name = "Forwarder",
                Status = GetForwarderHealth(),
                Message = $"Success rate: {(_totalSuccessfulDeliveries * 100.0 / Math.Max(1, _totalDeliveryAttempts)):F1}%",
                Details = new()
                {
                    ["TotalSuccessful"] = _totalSuccessfulDeliveries,
                    ["TotalFailed"] = _totalFailedDeliveries,
                    ["AvgLatencyMs"] = metrics.AverageDeliveryLatencyMs
                }
            }
        };

        return new SystemHealth
        {
            OverallStatus = new[] { components["Listener"].Status, components["Spool"].Status, components["Forwarder"].Status }
                .Max(),
            Components = components,
            Metrics = metrics,
            Uptime = uptime
        };
    }

    public DestinationMetrics? GetDestinationMetrics(string destination)
    {
        return _destinationMetrics.TryGetValue(destination, out var metrics) ? metrics : null;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _totalImagesReceived = 0;
            _totalBytesReceived = 0;
            _totalDeliveryAttempts = 0;
            _totalSuccessfulDeliveries = 0;
            _totalFailedDeliveries = 0;
            _receivedHistory.Clear();
            _deliveryHistory.Clear();
            _activeAssociations = 0;
            _spoolQueueDepth = 0;
            _spoolOldestAgeMs = 0;
        }
        _destinationMetrics.Clear();
    }

    public void SetActiveAssociations(int count)
    {
        lock (_lock)
        {
            _activeAssociations = count;
        }
    }

    public void SetSpoolAge(long oldestAgeMs)
    {
        lock (_lock)
        {
            _spoolOldestAgeMs = oldestAgeMs;
        }
    }

    public void SetSpoolDiskInfo(long usageBytes, long availableBytes)
    {
        lock (_lock)
        {
            _spoolDiskUsageBytes = usageBytes;
            _spoolAvailableSpaceBytes = availableBytes;
        }
    }

    private void PruneHistory()
    {
        var now = DateTime.UtcNow;
        var cutoff = now - _historyWindow;

        while (_receivedHistory.Count > 0 && _receivedHistory.Peek().Item1 < cutoff)
            _receivedHistory.Dequeue();

        while (_deliveryHistory.Count > 0 && _deliveryHistory.Peek().Item1 < cutoff)
            _deliveryHistory.Dequeue();
    }

    private HealthStatus GetSpoolHealth()
    {
        lock (_lock)
        {
            if (_spoolAvailableSpaceBytes < 100 * 1024 * 1024) // Less than 100 MB
                return HealthStatus.Unhealthy;

            if (_spoolOldestAgeMs > 3600000) // Older than 1 hour
                return HealthStatus.Degraded;

            return HealthStatus.Healthy;
        }
    }

    private HealthStatus GetForwarderHealth()
    {
        if (_totalDeliveryAttempts == 0)
            return HealthStatus.Healthy;

        var successRate = _totalSuccessfulDeliveries * 100.0 / _totalDeliveryAttempts;
        return successRate switch
        {
            >= 95 => HealthStatus.Healthy,
            >= 50 => HealthStatus.Degraded,
            _ => HealthStatus.Unhealthy
        };
    }
}
