using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace RZ.Foundation.Akka;

[PublicAPI]
public static class AkkInstaller
{
    public static IServiceCollection AddAkkaSystem(this IServiceCollection services, string systemName, int defaultAskTimeout = 10) {
        if (defaultAskTimeout < 1) throw new ArgumentOutOfRangeException(nameof(defaultAskTimeout));
        return AddAkkaSystem(services, systemName, string.Format(AkkaConfig.Simple, defaultAskTimeout));
    }

    public static IServiceCollection AddAkkaSystem(this IServiceCollection services, string systemName, string hocon) {
        if (string.IsNullOrWhiteSpace(systemName)) throw new ArgumentException(nameof(systemName));

        services.AddSingleton(sp => {
            var config = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon));
            var diSetup = DependencyResolverSetup.Create(sp);
            var setup = ActorSystemSetup.Create(config, diSetup);
            return ActorSystem.Create(systemName, setup);
        });
        return services;
    }
}