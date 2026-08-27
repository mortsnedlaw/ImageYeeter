using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Infrastructure;

public sealed record ManagementRequest(string Command, RouterConfiguration? Configuration = null);
public sealed record ManagementResponse(bool Success, string Message, RouterConfiguration? Configuration = null, IReadOnlyDictionary<string, string>? Status = null);

public sealed class ManagementPipeClient
{
    private readonly string _pipeName;
    public ManagementPipeClient(string pipeName = "ImageYeeter.Management") => _pipeName = pipeName;

    public async Task<ManagementResponse> SendAsync(ManagementRequest request, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000, cancellationToken).ConfigureAwait(false);
        await WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<ManagementResponse>(pipe, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? throw new EndOfStreamException();
        return JsonSerializer.Deserialize<T>(line) ?? throw new InvalidDataException("Invalid management response.");
    }
}

public sealed class ManagementPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Func<ManagementRequest, CancellationToken, Task<ManagementResponse>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public ManagementPipeServer(Func<ManagementRequest, CancellationToken, Task<ManagementResponse>> handler, string pipeName = "ImageYeeter.Management") { _handler = handler; _pipeName = pipeName; }
    public void Start() => _loop ??= Task.Run(ListenLoopAsync);

    private async Task ListenLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                _ = HandleClientAsync(pipe, _stop.Token);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                var request = await ManagementPipeClient.ReadAsync<ManagementRequest>(pipe, cancellationToken).ConfigureAwait(false);
                var response = await _handler(request, cancellationToken).ConfigureAwait(false);
                await ManagementPipeClient.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) { try { await ManagementPipeClient.WriteAsync(pipe, new ManagementResponse(false, ex.Message), CancellationToken.None).ConfigureAwait(false); } catch { } }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop != null) try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
