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
    const string ConfigHocon = "akka.actor.ask-timeout = {0}s";

    public static IServiceCollection AddAkkaSystem(this IServiceCollection services, string systemName, int defaultAskTimeout = 10) {
        if (string.IsNullOrWhiteSpace(systemName)) throw new ArgumentException(nameof(systemName));
        if (defaultAskTimeout < 1) throw new ArgumentOutOfRangeException(nameof(defaultAskTimeout));

        services.AddSingleton(sp => {
            var finalConfig = string.Format(ConfigHocon, defaultAskTimeout);
            var config = BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(finalConfig));
            var diSetup = DependencyResolverSetup.Create(sp);
            var setup = ActorSystemSetup.Create(config, diSetup);
            return ActorSystem.Create(systemName, setup);
        });
        return services;
    }
}