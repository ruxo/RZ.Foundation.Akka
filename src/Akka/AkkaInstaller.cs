using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.DependencyInjection;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RZ.Foundation.Akka;

/// <summary>
/// Builds the <see cref="Props"/> used to spawn a registered actor. Receives the root
/// <see cref="IServiceProvider"/>; the actor is then spawned via <c>system.ActorOf(props, registration.Name)</c>.
/// </summary>
/// <param name="sp">The root service provider, for resolving dependencies needed to build the props.</param>
/// <returns>The props describing how to create the actor.</returns>
public delegate Props     ActorPropsFactory(IServiceProvider sp);

/// <summary>
/// Creates and returns a registered actor's <see cref="IActorRef"/> directly. Use this when you need
/// full control over how the actor is built or named, rather than letting the system spawn it from props.
/// </summary>
/// <param name="sp">The root service provider, for resolving dependencies.</param>
/// <param name="system">The actor system the actor is created in.</param>
/// <returns>The reference to the created actor.</returns>
public delegate IActorRef ActorFactory(IServiceProvider sp, ActorSystem system);

/// <summary>
/// Describes an actor that is created when the Akka system starts. Registered actors are exposed
/// through <see cref="IAkkaServices"/> keyed by <paramref name="Name"/>.
/// </summary>
/// <param name="Name">
/// The lookup key under which the actor is exposed via <see cref="IAkkaServices.GetService"/>. For a
/// <paramref name="PropsFactory"/> registration this is also the actor's name (path segment), because the
/// actor is spawned as <c>system.ActorOf(props, Name)</c>. For an <paramref name="ActorFactory"/> registration
/// it is only the lookup key and need not equal the actor's actual path name.
/// </param>
/// <param name="PropsFactory">Optional factory that builds the actor's <see cref="Props"/>.</param>
/// <param name="ActorFactory">
/// Optional factory that creates the actor directly. If both <paramref name="PropsFactory"/> and
/// <paramref name="ActorFactory"/> are supplied, <paramref name="ActorFactory"/> takes precedence and
/// <paramref name="PropsFactory"/> is ignored.
/// </param>
/// <remarks>
/// A registration is only used at startup when <see cref="IsValid"/> is <c>true</c> (at least one factory set);
/// an invalid registration is skipped and a warning is logged.
/// </remarks>
public sealed record ActorRegistration(string Name, ActorPropsFactory? PropsFactory = null, ActorFactory? ActorFactory = null)
{
    /// <summary>
    /// <c>true</c> when at least one of <see cref="PropsFactory"/> or <see cref="ActorFactory"/> is set.
    /// An invalid registration (neither factory) is skipped during startup and a warning is logged.
    /// </summary>
    public bool IsValid => PropsFactory is not null || ActorFactory is not null;
}

/// <summary>
/// Provides access to actors registered at system startup, exposing each registered actor as a named
/// service (an "actor as a service" lookup). Resolve it from DI, e.g.
/// <c>serviceProvider.GetRequiredService&lt;IAKkaServices&gt;()</c>.
/// </summary>
[PublicAPI]
public interface IAkkaServices
{
    /// <summary>
    /// Returns the actor registered under <paramref name="name"/>, treating a registered actor as a
    /// resolvable named service.
    /// </summary>
    /// <param name="name">The registration name (lookup key) of the actor.</param>
    /// <returns>The actor's reference, or <c>null</c> if no actor was registered under that name.</returns>
    IActorRef? GetService(string name);
}

/// <summary>
/// Extension methods that register an Akka.NET <see cref="ActorSystem"/> (and its startup actors) into an
/// <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class AkkaInstaller
{
    /// <summary>The default Ask-pattern timeout, in seconds, used when no timeout is supplied.</summary>
    public const int DefaultAskTimeout = 10;

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers an Akka.NET <see cref="ActorSystem"/> as a singleton together with the given actor
        /// registrations, using <see cref="DefaultAskTimeout"/>. The system and its registered actors are created
        /// eagerly at host startup via a hosted service and shut down gracefully (Akka CoordinatedShutdown) when
        /// the host stops. Only one Akka system may be registered per container.
        /// </summary>
        /// <param name="systemName">The name of the actor system.</param>
        /// <param name="registrations">Actors to create when the system starts.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, params ActorRegistration[] registrations)
            => services.AddAkkaSystem(systemName, DefaultAskTimeout, registrations);

        /// <summary>
        /// Registers an Akka.NET <see cref="ActorSystem"/> as a singleton together with the given actor
        /// registrations, configured with the supplied default Ask-pattern timeout. The system and its registered
        /// actors are created eagerly at host startup via a hosted service and shut down gracefully (Akka
        /// CoordinatedShutdown) when the host stops. Only one Akka system may be registered per container.
        /// </summary>
        /// <param name="systemName">The name of the actor system.</param>
        /// <param name="defaultAskTimeout">The default Ask-pattern timeout, in seconds.</param>
        /// <param name="registrations">Actors to create when the system starts.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="defaultAskTimeout"/> is less than 1.</exception>
        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, int defaultAskTimeout, params ActorRegistration[] registrations) {
            if (defaultAskTimeout < 1) throw new ArgumentOutOfRangeException(nameof(defaultAskTimeout));
            return services.AddAkkaSystem(systemName, string.Format(AkkaConfig.Simple, defaultAskTimeout), registrations);
        }

        /// <summary>
        /// Registers an Akka.NET <see cref="ActorSystem"/> as a singleton together with the given actor
        /// registrations, configuring the system from a raw HOCON string. The system and its registered actors are
        /// created eagerly at host startup via a hosted service and shut down gracefully (Akka CoordinatedShutdown)
        /// when the host stops. Only one Akka system may be registered per container.
        /// </summary>
        /// <param name="systemName">The name of the actor system.</param>
        /// <param name="hocon">The raw HOCON configuration for the system.</param>
        /// <param name="registrations">Actors to create when the system starts.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, string hocon, params ActorRegistration[] registrations)
            => services.AddAkkaSystem(systemName, [AkkaConfig.Bootstrap(hocon)], registrations);

        /// <summary>
        /// Registers an Akka.NET <see cref="ActorSystem"/> as a singleton together with the given actor
        /// registrations, configuring the system from the supplied <see cref="Setup"/> entries. This is the
        /// lowest-level overload that all other <c>AddAkkaSystem</c> overloads funnel into. The system and its
        /// registered actors are created eagerly at host startup via a hosted service and shut down gracefully
        /// (Akka CoordinatedShutdown) when the host stops. Only one Akka system may be registered per container.
        /// </summary>
        /// <param name="systemName">The name of the actor system.</param>
        /// <param name="configs">The setup entries used to configure the system.</param>
        /// <param name="registrations">Actors to create when the system starts.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="systemName"/> is null or blank.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an <see cref="ActorSystem"/> has already been registered, since only a single system is supported.</exception>
        [PublicAPI]
        public IServiceCollection AddAkkaSystem(string systemName, Setup[] configs, params ActorRegistration[] registrations) {
            if (string.IsNullOrWhiteSpace(systemName)) throw new ArgumentException("System name is required.", nameof(systemName));

            if (services.Any(s => s.ServiceType == typeof(ActorSystem)))
                throw new InvalidOperationException("An Akka system has already been registered; multiple systems are not supported.");

            var lookup = new Impl.AkkaServices();
            services.AddSingleton(lookup)
                    .AddSingleton<IAkkaServices>(sp => {
                         // Force `lookup.Actors` to be populated.
                         sp.GetRequiredService<ActorSystem>();
                         return lookup;
                     })
                    .AddSingleton(sp => {
                         var logger = sp.GetRequiredService<ILogger<ActorRegistration>>();

                         var diSetup = DependencyResolverSetup.Create(sp);
                         var setup = ActorSystemSetup.Create(configs.Append(diSetup).ToArray());
                         var system = ActorSystem.Create(systemName, setup);
                         var dict = new Dictionary<string, IActorRef>();
                         foreach (var r in registrations)
                             if (r.IsValid){
                                 var (name, pFactory, aFactory) = r;
                                 dict[name] = aFactory is null
                                                  ? system.ActorOf(pFactory!(sp), name)
                                                  : aFactory(sp, system);
                             }
                             else
                                 logger.LogWarning("Actor registration {Name} is invalid. Actor registration skipped!", r.Name);
                         lookup.Actors = dict.ToFrozenDictionary();
                         return system;
                     })
                    .AddHostedService<Impl.AkkaStartup>();
            return services;
        }

        /// <summary>
        /// Registers an Akka.NET <see cref="ActorSystem"/> as a singleton together with the given actor
        /// registrations, building the configuration from an <see cref="AkkaConfig"/> (simple or cluster). The
        /// system and its registered actors are created eagerly at host startup via a hosted service and shut down
        /// gracefully (Akka CoordinatedShutdown) when the host stops. Only one Akka system may be registered per
        /// container.
        /// </summary>
        /// <param name="config">The typed configuration describing the system.</param>
        /// <param name="registrations">Actors to create when the system starts.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        [PublicAPI]
        public IServiceCollection AddAkkaSystem(AkkaConfig config, params ActorRegistration[] registrations) {
            var system = config.GetSystem();
            return services.AddAkkaSystem(system, config.ToHocon(system).Unwrap(), registrations);
        }
    }

    extension(Props props)
    {
        /// <summary>Spawns the actor described by these props at system scope; a convenience wrapper over <c>system.ActorOf(props, name)</c>.</summary>
        /// <param name="system">The actor system to spawn the actor in.</param>
        /// <param name="name">The name (path segment) of the new actor.</param>
        /// <returns>A reference to the spawned actor.</returns>
        [PublicAPI, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IActorRef Spawn(ActorSystem system, string name)
            => system.ActorOf(props, name);

        /// <summary>Spawns the actor described by these props as a child at context scope; a convenience wrapper over <c>context.ActorOf(props, name)</c>.</summary>
        /// <param name="context">The parent actor context to spawn the child actor in.</param>
        /// <param name="name">The name (path segment) of the new actor.</param>
        /// <returns>A reference to the spawned actor.</returns>
        [PublicAPI, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IActorRef Spawn(IUntypedActorContext context, string name)
            => context.ActorOf(props, name);
    }
}
