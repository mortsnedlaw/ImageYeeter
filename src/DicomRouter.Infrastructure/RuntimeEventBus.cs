using System.Threading.Channels;

namespace DicomRouter.Infrastructure;

public enum RuntimeEventType
{
    ListenerStarted, ListenerStopped, AssociationOpened, AssociationClosed,
    DicomObjectReceived, SpoolCommitted, RuleEvaluated, RuleMatched,
    ForwardStarted, ForwardSucceeded, ForwardFailed, RetryScheduled, ProtocolEvent, Error
}

public sealed record RuntimeEvent(RuntimeEventType Type, DateTime TimestampUtc, string Message, string? CorrelationId = null, IReadOnlyDictionary<string, string>? Data = null);

public interface IRuntimeEventBus
{
    bool Publish(RuntimeEvent runtimeEvent);
    IAsyncEnumerable<RuntimeEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class RuntimeEventBus : IRuntimeEventBus
{
    private readonly Channel<RuntimeEvent> _events = Channel.CreateBounded<RuntimeEvent>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = false });
    public bool Publish(RuntimeEvent runtimeEvent) => _events.Writer.TryWrite(runtimeEvent);
    public bool TryRead(out RuntimeEvent runtimeEvent) => _events.Reader.TryRead(out runtimeEvent!);
    public IAsyncEnumerable<RuntimeEvent> ReadAllAsync(CancellationToken cancellationToken = default) => _events.Reader.ReadAllAsync(cancellationToken);
}
