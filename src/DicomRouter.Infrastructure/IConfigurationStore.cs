using System.Threading;
using System.Threading.Tasks;
using DicomRouter.Infrastructure.Models;

namespace DicomRouter.Infrastructure;

/// <summary>
/// Interface for configuration storage and loading.
/// </summary>
public interface IConfigurationStore
{
    /// <summary>
    /// Loads the current router configuration asynchronously.
    /// </summary>
    Task<RouterConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves router configuration asynchronously.
    /// </summary>
    Task SaveAsync(RouterConfiguration configuration, CancellationToken cancellationToken = default);
}
