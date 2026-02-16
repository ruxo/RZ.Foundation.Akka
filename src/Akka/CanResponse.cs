using Akka.Actor;
using JetBrains.Annotations;
using LanguageExt.UnitsOfMeasure;
using RZ.Foundation.Types;

namespace RZ.Foundation.Akka;

[PublicAPI]
public abstract record CanResponse<T>
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

    public Outcome<T> RaiseError(IActorRef target, ErrorInfo error) {
        var message = FailedOutcome<T>(error);
        target.Tell(message);
        return message;
    }
}