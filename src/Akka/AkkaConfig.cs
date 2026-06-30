using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using JetBrains.Annotations;
using LanguageExt;
using RZ.Foundation.Extensions;
using RZ.Foundation.Types;

namespace RZ.Foundation.Akka;

/// <summary>
/// Typed builder that produces HOCON configuration for an Akka actor system.
/// Produces a simple ask-timeout-only configuration, or a full cluster configuration when
/// <see cref="Nodes"/> is set.
/// </summary>
[PublicAPI]
public class AkkaConfig
{
    /// <summary>
    /// HOCON format-string template for the simple (ask-timeout-only) configuration.
    /// The single placeholder <c>{0}</c> is replaced with the ask-timeout in seconds.
    /// </summary>
    public const string Simple = "akka.actor.ask-timeout = {0}s";

    /// <summary>
    /// Akka's system name
    /// </summary>
    public string? System { get; set; }

    /// <summary>
    /// Maximum wait time in seconds for receiving a response when using the Ask pattern.
    /// If not set, defaults to 10 seconds.
    /// This setting configures akka.actor.ask-timeout in the HOCON configuration.
    /// </summary>
    public int? AskTimeout { get; set; } = 10;

    /// <summary>
    /// <p>Gets or sets the "host:port" address that represents the actor system's own network endpoint.</p>
    /// <p>This property is used for remote actor communication and clustering configurations.</p>
    /// <p>The format should be "hostname:port", "ip:port", "hostname", or "ip".</p>
    /// </summary>
    public string? Self { get; set; }

    /// <summary>
    /// <p>Gets or sets the Akka cluster seed nodes that this actor system should connect to.</p>
    /// <p>Seed nodes are the first nodes contacted when joining the cluster. They act as initial
    /// contact points for cluster formation. At least one seed node must be specified.</p>
    /// <p>Each node requires the format: <c>"hostname:port"</c></p>
    /// <p>If this property is set, the system will use Akka's clustering functionality.</p>
    /// </summary>
    public string[]? Nodes { get; set; }

    /// <summary>
    /// Gets the effective Akka system name: returns <see cref="System"/> when set, otherwise a
    /// sanitized name derived from the executing assembly's name (keeping only letters and digits).
    /// </summary>
    /// <returns>The configured system name, or a sanitized assembly-derived name when none is set.</returns>
    public string GetSystem()
        => System ?? new string(global::System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Builds the HOCON configuration string for the given system name.
    /// When <see cref="Nodes"/> is set, produces a cluster configuration (validating each node's
    /// <c>host:port</c> format and the <see cref="Self"/> endpoint); otherwise produces a simple
    /// ask-timeout-only configuration. The ask-timeout defaults to
    /// <see cref="AkkaInstaller.DefaultAskTimeout"/> when <see cref="AskTimeout"/> is not set.
    /// </summary>
    /// <param name="system">The Akka system name embedded into cluster node addresses.</param>
    /// <returns>
    /// A successful <see cref="Outcome{T}"/> containing the HOCON string, or a failure carrying an
    /// <see cref="ErrorInfo"/> when node formats are invalid or <see cref="Self"/> is missing/malformed
    /// in cluster mode.
    /// </returns>
    public Outcome<string> ToHocon(string system) {
        if (Nodes is { } nodes){
            var result = nodes.Map(n => ParseHostPort(n).IsSome
                                            ? SuccessOutcome($"akka.tcp://{system}@{n}")
                                            : new ErrorInfo(StandardErrorCodes.ValidationFailed, "Akka configuration contains invalid node format"))
                              .MakeList();
            if (Fail(result, out var e, out var akkaNodes)) return e;

            var hostPost = (from self in Optional(Self)
                            from parsed in ParseHostPort(self)
                            select parsed
                           ).ToNullable();
            if (hostPost is null)
                return new ErrorInfo(StandardErrorCodes.ValidationFailed, "Akka configuration is required for Akka cluster");

            var (host, port) = hostPost.Value;
            return CreateClusterConfig(akkaNodes, host, port, AskTimeout ?? AkkaInstaller.DefaultAskTimeout);
        }
        return string.Format(Simple, AskTimeout ?? AkkaInstaller.DefaultAskTimeout);
    }

    /// <summary>
    /// Parses a <c>"host"</c> or <c>"host:port"</c> string into its host and port components.
    /// When the port is omitted, <c>Port</c> is <c>0</c>.
    /// </summary>
    /// <param name="hostPort">The endpoint to parse, e.g. <c>"localhost"</c> or <c>"localhost:8081"</c>.</param>
    /// <returns>
    /// <c>Some</c> tuple of host and port on success; <c>None</c> when the input is malformed
    /// (neither a single host nor a host/port pair).
    /// </returns>
    public static Option<(string Host, int Port)> ParseHostPort(string hostPort)
        => hostPort.Split(':', StringSplitOptions.RemoveEmptyEntries) switch {
            [var host]           => (host.Trim(), 0),
            [var host, var port] => (host.Trim(), int.Parse(port)),

            _ => None
        };

    /// <summary>
    /// Creates a <see cref="BootstrapSetup"/> from a HOCON configuration string, parsing it via
    /// <see cref="ConfigurationFactory"/>.
    /// </summary>
    /// <param name="hocon">The HOCON configuration text.</param>
    /// <returns>A <see cref="Setup"/> wrapping the parsed configuration, ready to start an actor system.</returns>
    public static Setup Bootstrap(string hocon)
        => BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon));

    /// <summary>
    /// Builds a cluster HOCON configuration string (cluster provider, TCP remote transport, and seed
    /// nodes), validating the supplied parameters.
    /// </summary>
    /// <param name="seedNodes">The cluster seed-node addresses; at least one is required.</param>
    /// <param name="hostName">The host name or IP this node binds to; must not be empty.</param>
    /// <param name="port">The remote TCP port (0 to bind a random port); must be in the range 0-65535.</param>
    /// <param name="defaultAskTimeout">The ask-timeout in seconds; must be greater than 0.</param>
    /// <returns>
    /// A successful <see cref="Outcome{T}"/> containing the cluster HOCON, or a failure carrying an
    /// <see cref="ErrorInfo"/> when no seed nodes are given, the timeout is less than 1, the port is
    /// out of range, or the host name is blank.
    /// </returns>
    public static Outcome<string> CreateClusterConfig(IReadOnlyList<string> seedNodes, string hostName, int port = 0, int defaultAskTimeout = 10) {
        if (seedNodes.Count == 0) return new ErrorInfo(StandardErrorCodes.ValidationFailed, "At least one seed node is required");
        if (defaultAskTimeout < 1) return new ErrorInfo(StandardErrorCodes.ValidationFailed, "Default ask timeout must be greater than 0");

        if (port is < 0 or > 65535) return new ErrorInfo(StandardErrorCodes.ValidationFailed, $"Port value is invalid {port}");
        if (string.IsNullOrWhiteSpace(hostName)) return new ErrorInfo(StandardErrorCodes.ValidationFailed, "Host name cannot be empty");

        return $@"
akka.actor.ask-timeout = {defaultAskTimeout}s
akka.actor.provider = cluster
akka.remote.dot-netty.tcp {{
    port = {port}
    hostname = {hostName}
}}
akka.cluster.seed-nodes = [{seedNodes.Map(s => $"\"{s}\"").Join(',')}]
";
    }
}
