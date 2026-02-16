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

public interface ICanResponse;

// ReSharper disable once UnusedTypeParameter
public interface ICanResponse<T> : ICanResponse;

[PublicAPI]
public static class ActorExtension
{
    extension(ActorSystem sys)
    {
        [PublicAPI, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Props DependencyProps<T>(params object[] parameters) where T : ActorBase =>
            DependencyResolver.For(sys).Props<T>(parameters);

        [PublicAPI]
        public Task CoordinatedShutdown(CS.Reason? reason = null)
            => CS.Get(sys).Run(reason ?? CS.ClrExitReason.Instance);

        [PublicAPI]
        public IActorRef CreateWithProps<T>(string name, Func<Props,Props> setter, params object[] parameters) where T : ActorBase =>
            sys.ActorOf(setter(sys.DependencyProps<T>(parameters)), name);

        [PublicAPI]
        public IActorRef CreateActor<T>(string name, params object[] parameters) where T : ActorBase =>
            sys.ActorOf(sys.DependencyProps<T>(parameters), name);
    }

    [PublicAPI]
    public static IActorRef CreateActor<T>(this IUntypedActorContext context, string name, params object[] parameters) where T : ActorBase =>
        context.ActorOf(context.System.DependencyProps<T>(parameters), name);

    extension(ICanTell target)
    {
        [PublicAPI]
        public void SafeRespond<T>(ValueTask<Outcome<T>> message, Func<ErrorInfo, ErrorInfo>? errorMapper = null, IActorRef? sender = null) {
            ActorTaskScheduler.RunTask(async () => {
                if (Fail(await message.ConfigureAwait(false), out var error, out var result))
                    target.RespondError<T>(errorMapper?.Invoke(error) ?? error, sender);
                else
                    target.RespondSuccess(result, sender);
            });
        }

        [PublicAPI]
        public void RespondSuccess<T>(in T data, IActorRef? sender = null)
            => target.Tell(SuccessOutcome(data), sender ?? ActorRefs.NoSender);

        [PublicAPI]
        public void RespondError<T>(ErrorInfo error, IActorRef? sender = null)
            => target.Tell(FailedOutcome<T>(error), sender ?? ActorRefs.NoSender);

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

        [PublicAPI]
        public void TellUnit(IActorRef? sender = null)
            => target.Tell(unit, sender ?? ActorRefs.NoSender);


        /// <summary>
        /// Sends a request message to the target actor and awaits a response of type <see cref="Outcome{R}"/>.
        /// </summary>
        /// <typeparam name="T">The type of the request message, which must implement <see cref="ICanResponse{R}"/>.</typeparam>
        /// <typeparam name="R">The type of the response expected from the target actor.</typeparam>
        /// <param name="message">The request message to be sent to the target actor.</param>
        /// <param name="timeout">
        /// Optional timeout duration for the request. If not specified, the default ask timeout defined in <see cref="AkkaInstaller.DefaultAskTimeout"/> is used.
        /// </param>
        /// <returns>An outcome containing the result of type <typeparamref name="R"/> or an error.</returns>
        [PublicAPI]
        public async ValueTask<Outcome<R>> Request<T, R>(T message, TimeSpan? timeout = null) where T : ICanResponse<R> {
            try{
                return await target.Ask<Outcome<R>>(message, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds()).ConfigureAwait(false);
            }
            catch (Exception e){
                return ErrorFrom.Exception(e);
            }
        }

        [PublicAPI]
        public async ValueTask<Outcome<object>> TryAsk(object message, TimeSpan? timeout = null) {
            try{
                return await target.Ask(message, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds()).ConfigureAwait(false);
            }
            catch (Exception e){
                return ErrorFrom.Exception(e);
            }
        }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AsyncCall<T>(this UntypedActor _, T state, Func<T, ValueTask> task)
        => ActorTaskScheduler.RunTask(async () => await task(state).ConfigureAwait(false));
}