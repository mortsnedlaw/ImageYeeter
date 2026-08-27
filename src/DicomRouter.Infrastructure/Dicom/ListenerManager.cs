using System.Net;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Infrastructure.Dicom;

public sealed class ListenerManager : IAsyncDisposable
{
    private readonly Dictionary<string, NativeDicomListener> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DicomReceivedEventArgs, Task> _received;

    public ListenerManager(Func<DicomReceivedEventArgs, Task> received) => _received = received;
    public IReadOnlyCollection<string> RunningIds => _running.Keys;

    public async Task StartAsync(ListenerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Validate(configuration);
        if (_running.ContainsKey(configuration.Id)) return;
        var listener = new NativeDicomListener();
        listener.OnDicomReceived += _received;
        try
        {
            await listener.StartAsync(configuration.CalledAeTitle, configuration.BindIp, configuration.Port).ConfigureAwait(false);
            _running.Add(configuration.Id, listener);
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    public async Task StopAsync(string id)
    {
        if (!_running.Remove(id, out var listener)) return;
        await listener.StopAsync().ConfigureAwait(false);
        listener.Dispose();
    }

    public async Task RestartAsync(ListenerConfiguration configuration)
    {
        await StopAsync(configuration.Id).ConfigureAwait(false);
        await StartAsync(configuration).ConfigureAwait(false);
    }

    public static void Validate(ListenerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!IPAddress.TryParse(configuration.BindIp, out _)) throw new ArgumentException("Bind IP is invalid.", nameof(configuration));
        if (configuration.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(configuration.Port));
        if (string.IsNullOrWhiteSpace(configuration.CalledAeTitle) || configuration.CalledAeTitle.Length > 16) throw new ArgumentException("Called AE title must contain 1-16 characters.", nameof(configuration.CalledAeTitle));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _running.Keys.ToArray()) await StopAsync(id).ConfigureAwait(false);
    }
}
