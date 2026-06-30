using Akka.Actor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RZ.Foundation.Akka.Impl;

sealed class AkkaStartup(ILogger<AkkaStartup> logger, ActorSystem system) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) {
        logger.LogDebug("Akka system {Name} started", system.Name);
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken)
        => system.CoordinatedShutdown(CoordinatedShutdown.ClrExitReason.Instance);
}