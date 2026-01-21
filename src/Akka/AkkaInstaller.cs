using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace RZ.Foundation.Akka;

[PublicAPI]
public static class AkkaInstaller
{
    public const int DefaultAskTimeout = 10;

    extension(IServiceCollection services)
    {
        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, int defaultAskTimeout = DefaultAskTimeout) {
            if (defaultAskTimeout < 1) throw new ArgumentOutOfRangeException(nameof(defaultAskTimeout));
            return AddAkkaSystem(services, systemName, string.Format(AkkaConfig.Simple, defaultAskTimeout));
        }

        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, string hocon)
            => services.AddAkkaSystem(systemName, [AkkaConfig.Bootstrap(hocon)]);

        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, Setup[] configs) {
            if (string.IsNullOrWhiteSpace(systemName)) throw new ArgumentException(nameof(systemName));

            services.AddSingleton(sp => {
                var diSetup = DependencyResolverSetup.Create(sp);
                var setup = ActorSystemSetup.Create(configs.Append(diSetup).ToArray());
                return ActorSystem.Create(systemName, setup);
            });
            return services;
        }

        [PublicAPI]
        public IServiceCollection AddAkkaSystem(AkkaConfig config) {
            var system = config.GetSystem();
            return services.AddAkkaSystem(system, config.ToHocon(system).Unwrap());
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