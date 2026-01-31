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

    protected void Run<A>(A message, Func<A, ValueTask> task)
        => RunTask(async () => await task(message).ConfigureAwait(false));

    protected void Run<A>(A message, Func<A, ValueTask<Outcome<Unit>>> task)
        => RunTask(async () => {
            if (Fail(await task(message).ConfigureAwait(false), out var e))
                Logger.LogError("Error from processing message {MessageType}: {@Error}", typeof(A).Name, new { Message = message, Error = e });
        });

    protected virtual void OnFinalization(object message) {
        Logger.LogDebug("Finalizing [{Path}]: Draining message: {@Message}", Self.Path.Name, message);
    }
}