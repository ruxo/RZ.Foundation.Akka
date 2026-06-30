using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Dispatch;
using JetBrains.Annotations;
using LanguageExt.UnitsOfMeasure;
using RZ.Foundation.Types;
using CS = Akka.Actor.CoordinatedShutdown;
// ReSharper disable CheckNamespace

namespace RZ.Foundation.Akka;

/// <summary>
/// Extension methods over <see cref="ActorSystem"/>, <see cref="IUntypedActorContext"/>, and <see cref="ICanTell"/>
/// for dependency-injection-aware actor creation, safe reply helpers built on <see cref="Outcome{T}"/>,
/// and exception-converting ask patterns.
/// </summary>
[PublicAPI]
public static class ActorExtension
{
    extension(ActorSystem sys)
    {
        /// <summary>
        /// Builds DI-aware <see cref="Props"/> for actor type <typeparamref name="T"/> using Akka's
        /// <see cref="DependencyResolver"/>, so the actor's constructor dependencies are resolved from the service provider.
        /// </summary>
        /// <typeparam name="T">The actor type to create props for.</typeparam>
        /// <param name="parameters">Additional constructor arguments passed alongside the DI-resolved ones.</param>
        /// <returns>Props that construct <typeparamref name="T"/> with its dependencies injected.</returns>
        [PublicAPI, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Props DependencyProps<T>(params object[] parameters) where T : ActorBase =>
            DependencyResolver.For(sys).Props<T>(parameters);

        /// <summary>
        /// Runs Akka's <see cref="T:Akka.Actor.CoordinatedShutdown"/> for this actor system.
        /// </summary>
        /// <param name="reason">The shutdown reason; defaults to <c>CoordinatedShutdown.ClrExitReason</c> when not supplied.</param>
        /// <returns>A task that completes when the coordinated shutdown sequence has finished.</returns>
        [PublicAPI]
        public Task CoordinatedShutdown(CS.Reason? reason = null)
            => CS.Get(sys).Run(reason ?? CS.ClrExitReason.Instance);

        /// <summary>
        /// Creates a top-level DI-aware actor of type <typeparamref name="T"/>, allowing <paramref name="setter"/>
        /// to adjust the generated <see cref="Props"/> (for example to set a mailbox or dispatcher) before the actor is spawned.
        /// </summary>
        /// <typeparam name="T">The actor type to create.</typeparam>
        /// <param name="name">The name of the actor.</param>
        /// <param name="setter">A transform applied to the DI-resolved props before the actor is created.</param>
        /// <param name="parameters">Additional constructor arguments for the actor.</param>
        /// <returns>A reference to the newly created actor.</returns>
        [PublicAPI]
        public IActorRef CreateWithProps<T>(string name, Func<Props,Props> setter, params object[] parameters) where T : ActorBase =>
            sys.ActorOf(setter(sys.DependencyProps<T>(parameters)), name);

        /// <summary>
        /// Creates a top-level DI-aware actor of type <typeparamref name="T"/> using its default DI-resolved <see cref="Props"/>.
        /// </summary>
        /// <typeparam name="T">The actor type to create.</typeparam>
        /// <param name="name">The name of the actor.</param>
        /// <param name="parameters">Additional constructor arguments for the actor.</param>
        /// <returns>A reference to the newly created actor.</returns>
        [PublicAPI]
        public IActorRef CreateActor<T>(string name, params object[] parameters) where T : ActorBase =>
            sys.ActorOf(sys.DependencyProps<T>(parameters), name);
    }

    /// <summary>
    /// Creates a DI-aware child actor of type <typeparamref name="T"/> within this actor context, using its
    /// default DI-resolved <see cref="Props"/>.
    /// </summary>
    /// <typeparam name="T">The actor type to create.</typeparam>
    /// <param name="context">The context whose <see cref="ActorSystem"/> resolves the dependencies and under which the actor is created.</param>
    /// <param name="name">The name of the child actor.</param>
    /// <param name="parameters">Additional constructor arguments for the actor.</param>
    /// <returns>A reference to the newly created child actor.</returns>
    [PublicAPI]
    public static IActorRef CreateActor<T>(this IUntypedActorContext context, string name, params object[] parameters) where T : ActorBase =>
        context.ActorOf(context.System.DependencyProps<T>(parameters), name);

    extension(ICanTell target)
    {
        /// <summary>
        /// Awaits <paramref name="message"/> on the actor task scheduler and replies to <paramref name="sender"/>
        /// with the resulting <see cref="Outcome{T}"/>: a success reply on success, or a failed reply on error
        /// (optionally transformed by <paramref name="errorMapper"/>). The work runs on the actor's scheduler so it
        /// never throws into the actor.
        /// </summary>
        /// <typeparam name="T">The success payload type.</typeparam>
        /// <param name="message">The asynchronous outcome to await and reply with.</param>
        /// <param name="errorMapper">Optional transform applied to the <see cref="ErrorInfo"/> before sending a failed reply.</param>
        /// <param name="sender">The actor recorded as the sender of the reply; defaults to no sender.</param>
        [PublicAPI]
        public void SafeRespond<T>(ValueTask<Outcome<T>> message, Func<ErrorInfo, ErrorInfo>? errorMapper = null, IActorRef? sender = null) {
            ActorTaskScheduler.RunTask(async () => {
                if (Fail(await message.ConfigureAwait(false), out var error, out var result))
                    target.RespondError<T>(errorMapper?.Invoke(error) ?? error, sender);
                else
                    target.RespondSuccess(result, sender);
            });
        }

        /// <summary>
        /// Sends a successful <see cref="Outcome{T}"/> reply wrapping <paramref name="data"/> to the target.
        /// </summary>
        /// <typeparam name="T">The success payload type.</typeparam>
        /// <param name="data">The value to wrap in a successful outcome.</param>
        /// <param name="sender">The actor recorded as the sender of the reply; defaults to no sender.</param>
        [PublicAPI]
        public void RespondSuccess<T>(in T data, IActorRef? sender = null)
            => target.Tell(SuccessOutcome(data), sender ?? ActorRefs.NoSender);

        /// <summary>
        /// Sends a failed <see cref="Outcome{T}"/> reply carrying <paramref name="error"/> to the target.
        /// </summary>
        /// <typeparam name="T">The success payload type of the failed outcome.</typeparam>
        /// <param name="error">The error to report.</param>
        /// <param name="sender">The actor recorded as the sender of the reply; defaults to no sender.</param>
        [PublicAPI]
        public void RespondError<T>(ErrorInfo error, IActorRef? sender = null)
            => target.Tell(FailedOutcome<T>(error), sender ?? ActorRefs.NoSender);

        /// <summary>
        /// Obsolete. Awaits <paramref name="message"/> on the actor task scheduler and replies with <c>unit</c> on
        /// success or a <see cref="Status.Failure"/> (optionally mapped by <paramref name="errorMapper"/>) on error.
        /// </summary>
        /// <param name="message">The asynchronous work to await.</param>
        /// <param name="errorMapper">Optional transform applied to the thrown exception before sending the failure reply.</param>
        /// <param name="sender">The actor recorded as the sender of the reply; defaults to no sender.</param>
        /// <remarks>Use <see cref="SafeRespond{T}"/> instead, which replies with an <see cref="Outcome{T}"/> rather than a <see cref="Status.Failure"/>.</remarks>
        [PublicAPI, Obsolete("Use SafeRespond instead")]
        public void Respond(ValueTask message,
                            Func<Exception, Exception>? errorMapper = null,
                            IActorRef? sender = null) {
            ActorTaskScheduler.RunTask(async () => {
                var (error, _) = await Try(message).ConfigureAwait(false);
                var finalSender = sender ?? ActorRefs.NoSender;
                if (error is null)
                    target.Tell(unit, finalSender);
                else{
                    var final = errorMapper?.Invoke(error) ?? error;
                    target.Tell(new Status.Failure(final), finalSender);
                }
            });
        }

        /// <summary>
        /// Obsolete. Awaits <paramref name="message"/> on the actor task scheduler and replies with the resulting value
        /// on success or a <see cref="Status.Failure"/> (optionally mapped by <paramref name="errorMapper"/>) on error.
        /// </summary>
        /// <typeparam name="T">The result type produced by <paramref name="message"/>.</typeparam>
        /// <param name="message">The asynchronous work to await.</param>
        /// <param name="errorMapper">Optional transform applied to the thrown exception before sending the failure reply.</param>
        /// <param name="sender">The actor recorded as the sender of the reply; defaults to no sender.</param>
        /// <remarks>Use <see cref="SafeRespond{T}"/> instead, which replies with an <see cref="Outcome{T}"/> rather than a <see cref="Status.Failure"/>.</remarks>
        [PublicAPI, Obsolete("Use SafeRespond instead")]
        public void Respond<T>(ValueTask<T> message,
                               Func<Exception, Exception>? errorMapper = null,
                               IActorRef? sender = null) where T : notnull {
            ActorTaskScheduler.RunTask(async () => {
                var (error, result) = await Try(message).ConfigureAwait(false);
                var finalSender = sender ?? ActorRefs.NoSender;
                if (error is null)
                    target.Tell(result, finalSender);
                else{
                    var final = errorMapper?.Invoke(error) ?? error;
                    target.Tell(new Status.Failure(final), finalSender);
                }
            });
        }

        /// <summary>
        /// Replies to the target with a <c>unit</c> value, signalling completion without a payload.
        /// </summary>
        /// <param name="sender">The actor recorded as the sender of the reply; defaults to no sender.</param>
        [PublicAPI]
        public void TellUnit(IActorRef? sender = null)
            => target.Tell(unit, sender ?? ActorRefs.NoSender);

        /// <summary>
        /// Sends an ask request and returns the reply as an <see cref="Outcome{T}"/>, converting any exception
        /// (including timeouts) into an <see cref="ErrorInfo"/> rather than throwing.
        /// </summary>
        /// <param name="message">The message to ask.</param>
        /// <param name="timeout">The ask timeout; defaults to <c>AkkaInstaller.DefaultAskTimeout</c> seconds when not supplied.</param>
        /// <returns>A successful outcome with the reply object, or a failed outcome describing the error.</returns>
        [PublicAPI]
        public async ValueTask<Outcome<object>> TryAsk(object message, TimeSpan? timeout = null) {
            try{
                return await target.Ask(message, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds()).ConfigureAwait(false);
            }
            catch (Exception e){
                return ErrorFrom.Exception(e);
            }
        }

        /// <summary>
        /// Sends an ask request expecting a reply of type <typeparamref name="T"/> and returns it as an
        /// <see cref="Outcome{T}"/>, converting any exception (including timeouts) into an <see cref="ErrorInfo"/> rather than throwing.
        /// </summary>
        /// <typeparam name="T">The expected reply type.</typeparam>
        /// <param name="message">The message to ask.</param>
        /// <param name="timeout">The ask timeout; defaults to <c>AkkaInstaller.DefaultAskTimeout</c> seconds when not supplied.</param>
        /// <returns>A successful outcome with the typed reply, or a failed outcome describing the error.</returns>
        [PublicAPI]
        public async ValueTask<Outcome<T>> TryAsk<T>(object message, TimeSpan? timeout = null) {
            try{
                return await target.Ask<T>(message, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds()).ConfigureAwait(false);
            }
            catch (Exception e){
                return ErrorFrom.Exception(e);
            }
        }
    }

    /// <summary>
    /// Runs asynchronous work on the actor task scheduler, passing <paramref name="state"/> explicitly to
    /// <paramref name="task"/> so the delegate captures less of the surrounding closure.
    /// </summary>
    /// <typeparam name="T">The type of the state passed to the work.</typeparam>
    /// <param name="_">The actor whose task scheduler is used (the actor itself is unused).</param>
    /// <param name="state">The state forwarded to <paramref name="task"/>.</param>
    /// <param name="task">The asynchronous work to run on the actor scheduler.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AsyncCall<T>(this UntypedActor _, T state, Func<T, ValueTask> task)
        => ActorTaskScheduler.RunTask(async () => await task(state).ConfigureAwait(false));
}
