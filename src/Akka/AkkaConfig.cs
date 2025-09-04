using System.Configuration;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using JetBrains.Annotations;
using LanguageExt;
using RZ.Foundation.Extensions;

namespace RZ.Foundation.Akka;

[PublicAPI]
public class AkkaConfig
{
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

    public string GetSystem()
        => System ?? new string(global::System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!.Where(char.IsLetterOrDigit).ToArray());

    public string ToHocon(string system) {
        if (Nodes is { } nodes){
            var akkaNodes = nodes.Map(n => ParseHostPort(n).IsSome
                                               ? $"akka.tcp://{system}@{n}"
                                               : throw new ConfigurationErrorsException("Akka configuration contains invalid node format")).ToArray();
            var (host, port) = (from self in Optional(Self)
                                from parsed in ParseHostPort(self)
                                select parsed
                               ).ToNullable() ?? throw new ConfigurationErrorsException("Akka configuration is required for Akka cluster");
            return CreateClusterConfig(akkaNodes, host, port, AskTimeout ?? AkkaInstaller.DefaultAskTimeout);
        }
        else
            return string.Format(Simple, AskTimeout ?? AkkaInstaller.DefaultAskTimeout);
    }

    public static Option<(string Host, int Port)> ParseHostPort(string hostPort)
        => hostPort.Split(':', StringSplitOptions.RemoveEmptyEntries) switch {
            [var host]           => (host.Trim(), 0),
            [var host, var port] => (host.Trim(), int.Parse(port)),

            _ => None
        };

    public static Setup Bootstrap(string hocon)
        => BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(hocon));

    public static string CreateClusterConfig(string[] seedNodes, string hostName, int port = 0, int defaultAskTimeout = 10) {
        if (seedNodes.Length == 0) throw new ArgumentException("At least one seed node is required");
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultAskTimeout, 1);
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(hostName)) throw new ArgumentException(nameof(hostName));

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