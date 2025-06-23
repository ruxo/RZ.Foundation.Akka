using System.Configuration;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.DependencyInjection;
using JetBrains.Annotations;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;

namespace RZ.Foundation.Akka;

[PublicAPI]
public static class AkkInstaller
{
    public const int DefaultAskTimeout = 10;

    public static IServiceCollection AddAkkaSystem(this IServiceCollection services, string systemName, int defaultAskTimeout = DefaultAskTimeout) {
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

    public static IServiceCollection AddAkkaSystem(this IServiceCollection builder, AkkaConfig config){
        var system = config.System ?? new string(System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!.Where(char.IsLetterOrDigit).ToArray());

        if (config.Nodes is { } nodes){
            var akkaNodes = nodes.Map(n => ParseHostPort(n).IsSome
                                               ? $"akka.tcp://{system}@{n}"
                                               : throw new ConfigurationErrorsException("Akka configuration contains invalid node format")).ToArray();
            var (host, port) = (from self in Optional(config.Self)
                                from parsed in ParseHostPort(self)
                                select parsed
                               ).ToNullable() ?? throw new ConfigurationErrorsException("Akka configuration is required for Akka cluster");
            var hocon = AkkaConfig.CreateClusterConfig(akkaNodes, host, port, config.AskTimeout ?? DefaultAskTimeout);

            builder.AddAkkaSystem(system, hocon);
        }
        else
            builder.AddAkkaSystem(system, config.AskTimeout ?? DefaultAskTimeout);

        return builder;
    }

    public static Option<(string Host, int Port)> ParseHostPort(string hostPort)
        => hostPort.Split(':', StringSplitOptions.RemoveEmptyEntries) switch {
            [var host]           => (host.Trim(), 0),
            [var host, var port] => (host.Trim(), int.Parse(port)),

            _ => None
        };
}