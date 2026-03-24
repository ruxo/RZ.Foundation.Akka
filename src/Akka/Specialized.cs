using Akka.Actor;
using JetBrains.Annotations;
using LanguageExt.UnitsOfMeasure;
using RZ.Foundation.Types;

namespace RZ.Foundation.Akka;

[PublicAPI]
public interface ICanRaiseError
{
    object RaiseError(ErrorInfo error);
}

[PublicAPI]
public static class CanRaiseErrorExtensions
{
    public static object RaiseError(this ICanRaiseError i, IActorRef target, ErrorInfo error) {
        var message = i.RaiseError(error);
        target.Tell(message);
        return message;
    }
}

[PublicAPI]
public abstract record CanResponse<T> : ICanRaiseError
{
    public async ValueTask<Outcome<T>> RequestTo(ICanTell target, TimeSpan? timeout = null) {
        try{
            return await target.Ask<Outcome<T>>(this, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds()).ConfigureAwait(false);
        }
        catch (Exception e){
            return ErrorFrom.Exception(e);
        }
    }

    public Outcome<T> Respond(IActorRef target, T data) {
        var message = SuccessOutcome(data);
        target.Tell(message);
        return message;
    }

    public object RaiseError(ErrorInfo error) => FailedOutcome<T>(error);
}

[PublicAPI]
public interface ICanSwapActivity
{
    object SwapActivity(ActivityId? activityId);
}

[PublicAPI]
public interface ICanSwapActivity<out T> : ICanSwapActivity where T : ICanSwapActivity<T>
{
    T SwapActivityT(ActivityId? activityId);
}

[PublicAPI]
public abstract record TraceableCommand<T>(ActivityId? ActivityId) : ICanSwapActivity<T> where T: TraceableCommand<T>
{
    public T SwapActivityT(ActivityId? activityId) => (T) this with { ActivityId = activityId };

    public object SwapActivity(ActivityId? activityId) => SwapActivityT(activityId);
}

[PublicAPI]
public abstract record TraceableResponder<TSelf,TRes>(ActivityId? ActivityId) : CanResponse<TRes>, ICanSwapActivity<TSelf> where TSelf : TraceableResponder<TSelf,TRes>
{
    public TSelf SwapActivityT(ActivityId? activityId) => (TSelf) this with { ActivityId = activityId };

    public object SwapActivity(ActivityId? activityId) => SwapActivityT(activityId);
}