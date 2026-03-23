using System.Runtime.CompilerServices;
using Akka.Actor;
using JetBrains.Annotations;
using LanguageExt.UnitsOfMeasure;
using RZ.Foundation.Types;

namespace RZ.Foundation.Akka;

[PublicAPI]
public interface ICanRaiseError
{
    object RaiseError(IActorRef target, ErrorInfo error);
    object RaiseError(ErrorInfo error);
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

    public object RaiseError(IActorRef target, ErrorInfo error) {
        var message = CreateError(error);
        target.Tell(message);
        return message;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Outcome<T> CreateError(ErrorInfo error) => FailedOutcome<T>(error);

    public object RaiseError(ErrorInfo error) => CreateError(error);
}