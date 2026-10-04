using Microsoft.Extensions.Hosting;

namespace Rockets.Listener.Http;

/// <summary>
/// The listener's web app runs inside the main host, which owns the process lifetime. This
/// replaces the console lifetime so the listener does not react to Ctrl+C/SIGTERM on its own;
/// the main host stops it in the right order (see <see cref="HttpMessageListener"/>).
/// </summary>
internal sealed class EmbeddedHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
