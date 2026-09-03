using System;
using System.Collections.Generic;

namespace DicomRouter.Infrastructure.Models;

/// <summary>
/// Enumeration of possible states for a spooled DICOM instance.
/// </summary>
public enum SpoolItemState
{
    /// <summary>
    /// Freshly received, waiting for first delivery attempt.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Currently being sent to one or more destinations.
    /// </summary>
    Sending = 1,

    /// <summary>
    /// Successfully delivered to all destinations.
    /// </summary>
    Delivered = 2,

    /// <summary>
    /// Delivery failed but awaiting retry (backoff in progress).
    /// </summary>
    RetryWaiting = 3,

    /// <summary>
    /// Permanently failed after max retries or terminal error.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Explicitly cancelled by user action.
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// Moved to dead letter queue (corrupted or undeliverable).
    /// </summary>
    DeadLetter = 6
}

/// <summary>
/// Represents the delivery status of a DICOM instance to a single destination.
/// </summary>
public class DestinationDelivery
{
    public string Name { get; set; } = string.Empty;
    public int Attempts { get; set; } = 0;
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextRetryUtc { get; set; }
    public bool Succeeded { get; set; } = false;
    public bool Cancelled { get; set; } = false;
    public string? LastError { get; set; }
    
    /// <summary>
    /// DICOM status code from C-STORE response (if available).
    /// Hex format, e.g., "0x0000" for success.
    /// </summary>
    public string? DicomStatusCode { get; set; }

    /// <summary>
    /// Current delivery state.
    /// </summary>
    public SpoolItemState State
    {
        get
        {
            if (Cancelled) return SpoolItemState.Cancelled;
            if (Succeeded) return SpoolItemState.Delivered;
            if (Attempts == 0) return SpoolItemState.Pending;
            if (NextRetryUtc > DateTime.UtcNow) return SpoolItemState.RetryWaiting;
            return SpoolItemState.Sending;
        }
    }
}

/// <summary>
/// Represents a spooled DICOM instance with full audit trail and state machine semantics.
/// </summary>
public class SpoolItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>
    /// Unique correlation ID for this instance throughout its lifecycle.
    /// Used for tracing across all subsystems.
    /// </summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    
    public string DicomFileName { get; set; } = string.Empty;
    
    /// <summary>
    /// DICOM file size in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; } = 0;
    
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    
    /// <summary>
    /// Transfer syntax UID used for this instance.
    /// </summary>
    public string TransferSyntax { get; set; } = "1.2.840.10008.1.2";
    
    /// <summary>
    /// SOP Class UID of the instance.
    /// </summary>
    public string SopClassUid { get; set; } = string.Empty;
    
    /// <summary>
    /// SOP Instance UID of the instance.
    /// </summary>
    public string SopInstanceUid { get; set; } = string.Empty;
    
    /// <summary>
    /// Calling AE Title from the source association.
    /// </summary>
    public string CallingAET { get; set; } = string.Empty;
    
    public List<string> DestinationNames { get; set; } = new();
    public List<DestinationDelivery> Destinations { get; set; } = new();
    
    /// <summary>
    /// Tag overrides to apply before forwarding (e.g., PatientID remapping).
    /// </summary>
    public Dictionary<string, string> TagOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    public int Attempts { get; set; } = 0;
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Overall state of this item.
    /// </summary>
    public SpoolItemState State
    {
        get
        {
            if (Destinations.Count == 0) return SpoolItemState.Pending;
            
            var states = Destinations.Select(d => d.State).Distinct().ToList();
            
            // If all delivered, done
            if (states.Count == 1 && states[0] == SpoolItemState.Delivered)
                return SpoolItemState.Delivered;
            
            // If all cancelled, cancelled
            if (states.Count == 1 && states[0] == SpoolItemState.Cancelled)
                return SpoolItemState.Cancelled;
            
            // If any sending, report sending
            if (states.Contains(SpoolItemState.Sending))
                return SpoolItemState.Sending;
            
            // If any retry waiting, report waiting
            if (states.Contains(SpoolItemState.RetryWaiting))
                return SpoolItemState.RetryWaiting;
            
            // If any failed, report failed
            if (states.Contains(SpoolItemState.Failed))
                return SpoolItemState.Failed;
            
            // Otherwise pending
            return SpoolItemState.Pending;
        }
    }

    /// <summary>
    /// Gets destinations that are still pending delivery.
    /// </summary>
    public IEnumerable<DestinationDelivery> GetPendingDeliveries() =>
        Destinations.Where(d => !d.Succeeded && !d.Cancelled);

    /// <summary>
    /// Checks if this item should be retried.
    /// </summary>
    public bool ShouldRetry(int maxAttempts) =>
        GetPendingDeliveries().Any(d => d.Attempts < maxAttempts && NextAttemptUtc <= DateTime.UtcNow);

    /// <summary>
    /// Checks if this item is permanently failed.
    /// </summary>
    public bool IsPermanentlyFailed(int maxAttempts) =>
        GetPendingDeliveries().All(d => d.Attempts >= maxAttempts);

    /// <summary>
    /// Checks if all deliveries have completed (successfully or with cancellation/failure).
    /// </summary>
    public bool IsCompleted() =>
        Destinations.All(d => d.Succeeded || d.Cancelled || d.State == SpoolItemState.Failed);

    /// <summary>
    /// Marks this item as failed and moves it to dead letter queue.
    /// </summary>
    public void MoveToDeadLetter(string reason)
    {
        foreach (var delivery in Destinations.Where(d => !d.Succeeded))
        {
            delivery.Cancelled = true;
            delivery.LastError = reason;
        }
        CompletedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a summary for logging/diagnostics.
    /// </summary>
    public string GetSummary() =>
        $"[{CorrelationId}] {SopInstanceUid} ({State}): " +
        $"{Destinations.Count(d => d.Succeeded)}/{Destinations.Count} delivered, " +
        $"attempts={Attempts}, next={NextAttemptUtc:u}";
}
