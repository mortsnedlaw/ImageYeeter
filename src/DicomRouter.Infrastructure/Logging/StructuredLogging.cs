using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace DicomRouter.Infrastructure.Logging;

/// <summary>
/// Manages correlation IDs for tracing DICOM instances through the entire pipeline.
/// </summary>
public interface ICorrelationIdManager
{
    /// <summary>
    /// Gets the current correlation ID, creating one if needed.
    /// </summary>
    string GetOrCreateCorrelationId();

    /// <summary>
    /// Sets the correlation ID for the current scope.
    /// </summary>
    void SetCorrelationId(string correlationId);

    /// <summary>
    /// Gets the current correlation ID without creating one.
    /// </summary>
    string? GetCorrelationId();

    /// <summary>
    /// Clears the current correlation ID.
    /// </summary>
    void Clear();
}

/// <summary>
/// Thread-safe correlation ID manager using AsyncLocal.
/// </summary>
public class AsyncLocalCorrelationIdManager : ICorrelationIdManager
{
    private static readonly AsyncLocal<string?> CorrelationId = new();

    public string GetOrCreateCorrelationId()
    {
        if (!string.IsNullOrEmpty(CorrelationId.Value))
            return CorrelationId.Value;

        var id = Guid.NewGuid().ToString("N");
        CorrelationId.Value = id;
        return id;
    }

    public void SetCorrelationId(string correlationId)
    {
        CorrelationId.Value = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
    }

    public string? GetCorrelationId() => CorrelationId.Value;

    public void Clear() => CorrelationId.Value = null;
}

/// <summary>
/// Structured logging helper with classified error types.
/// </summary>
public static class StructuredLogging
{
    /// <summary>
    /// Enumeration of classified error categories for operational visibility.
    /// </summary>
    public enum ErrorClassification
    {
        /// <summary>
        /// Network-related error (connection refused, timeout, etc.).
        /// </summary>
        NetworkError,

        /// <summary>
        /// DICOM protocol error (invalid PDU, status code failure, etc.).
        /// </summary>
        DicomProtocolError,

        /// <summary>
        /// File system error (disk full, permission denied, etc.).
        /// </summary>
        FileSystemError,

        /// <summary>
        /// Configuration error (invalid settings, missing parameters, etc.).
        /// </summary>
        ConfigurationError,

        /// <summary>
        /// Unexpected application error (null reference, logic bug, etc.).
        /// </summary>
        ApplicationError,

        /// <summary>
        /// Resource exhaustion (out of memory, thread pool starvation, etc.).
        /// </summary>
        ResourceExhaustion,

        /// <summary>
        /// Authorization/authentication error.
        /// </summary>
        AuthorizationError,

        /// <summary>
        /// Validation error (malformed DICOM, invalid input, etc.).
        /// </summary>
        ValidationError,

        /// <summary>
        /// Destination unreachable or not responding.
        /// </summary>
        DestinationUnreachable
    }

    /// <summary>
    /// Logs an error with classification and context.
    /// </summary>
    public static void LogClassifiedError(
        ILogger logger,
        Exception ex,
        ErrorClassification classification,
        string message,
        string? correlationId = null,
        params object?[] args)
    {
        var context = new Dictionary<string, object>
        {
            ["Classification"] = classification.ToString(),
            ["Exception"] = ex.GetType().Name
        };

        if (!string.IsNullOrEmpty(correlationId))
            context["CorrelationId"] = correlationId;

        using (logger.BeginScope(context))
        {
            logger.LogError(ex, $"[{classification}] {message}", args);
        }
    }

    /// <summary>
    /// Logs DICOM instance processing with correlation ID and metadata.
    /// </summary>
    public static void LogDicomReceived(
        ILogger logger,
        string correlationId,
        string sopInstanceUid,
        string sopClassUid,
        string transferSyntax,
        string callingAET,
        int destinationCount)
    {
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["SOPInstanceUid"] = sopInstanceUid,
            ["SOPClassUid"] = sopClassUid,
            ["TransferSyntax"] = transferSyntax,
            ["CallingAET"] = callingAET,
            ["DestinationCount"] = destinationCount
        }))
        {
            logger.LogInformation(
                "DICOM received: {SOPInstanceUid} from {CallingAET} → {DestinationCount} destinations",
                sopInstanceUid, callingAET, destinationCount);
        }
    }

    /// <summary>
    /// Logs delivery attempt with results.
    /// </summary>
    public static void LogDeliveryAttempt(
        ILogger logger,
        string correlationId,
        string sopInstanceUid,
        string destination,
        bool succeeded,
        int attemptNumber,
        int maxAttempts,
        string? dicomStatusCode = null,
        string? errorMessage = null)
    {
        var scope = new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["SOPInstanceUid"] = sopInstanceUid,
            ["Destination"] = destination,
            ["Attempt"] = $"{attemptNumber}/{maxAttempts}"
        };

        if (!string.IsNullOrEmpty(dicomStatusCode))
            scope["DicomStatus"] = dicomStatusCode;

        using (logger.BeginScope(scope))
        {
            if (succeeded)
            {
                logger.LogInformation(
                    "DICOM delivery succeeded to {Destination} (attempt {Attempt})",
                    destination, attemptNumber);
            }
            else
            {
                logger.LogWarning(
                    "DICOM delivery failed to {Destination} (attempt {Attempt}): {Error}",
                    destination, attemptNumber, errorMessage ?? "Unknown error");
            }
        }
    }

    /// <summary>
    /// Logs spool state transitions.
    /// </summary>
    public static void LogSpoolStateChange(
        ILogger logger,
        string correlationId,
        string itemId,
        string fromState,
        string toState,
        string? reason = null)
    {
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["SpoolItemId"] = itemId,
            ["StateTransition"] = $"{fromState} → {toState}"
        }))
        {
            logger.LogInformation(
                "Spool item {ItemId}: {FromState} → {ToState}",
                itemId, fromState, toState);

            if (!string.IsNullOrEmpty(reason))
                logger.LogInformation("Reason: {Reason}", reason);
        }
    }

    /// <summary>
    /// Classifies an exception for proper error handling.
    /// </summary>
    public static ErrorClassification ClassifyException(Exception ex)
    {
        return ex switch
        {
            TimeoutException or OperationCanceledException => ErrorClassification.NetworkError,
            System.Net.Sockets.SocketException => ErrorClassification.NetworkError,
            System.IO.DirectoryNotFoundException or System.IO.FileNotFoundException => ErrorClassification.FileSystemError,
            System.IO.IOException => ErrorClassification.FileSystemError,
            System.UnauthorizedAccessException => ErrorClassification.AuthorizationError,
            ArgumentException or FormatException => ErrorClassification.ValidationError,
            InvalidOperationException => ErrorClassification.ConfigurationError,
            OutOfMemoryException or StackOverflowException => ErrorClassification.ResourceExhaustion,
            _ => ErrorClassification.ApplicationError
        };
    }
}
