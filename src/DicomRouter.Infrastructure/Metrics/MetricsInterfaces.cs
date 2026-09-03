using System;
using System.Collections.Generic;

namespace DicomRouter.Infrastructure.Metrics;

/// <summary>
/// Health status of a component.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Component is healthy and operational.
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// Component is degraded but operational.
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// Component is unhealthy and not operational.
    /// </summary>
    Unhealthy = 2
}

/// <summary>
/// Metrics snapshot for operational visibility.
/// </summary>
public class OperationalMetrics
{
    /// <summary>
    /// Number of active DICOM associations.
    /// </summary>
    public int ActiveAssociations { get; set; }

    /// <summary>
    /// DICOM images received per second (moving average).
    /// </summary>
    public double ImagesPerSecond { get; set; }

    /// <summary>
    /// Data throughput in MB/s (moving average).
    /// </summary>
    public double MegabytesPerSecond { get; set; }

    /// <summary>
    /// Number of items currently in spool queue.
    /// </summary>
    public int SpoolQueueDepth { get; set; }

    /// <summary>
    /// Age of oldest item in spool queue (milliseconds).
    /// </summary>
    public long OldestQueueAgeMs { get; set; }

    /// <summary>
    /// Average delivery latency to all destinations (milliseconds).
    /// </summary>
    public double AverageDeliveryLatencyMs { get; set; }

    /// <summary>
    /// Rate of failed deliveries (failures per second).
    /// </summary>
    public double FailureRatePerSecond { get; set; }

    /// <summary>
    /// Current retry rate (items waiting for retry).
    /// </summary>
    public int ItemsWaitingForRetry { get; set; }

    /// <summary>
    /// Disk usage for spool (bytes).
    /// </summary>
    public long SpoolDiskUsageBytes { get; set; }

    /// <summary>
    /// Available disk space for spool (bytes).
    /// </summary>
    public long SpoolAvailableSpaceBytes { get; set; }

    /// <summary>
    /// Timestamp when metrics were captured.
    /// </summary>
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Health information for a specific component.
/// </summary>
public class ComponentHealth
{
    /// <summary>
    /// Name of the component.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Current health status.
    /// </summary>
    public HealthStatus Status { get; set; } = HealthStatus.Healthy;

    /// <summary>
    /// Human-readable message describing the current state.
    /// </summary>
    public string Message { get; set; } = "OK";

    /// <summary>
    /// Component-specific metrics.
    /// </summary>
    public Dictionary<string, object> Details { get; set; } = new();

    /// <summary>
    /// Last time the component was checked.
    /// </summary>
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Overall system health status.
/// </summary>
public class SystemHealth
{
    /// <summary>
    /// Overall system status.
    /// </summary>
    public HealthStatus OverallStatus { get; set; } = HealthStatus.Healthy;

    /// <summary>
    /// Health of individual components.
    /// </summary>
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();

    /// <summary>
    /// Operational metrics.
    /// </summary>
    public OperationalMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Time the health check was performed.
    /// </summary>
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Uptime of the service.
    /// </summary>
    public TimeSpan Uptime { get; set; }

    /// <summary>
    /// Gets the overall status based on component statuses.
    /// </summary>
    public HealthStatus GetDerivedStatus()
    {
        if (Components.Values.Any(c => c.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;

        if (Components.Values.Any(c => c.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;

        return HealthStatus.Healthy;
    }
}

/// <summary>
/// Per-destination delivery metrics.
/// </summary>
public class DestinationMetrics
{
    /// <summary>
    /// Name of the destination.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Current health status.
    /// </summary>
    public HealthStatus Status { get; set; } = HealthStatus.Healthy;

    /// <summary>
    /// Average round-trip latency (milliseconds).
    /// </summary>
    public double LatencyMs { get; set; }

    /// <summary>
    /// Success rate (0-100).
    /// </summary>
    public double SuccessRatePercent { get; set; }

    /// <summary>
    /// Number of items currently pending delivery.
    /// </summary>
    public int PendingItems { get; set; }

    /// <summary>
    /// Number of items waiting for retry.
    /// </summary>
    public int RetryingItems { get; set; }

    /// <summary>
    /// Number of permanently failed items.
    /// </summary>
    public int FailedItems { get; set; }

    /// <summary>
    /// Total items delivered successfully (lifetime).
    /// </summary>
    public long TotalDeliveredLifetime { get; set; }

    /// <summary>
    /// Total items failed (lifetime).
    /// </summary>
    public long TotalFailedLifetime { get; set; }

    /// <summary>
    /// Last error message.
    /// </summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// Time of last successful delivery.
    /// </summary>
    public DateTime? LastSuccessUtc { get; set; }

    /// <summary>
    /// Time of last delivery attempt.
    /// </summary>
    public DateTime? LastAttemptUtc { get; set; }
}

/// <summary>
/// Manages metrics collection and health checks.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Records a DICOM image received.
    /// </summary>
    void RecordImageReceived(long fileSizeBytes);

    /// <summary>
    /// Records a delivery attempt.
    /// </summary>
    void RecordDeliveryAttempt(string destination, bool succeeded, long elapsedMs);

    /// <summary>
    /// Records a spool operation.
    /// </summary>
    void RecordSpoolOperation(int itemCount, long totalSizeBytes);

    /// <summary>
    /// Gets current operational metrics.
    /// </summary>
    OperationalMetrics GetMetrics();

    /// <summary>
    /// Gets system-wide health status.
    /// </summary>
    SystemHealth GetSystemHealth();

    /// <summary>
    /// Gets metrics for a specific destination.
    /// </summary>
    DestinationMetrics? GetDestinationMetrics(string destination);

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    void Reset();
}
