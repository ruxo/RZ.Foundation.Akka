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
        public void Respond(ValueTask message,
                            Func<Exception, Exception>? errorMapper = null,
                            IActorRef? sender = null) {
            ActorTaskScheduler.RunTask(async () => {
                var (error, _) = await Try(message);
                var finalSender = sender ?? ActorRefs.NoSender;
                if (error is null)
                    target.Tell(unit, finalSender);
                else{
                    var final = errorMapper?.Invoke(error) ?? error;
                    target.Tell(new Status.Failure(final), finalSender);
                }
            });
        }

        [PublicAPI]
        public void Respond<T>(ValueTask<T> message,
                               Func<Exception, Exception>? errorMapper = null,
                               IActorRef? sender = null) where T : notnull {
            ActorTaskScheduler.RunTask(async () => {
                var (error, result) = await Try(message);
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

        [PublicAPI]
        public async ValueTask<Outcome<object>> TryAsk(object message, TimeSpan? timeout = null) {
            try{
                return await target.Ask(message, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds());
            }
            catch (Exception e){
                return ErrorFrom.Exception(e);
            }
        }

        [PublicAPI]
        public async ValueTask<Outcome<T>> TryAsk<T>(object message, TimeSpan? timeout = null) {
            try{
                return await target.Ask<T>(message, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds());
            }
            catch (Exception e){
                return ErrorFrom.Exception(e);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AsyncCall<T>(this UntypedActor _, T state, Func<T, ValueTask> task)
        => ActorTaskScheduler.RunTask(async () => await task(state));
}