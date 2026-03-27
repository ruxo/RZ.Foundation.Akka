using System.Collections.Frozen;
using Akka.Actor;

namespace RZ.Foundation.Akka.Impl;

sealed class AkkaServices : IAKkaServices
{
    public FrozenDictionary<string, IActorRef> Actors = FrozenDictionary<string, IActorRef>.Empty;

    public IActorRef? GetService(string name)
        => Actors.GetValueOrDefault(name);
}