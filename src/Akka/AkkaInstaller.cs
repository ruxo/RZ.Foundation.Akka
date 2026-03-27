using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace RZ.Foundation.Akka;

public delegate Props ActorPropsFactory(IServiceProvider sp);
public delegate IActorRef ActorFactory(IServiceProvider sp, ActorSystem system);

public sealed record ActorRegistration(string Name, ActorPropsFactory? PropsFactory = null, ActorFactory? ActorFactory = null)
{
    public bool IsValid => PropsFactory is not null || ActorFactory is not null;
}

[PublicAPI]
public interface IAKkaServices
{
    IActorRef? GetService(string name);
}

[PublicAPI]
public static class AkkaInstaller
{
    public const int DefaultAskTimeout = 10;

    extension(IServiceCollection services)
    {
        [PublicAPI, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IServiceCollection AddAkkaSystem(string systemName, params ActorRegistration[] registrations)
            => services.AddAkkaSystem(systemName, DefaultAskTimeout, registrations);

        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, int defaultAskTimeout, params ActorRegistration[] registrations) {
            if (defaultAskTimeout < 1) throw new ArgumentOutOfRangeException(nameof(defaultAskTimeout));
            return services.AddAkkaSystem(systemName, string.Format(AkkaConfig.Simple, defaultAskTimeout), registrations);
        }

        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, string hocon, params ActorRegistration[] registrations)
            => services.AddAkkaSystem(systemName, [AkkaConfig.Bootstrap(hocon)], registrations);

        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, Setup[] configs, params ActorRegistration[] registrations) {
            if (string.IsNullOrWhiteSpace(systemName)) throw new ArgumentException(nameof(systemName));

            var lookup = new Impl.AkkaServices();
            services.AddSingleton<IAKkaServices>(lookup)
                    .AddSingleton(sp => {
                         var diSetup = DependencyResolverSetup.Create(sp);
                         var setup = ActorSystemSetup.Create(configs.Append(diSetup).ToArray());
                         var system = ActorSystem.Create(systemName, setup);
                         var dict = new Dictionary<string, IActorRef>();
                         foreach (var (name, pFactory, aFactory) in registrations.Where(r => r.IsValid))
                                 dict[name] = aFactory is null
                                                  ? system.ActorOf(pFactory!(sp), name)
                                                  : aFactory(sp, system);
                         lookup.Actors = dict.ToFrozenDictionary();
                         return system;
                     });
            return services;
        }

        [PublicAPI]
        public IServiceCollection AddAkkaSystem(AkkaConfig config, params ActorRegistration[] registrations) {
            var system = config.GetSystem();
            return services.AddAkkaSystem(system, config.ToHocon(system).Unwrap(), registrations);
        }
    }

    extension(Props props)
    {
        [PublicAPI, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IActorRef Spawn(ActorSystem system, string name)
            => system.ActorOf(props, name);

        [PublicAPI, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IActorRef Spawn(IUntypedActorContext context, string name)
            => context.ActorOf(props, name);
    }
}