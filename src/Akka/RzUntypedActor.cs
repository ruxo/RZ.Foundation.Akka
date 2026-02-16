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

    protected IServiceProvider Services => sp;

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

    /// <summary>
    /// Simplifies calling methods with ValueTask return by reducing closure capture.
    /// Executes the task with the provided message and configures awaits appropriately.
    /// </summary>
    /// <typeparam name="A">The type of the message</typeparam>
    /// <param name="message">The message to pass to the task</param>
    /// <param name="task">The async task to execute</param>
    protected void Run<A>(A message, Func<A, ValueTask> task)
        => RunTask(async () => await task(message).ConfigureAwait(false));

    /// <summary>
    /// Simplifies calling methods with ValueTask&lt;Outcome&lt;Unit&gt;&gt; return by reducing closure capture.
    /// Executes the task with the provided message, configures awaits appropriately, and automatically
    /// logs any outcome errors with full message context.
    /// </summary>
    /// <typeparam name="A">The type of the message</typeparam>
    /// <param name="message">The message to pass to the task</param>
    /// <param name="task">The async task that returns an Outcome to execute</param>
    protected void Run<A>(A message, Func<A, ValueTask<Outcome<Unit>>> task)
        => RunTask(async () => {
            if (Fail(await task(message).ConfigureAwait(false), out var e))
                Logger.LogError("Error from processing message {MessageType}: {@Error}", typeof(A).Name, new { Message = message, Error = e });
        });

    /// <summary>
    /// Handles messages during actor finalization after <see cref="FloodAndStop"/> is called.
    /// Override this method to implement custom logic for processing messages during shutdown.
    /// </summary>
    /// <param name="message">The message being drained from the actor's mailbox</param>
    /// <remarks>
    /// The default implementation logs each drained message at Debug level.
    /// This method is invoked for all messages remaining in the actor's mailbox when the actor is stopping.
    /// </remarks>
    protected virtual void OnFinalization(object message) {
        Logger.LogDebug("Finalizing [{Path}]: Draining message: {@Message}", Self.Path.Name, message);
    }
}