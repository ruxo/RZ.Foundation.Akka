using System.Runtime.CompilerServices;
using Akka.Actor;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RZ.Foundation.Akka;

[PublicAPI]
public abstract class RzUntypedActor<T>(IServiceProvider sp) : UntypedActor where T : RzUntypedActor<T>
{
    protected readonly ILogger Logger = sp.GetRequiredService<ILogger<T>>();

    /// <summary>
    /// Drain all messages and stop the actor.
    /// </summary>
    protected void FloodAndStop() {
        Become(OnFinalization);
        Self.Tell(PoisonPill.Instance);
    }

    /// <summary>
    /// Log with class and method name.
    /// </summary>
    /// <param name="message">The unhandled message</param>
    /// <param name="caller">The caller's method name</param>
    protected void LogUnhandled(object message, [CallerMemberName] string caller = "") {
        Logger.LogWarning("{Processor}:[{Path}] cannot handle message from sender:[{Sender}]: {@Message}", caller, Self.Path.Name, Sender.Path.Name, message);
    }

    void OnFinalization(object message) {
        Logger.LogDebug("Draining message: {@Message}", message);
    }
}