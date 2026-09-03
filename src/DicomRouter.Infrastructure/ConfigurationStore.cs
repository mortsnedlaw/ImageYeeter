using System.Text.Json;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Infrastructure;

public sealed class ConfigurationStore : IConfigurationStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConfigurationStore(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImageYeeter");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "configuration.json");
    }

    public async Task<RouterConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return new RouterConfiguration();
            await using var file = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<RouterConfiguration>(file, _options, cancellationToken).ConfigureAwait(false) ?? new RouterConfiguration();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(RouterConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporary = _path + ".tmp";
            await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, configuration, _options, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, true);
        }
        finally { _gate.Release(); }
    }
}
