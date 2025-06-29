﻿using Akka.Actor;
using Akka.Actor.Setup;
using Akka.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace RZ.Foundation.Akka;

[PublicAPI]
public static class AkkaInstaller
{
    public const int DefaultAskTimeout = 10;

    public static IServiceCollection AddAkkaSystem(this IServiceCollection services, string systemName, int defaultAskTimeout = DefaultAskTimeout) {
        if (defaultAskTimeout < 1) throw new ArgumentOutOfRangeException(nameof(defaultAskTimeout));
        return AddAkkaSystem(services, systemName, string.Format(AkkaConfig.Simple, defaultAskTimeout));
    }

    public static IServiceCollection AddAkkaSystem(this IServiceCollection services, string systemName, string hocon) =>
        services.AddAkkaSystem(systemName, AkkaConfig.Bootstrap(hocon));

    public static IServiceCollection AddAkkaSystem(this IServiceCollection services, string systemName, params Setup[] configs) {
        if (string.IsNullOrWhiteSpace(systemName)) throw new ArgumentException(nameof(systemName));

        services.AddSingleton(sp => {
            var diSetup = DependencyResolverSetup.Create(sp);
            var setup = ActorSystemSetup.Create(configs.Append(diSetup).ToArray());
            return ActorSystem.Create(systemName, setup);
        });
        return services;
    }

    public static IServiceCollection AddAkkaSystem(this IServiceCollection builder, AkkaConfig config){
        var system = config.System ?? new string(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!.Where(char.IsLetterOrDigit).ToArray());

        builder.AddAkkaSystem(system, config.ToHocon(system));

        return builder;
    }
}