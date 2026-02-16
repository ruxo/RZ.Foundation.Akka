using Akka.Actor;
using JetBrains.Annotations;
using LanguageExt.UnitsOfMeasure;
using RZ.Foundation.Types;

namespace RZ.Foundation.Akka;

public abstract record CanResponse<T>
{
    [PublicAPI]
    public async ValueTask<Outcome<T>> RequestTo(ICanTell target, TimeSpan? timeout = null) {
        try{
            return await target.Ask<Outcome<T>>(this, timeout ?? AkkaInstaller.DefaultAskTimeout.Seconds()).ConfigureAwait(false);
        }
        catch (Exception e){
            return ErrorFrom.Exception(e);
        }
    }

}